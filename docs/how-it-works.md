# 原理：为什么改 48 字节就能恢复

## 1. 不是删代码，是编译期折叠

PCL 的 `Modules/ModSecret.vb` 里有一个常量：

```vb
Public Const RestrictMcDownload As Boolean = True
```

所有下载入口都写成这样：

```vb
If RestrictMcDownload Then
    ' 抛异常 / 提示
Else
    ' 正常逻辑
End If
```

VB 编译器对 `Const` 做常量折叠：条件恒为 `True` 时，`Else` 分支被判定为不可达代码并整段删除。
所以官方 exe 里根本看不到正常逻辑，看起来像"功能被删了"，其实源码层面是完整的（仓库 main 分支可查）。

## 2. 需要改的地方

### 2.99.0.0

`PCL.PageDownloadInstall.LoaderInit()` 里创建了一个名字叫「关闭下载」的假加载器，回调只有一句 `throw`。
把构造这个加载器的 IL（48 字节）换成加载 `PCL.ModDownload::DlClientListLoader`（真正的版本清单加载器）：

```
原文：ldstr "关闭下载"; ... newobj LoaderTask(...)
改后：ldsfld PCL.ModDownload::DlClientListLoader; nop × 43
```

后面的 `PageLoaderInit(..., DlClientListLoader, AddressOf LoadMinecraft_OnFinish, ...)` 参数位置完全对得上，
`LoadMinecraft_OnFinish` 本来读的就是全局的 `DlClientListLoader.Output`，所以不需要改别的地方。

### 2.99.0.1

2.99.0.1 除了同样的 `LoaderInit`，还在 `PCL.PageDownloadLeft..ctor` 里加了：

```vb
If Not RestrictMcDownload Then Return
ItemInstall.Visibility = Visibility.Collapsed
LabResource.Visibility = Visibility.Collapsed
PageID = FormMain.PageSubType.DownloadMod
ItemMod.Checked = True
```

常量折叠后 `If Not True Then Return` 直接消失，剩下的赋值无条件执行，于是侧栏「原版游戏」被隐藏、默认页切到 Mod。
脚本把构造函数截断成 `ret` + NOP，等价于恢复到 2.99.0.0 的构造函数（`InitializeComponent()` 之后直接返回）。

## 3. 为什么可以原地覆盖字节

- .NET 方法体的 IL 长度记录在方法头里，**不随内容变化**；用 NOP（0x00）填充剩余长度不会破坏任何偏移。
- 被替换的那段代码里没有任何跳转目标从外部进入（分支都在段内），所以整段替换是安全的。
- 不改变文件长度、不增加元数据，PE 结构完全不动。

## 4. 校验方式

脚本在写之前会核对目标偏移处的原始字节，写完后再核对整个文件的 SHA256，
对不上就退出（例如版本不同、已经被别的工具改过）。

也可以手动用仓库里的工具检查：

```powershell
dotnet run --project tools/pcltool -- dump "Plain Craft Launcher 2.exe" PageDownloadInstall LoaderInit
```

正常情况下应该能看到 `ldsfld ... DlClientListLoader` 而没有 `ldstr "关闭下载"`。
