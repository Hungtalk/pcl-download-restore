<#
.SYNOPSIS
    恢复 PCL 2.99.0.1 的「下载 → 原版游戏」入口（只改本机副本）。

.DESCRIPTION
    2.99.0.1 需要两处修改：
      1) PCL.PageDownloadInstall.LoaderInit()：假加载器 → ModDownload.DlClientListLoader
      2) PCL.PageDownloadLeft..ctor()：去掉“隐藏原版游戏入口”的代码

.PARAMETER Path
    PCL 可执行文件路径。默认取当前目录下的 Plain Craft Launcher 2.exe。

.EXAMPLE
    .\patch-pcl-29901.ps1 -Path "D:\PCL\Plain Craft Launcher 2.exe"
#>
[CmdletBinding()]
param(
    [string]$Path = (Join-Path (Get-Location) 'Plain Craft Launcher 2.exe')
)

$ErrorActionPreference = 'Stop'

$SourceHash = '0DB34E19D9EEB6D496D20FABE786EC16BBD36BA6B0CCDC8D29CB7E909C114397'
$TargetHash = '2F255F386C8C22F4C40B83E3B2140BCD5E2CA8B1A431B934FF1A556BA3F641FD'

# 补丁 1：LoaderInit
$Offset1 = 0x2C16E
$Expected1 = [byte[]](
    0x72, 0x75, 0x48, 0x01, 0x70, 0x7E, 0xFC, 0x09, 0x00, 0x04, 0x2C, 0x07, 0x7E, 0xFC, 0x09, 0x00,
    0x04, 0x2B, 0x16, 0x7E, 0xFA, 0x09, 0x00, 0x04, 0xFE, 0x06, 0x69, 0x14, 0x00, 0x06, 0x73, 0x8D,
    0x05, 0x00, 0x0A, 0x25, 0x80, 0xFC, 0x09, 0x00, 0x04, 0x14, 0x18, 0x73, 0x8E, 0x05, 0x00, 0x0A
)
$Replacement1 = New-Object byte[] $Expected1.Length
$Replacement1[0] = 0x7E
[BitConverter]::GetBytes([int]0x0400040F).CopyTo($Replacement1, 1)

# 补丁 2：PageDownloadLeft..ctor（截断隐藏入口的代码，ret + NOP）
$Offset2 = 0x62DFF
$Expected2 = [byte[]](
    0x02, 0x6F, 0x7D, 0x0C, 0x00, 0x06, 0x18, 0x6F, 0x0E, 0x01, 0x00, 0x0A, 0x02, 0x6F, 0x7F, 0x0C,
    0x00, 0x06, 0x18, 0x6F, 0x0E, 0x01, 0x00, 0x0A, 0x02, 0x18, 0x7D, 0xC6, 0x04, 0x00, 0x04, 0x02,
    0x6F, 0x81, 0x0C, 0x00, 0x06, 0x17, 0x6F, 0x20, 0x0F, 0x00, 0x06
)
$Replacement2 = New-Object byte[] $Expected2.Length
$Replacement2[0] = 0x2A

function Get-Sha256([string]$File) {
    (Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash.ToUpperInvariant()
}

if (-not (Test-Path -LiteralPath $Path)) {
    throw "找不到文件：$Path"
}
$Path = (Resolve-Path -LiteralPath $Path).Path
$hash = Get-Sha256 $Path

if ($hash -eq $TargetHash) {
    Write-Host "这个文件已经打过补丁，无需重复修改。" -ForegroundColor Yellow
    exit 0
}
if ($hash -ne $SourceHash) {
    throw ("文件哈希不匹配，脚本不会修改它。`n  期望：{0}`n  实际：{1}`n请确认 PCL 版本为 2.99.0.1（VersionCode 415）。" -f $SourceHash, $hash)
}

$bytes = [System.IO.File]::ReadAllBytes($Path)
foreach ($hunk in @(
        @{ Offset = $Offset1; Expected = $Expected1 },
        @{ Offset = $Offset2; Expected = $Expected2 }
    )) {
    $off = [int]$hunk.Offset
    $exp = [byte[]]$hunk.Expected
    if ($bytes.Length -lt ($off + $exp.Length)) { throw "文件大小异常，可能不是完整的 PCL 程序。" }
    for ($i = 0; $i -lt $exp.Length; $i++) {
        if ($bytes[$off + $i] -ne $exp[$i]) {
            throw ("目标位置字节不匹配（偏移 0x{0:X}），脚本已中止。" -f ($off + $i))
        }
    }
}

$backup = "$Path.bak"
if (-not (Test-Path -LiteralPath $backup)) {
    Copy-Item -LiteralPath $Path -Destination $backup
    Write-Host "已备份原文件：$backup" -ForegroundColor Cyan
}
else {
    Write-Host "备份已存在，跳过备份：$backup" -ForegroundColor Cyan
}

for ($i = 0; $i -lt $Replacement1.Length; $i++) { $bytes[$Offset1 + $i] = $Replacement1[$i] }
for ($i = 0; $i -lt $Replacement2.Length; $i++) { $bytes[$Offset2 + $i] = $Replacement2[$i] }
[System.IO.File]::WriteAllBytes($Path, $bytes)

$newHash = Get-Sha256 $Path
if ($newHash -ne $TargetHash) {
    throw ("补丁写入后校验失败。`n  期望：{0}`n  实际：{1}`n请用 .bak 还原。" -f $TargetHash, $newHash)
}

Write-Host "patched 48 bytes at 0x2C16E (LoaderInit)" -ForegroundColor Green
Write-Host "patched 43 bytes at 0x62DFF (PageDownloadLeft ctor)" -ForegroundColor Green
Write-Host "OK：PCL 2.99.0.1 已恢复「下载 → 原版游戏」入口。" -ForegroundColor Green
Write-Host "如需还原：把 $backup 改名覆盖回去即可。"
