# PCL 下载功能修复脚本（2.99.0.0 / 2.99.0.1）

> 本仓库**只包含修复脚本**，不包含 PCL 程序本体、不包含修改后的 exe，也不分发 PCL 源码。

## 这是什么

PCL 2.99.0.0 / 2.99.0.1 把「下载 → 原版游戏」的版本列表加载器换成了一个必定抛异常的空任务：

```vb
Public Const RestrictMcDownload As Boolean = True
' ...
If RestrictMcDownload Then
    PageLoaderInit(..., New LoaderTask(Of String, DlClientListResult)("关闭下载",
        Sub() Throw New Exception("……不再提供 MC 下载功能……")), ...)
Else
    PageLoaderInit(..., DlClientListLoader, AddressOf LoadMinecraft_OnFinish)   ' 正常逻辑
End If
```

常量在编译期被折叠，`Else` 分支被当作死代码整段删除，所以官方 exe 里只剩那句报错。

本脚本把这两处 IL 改回正常逻辑，让你自己机器上的副本恢复「下载 → 原版游戏」页面。
2.99.0.1 还额外把侧栏的「原版游戏」入口隐藏了，脚本会一并恢复。

## 支持版本

| PCL 版本 | 原始 exe SHA256 | 修复后 SHA256 |
| --- | --- | --- |
| 2.99.0.0（VersionCode 413） | `6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386` | `BA7585206D97D63AD6F48CDF5FE3F79757B405990D85D1009E1EE19EDF4272A9` |
| 2.99.0.1（VersionCode 415） | `0DB34E19D9EEB6D496D20FABE786EC16BBD36BA6B0CCDC8D29CB7E909C114397` | `2F255F386C8C22F4C40B83E3B2140BCD5E2CA8B1A431B934FF1A556BA3F641FD` |

脚本会**先校验原文件 SHA256**，对不上就直接退出，不会误改别的版本。

## 使用方法

1. 完全退出 PCL（任务管理器里不要有 `Plain Craft Launcher 2.exe`）。
2. 打开 PowerShell，进入本仓库目录，按你的版本执行：

```powershell
# PCL 2.99.0.0
powershell -ExecutionPolicy Bypass -File .\scripts\patch-pcl-29900.ps1 -Path "D:\你的目录\Plain Craft Launcher 2.exe"

# PCL 2.99.0.1
powershell -ExecutionPolicy Bypass -File .\scripts\patch-pcl-29901.ps1 -Path "D:\你的目录\Plain Craft Launcher 2.exe"
```

3. 看到 `patched ... bytes` 和 `OK` 即成功；脚本会同时生成 `Plain Craft Launcher 2.exe.bak` 备份。
4. 启动 PCL → 顶部「下载」，侧栏应重新出现「原版游戏」，能看到版本列表。

### 还原

把同目录下的 `Plain Craft Launcher 2.exe.bak` 改名覆盖回去即可。

## 注意事项

- **只对你自己的副本使用**，修改后的 exe **不要二次分发**。PCL 使用《PCL 分发有限许可》，对二次分发有额外要求，本项目不提供也不鼓励分发成品。
- 本脚本只恢复「原版游戏」下载入口；**「整合包安装」等同样被关掉的功能不在范围内**（那部分在官方 exe 里已被编译器整段删除，改 IL 救不回来）。
- PCL 升级/覆盖安装后补丁失效；新版本的方法偏移和哈希都会变，需要重新适配，脚本会直接报哈希不匹配而不是乱改。
- 修改 exe 属于非官方行为，有风险；请保留备份。若 PCL 后续自校验或更新，可能出现异常。
- 本项目与 PCL 官方（龙腾猫跃 / Meloong-Git）**没有任何关系**，不代表官方立场。
- 各地对游戏下载/分发的法律法规不同，请自行确认使用场景合规。

## 原理

见 [docs/how-it-works.md](docs/how-it-works.md)。

## 工具

`tools/pcltool` 是一个读取 .NET 程序集 IL 的小工具（scan / strings / dump / type / patch），用来定位方法体、核对字节、验证补丁结果。可按需自行编译（.NET 10 SDK）。

## 许可

本仓库的脚本与工具代码使用 MIT 许可。
PCL 本体不在本仓库内，其版权与许可归原作者所有。
