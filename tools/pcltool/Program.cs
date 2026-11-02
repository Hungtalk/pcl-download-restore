using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("usage:");
            Console.WriteLine("  pcltool scan <exe> [keyword1,keyword2,...]");
            Console.WriteLine("  pcltool strings <exe> <keyword>");
            Console.WriteLine("  pcltool dump <exe> <typeSub> <methodSub>");
            Console.WriteLine("  pcltool type <exe> <typeSub>");
            Console.WriteLine("  pcltool patch <exe> <typeSub> <methodSub> [ret|ldnull_ret]");
            return 1;
        }

        string cmd = args[0];
        string path = Path.GetFullPath(args[1]);

        using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        using var pe = new PEReader(fs);
        if (!pe.HasMetadata) { Console.WriteLine("not a managed assembly"); return 2; }
        var md = pe.GetMetadataReader();

        switch (cmd)
        {
            case "scan": return Scan(pe, md, args.Length > 2 ? args[2].Split(',') : DefaultKeywords);
            case "strings": return Strings(pe, md, args[2]);
            case "dump": return Dump(pe, md, args[2], args[3]);
            case "type": return DumpType(pe, md, args[2]);
            case "patch": return Patch(fs, pe, md, args[2], args[3], args.Length > 4 ? args[4] : "ret");
            default: Console.WriteLine("unknown command"); return 1;
        }
    }

    private static readonly string[] DefaultKeywords =
    {
        "为遵守相关法律法规",
        "暂时不再提供",
        "下载功能",
        "关闭下载",
        "无法下载",
        "已关闭"
    };

    private static IEnumerable<(MethodDefinitionHandle Handle, MethodDefinition Def, string TypeName, string MethodName, byte[] Il, int IlFileOffset)> Enumerate(PEReader pe, MetadataReader md)
    {
        foreach (var handle in md.MethodDefinitions)
        {
            var def = md.GetMethodDefinition(handle);
            if (def.RelativeVirtualAddress == 0) continue;
            MethodBodyBlock body;
            try { body = pe.GetMethodBody(def.RelativeVirtualAddress); }
            catch { continue; }
            byte[] il = body.GetILBytes() ?? Array.Empty<byte>();
            if (il.Length == 0) continue;
            int header = (il.Length > 0 && (ReadByte(pe, def.RelativeVirtualAddress) & 3) == 2) ? 1 : 12;
            int bodyOffset = RvaToOffset(pe, def.RelativeVirtualAddress);
            string typeName = TypeNameOf(md, def);
            string methodName = md.GetString(def.Name);
            yield return (handle, def, typeName, methodName, il, bodyOffset < 0 ? -1 : bodyOffset + header);
        }
    }

    private static byte ReadByte(PEReader pe, int rva)
    {
        int off = RvaToOffset(pe, rva);
        var block = pe.GetSectionData(rva);
        return block.Length > 0 ? block.GetContent(0, 1)[0] : (byte)0;
    }

    private static string TypeNameOf(MetadataReader md, MethodDefinition def)
    {
        var td = md.GetTypeDefinition(def.GetDeclaringType());
        string name = md.GetString(td.Name);
        string ns = md.GetString(td.Namespace);
        if (td.IsNested)
        {            // walk up the nesting chain for a readable "Outer+Inner" name
            var parts = new List<string> { name };
            var parentHandle = td.GetDeclaringType();
            while (true)
            {
                var parent = md.GetTypeDefinition(parentHandle);
                parts.Insert(0, md.GetString(parent.Name));
                if (!parent.IsNested) { ns = md.GetString(parent.Namespace); break; }
                parentHandle = parent.GetDeclaringType();
            }
            name = string.Join("+", parts);
        }
        if (string.IsNullOrEmpty(ns)) return name;
        return ns + "." + name;
    }

    private static int RvaToOffset(PEReader pe, int rva)
    {
        foreach (var s in pe.PEHeaders.SectionHeaders)
        {
            if (rva >= s.VirtualAddress && rva < s.VirtualAddress + Math.Max(s.VirtualSize, s.SizeOfRawData))
                return rva - s.VirtualAddress + s.PointerToRawData;
        }
        return -1;
    }

    private static string UserString(MetadataReader md, int token)
    {
        try
        {
            var h = MetadataTokens.UserStringHandle(token & 0x00FFFFFF);
            return md.GetUserString(h);
        }
        catch { return null; }
    }

    private static IEnumerable<(int Offset, string Value)> LdStrRefs(MetadataReader md, byte[] il)
    {
        for (int i = 0; i + 4 < il.Length; i++)
        {
            if (il[i] != 0x72) continue;
            int token = BitConverter.ToInt32(il, i + 1);
            if ((token & 0xFF000000) != 0x70000000) continue;
            string s = UserString(md, token);
            if (s != null) yield return (i, s);
        }
    }

    private static int Scan(PEReader pe, MetadataReader md, string[] keywords)
    {
        int n = 0;
        foreach (var (handle, def, typeName, methodName, il, fileOff) in Enumerate(pe, md))
        {
            var hits = LdStrRefs(md, il).Where(r => keywords.Any(k => (r.Value ?? "").Contains(k, StringComparison.Ordinal))).ToList();
            if (hits.Count == 0) continue;
            n++;
            Console.WriteLine($"[{typeName}::{methodName}] ilsize={il.Length} file=0x{fileOff:X}");
            foreach (var (off, val) in hits) Console.WriteLine($"    +0x{off:X3} : {Shorten(val)}");
        }
        Console.WriteLine($"-- {n} methods");
        return 0;
    }

    private static string Shorten(string s)
    {
        if (s == null) return "<null>";
        s = s.Replace("\r", "\\r").Replace("\n", "\\n");
        return s.Length > 160 ? s.Substring(0, 160) + "…" : s;
    }

    private static int Strings(PEReader pe, MetadataReader md, string keyword)
    {
        var seen = new HashSet<int>();
        int n = 0;
        foreach (var (handle, def, typeName, methodName, il, fileOff) in Enumerate(pe, md))
        {
            for (int i = 0; i + 4 < il.Length; i++)
            {
                if (il[i] != 0x72) continue;
                int token = BitConverter.ToInt32(il, i + 1);
                if ((token & 0xFF000000) != 0x70000000) continue;
                int off = token & 0x00FFFFFF;
                if (!seen.Add(off)) continue;
                string s = UserString(md, token);
                if (s != null && (keyword == "*" || s.Contains(keyword, StringComparison.Ordinal)))
                {
                    Console.WriteLine($"0x{off:X6} [{typeName}::{methodName}] : {Shorten(s)}");
                    n++;
                }
            }
        }
        Console.WriteLine($"-- {n} matches");
        return 0;
    }

    private static int Dump(PEReader pe, MetadataReader md, string typeSub, string methodSub)
    {
        int n = 0;
        foreach (var (handle, def, typeName, methodName, il, fileOff) in Enumerate(pe, md))
        {
            if (!typeName.Contains(typeSub, StringComparison.Ordinal)) continue;
            if (!methodName.Contains(methodSub, StringComparison.Ordinal)) continue;
            n++;
            Console.WriteLine($"=== {typeName}::{methodName} rva=0x{def.RelativeVirtualAddress:X} ilsize={il.Length} file=0x{fileOff:X} attrs={def.Attributes}");
            Console.WriteLine(HexDump(il, fileOff));
        }
        Console.WriteLine($"-- {n} methods");
        return 0;
    }

    private static int DumpType(PEReader pe, MetadataReader md, string typeSub)
    {
        int n = 0;
        foreach (var (handle, def, typeName, methodName, il, fileOff) in Enumerate(pe, md))
        {
            if (!typeName.Contains(typeSub, StringComparison.Ordinal)) continue;
            n++;
            Console.WriteLine($"{typeName}::{methodName}  il={il.Length} file=0x{fileOff:X}");
        }
        Console.WriteLine($"-- {n} methods");
        return 0;
    }

    private static string HexDump(byte[] data, int baseOffset)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < data.Length; i += 16)
        {
            sb.Append($"  {baseOffset + i:X6}  ");
            for (int j = 0; j < 16; j++)
            {
                if (i + j < data.Length) sb.Append(data[i + j].ToString("X2")).Append(' ');
                else sb.Append("   ");
                if (j == 7) sb.Append(' ');
            }
            sb.Append(" |");
            for (int j = 0; j < 16 && i + j < data.Length; j++)
            {
                byte b = data[i + j];
                sb.Append(b >= 32 && b < 127 ? (char)b : '.');
            }
            sb.AppendLine("|");
        }
        return sb.ToString();
    }

    private static int Patch(FileStream fs, PEReader pe, MetadataReader md, string typeSub, string methodSub, string mode)
    {
        bool byOffset = typeSub.StartsWith("@", StringComparison.Ordinal);
        int wantedOffset = -1;
        if (byOffset && !int.TryParse(typeSub.Substring(1), System.Globalization.NumberStyles.HexNumber, null, out wantedOffset))
        {
            Console.WriteLine("bad offset");
            return 7;
        }
        var hits = new List<(string type, string method, byte[] il, int fileOff)>();
        foreach (var (handle, def, typeName, methodName, il, fileOff) in Enumerate(pe, md))
        {
            if (byOffset ? fileOff == wantedOffset
                         : typeName.Contains(typeSub, StringComparison.Ordinal) && methodName.Contains(methodSub, StringComparison.Ordinal))
                hits.Add((typeName, methodName, il, fileOff));
        }
        if (hits.Count == 0) { Console.WriteLine("no matching method"); return 3; }
        if (hits.Count > 1) { Console.WriteLine("ambiguous: " + string.Join(", ", hits.Select(h => h.type + "::" + h.method))); return 4; }
        var hit = hits[0];
        byte[] replacement = mode switch
        {
            "ret" => new byte[] { 0x2A },
            "ldnull_ret" => new byte[] { 0x14, 0x2A },
            _ => throw new ArgumentException("bad mode")
        };
        if (replacement.Length > hit.il.Length)
        {
            Console.WriteLine("method too small");
            return 5;
        }
        byte[] newIl = new byte[hit.il.Length];
        Array.Copy(replacement, newIl, replacement.Length);

        fs.Position = hit.fileOff;
        var check = new byte[hit.il.Length];
        fs.ReadExactly(check);
        if (!check.AsSpan().SequenceEqual(hit.il)) { Console.WriteLine("sanity check failed: file bytes != assembly IL"); return 6; }

        fs.Position = hit.fileOff;
        fs.Write(newIl);
        fs.Flush();
        Console.WriteLine($"patched {hit.type}::{hit.method} at 0x{hit.fileOff:X} ({hit.il.Length} bytes -> {mode})");
        return 0;
    }
}
