<#
.SYNOPSIS
    恢复 PCL 2.99.0.0 的「下载 → 原版游戏」入口（只改本机副本）。

.DESCRIPTION
    把 PCL.PageDownloadInstall.LoaderInit() 里的“关闭下载”假加载器
    替换回真正的版本清单加载器 PCL.ModDownload.DlClientListLoader。

.PARAMETER Path
    PCL 可执行文件路径。默认取当前目录下的 Plain Craft Launcher 2.exe。

.EXAMPLE
    .\patch-pcl-29900.ps1 -Path "D:\PCL\Plain Craft Launcher 2.exe"
#>
[CmdletBinding()]
param(
    [string]$Path = (Join-Path (Get-Location) 'Plain Craft Launcher 2.exe')
)

$ErrorActionPreference = 'Stop'

$SourceHash  = '6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386'
$TargetHash  = 'BA7585206D97D63AD6F48CDF5FE3F79757B405990D85D1009E1EE19EDF4272A9'
$Offset      = 0x2C0DA
$Expected    = [byte[]](
    0x72, 0x33, 0x47, 0x01, 0x70, 0x7E, 0xFB, 0x09, 0x00, 0x04, 0x2C, 0x07, 0x7E, 0xFB, 0x09, 0x00,
    0x04, 0x2B, 0x16, 0x7E, 0xF9, 0x09, 0x00, 0x04, 0xFE, 0x06, 0x67, 0x14, 0x00, 0x06, 0x73, 0x8D,
    0x05, 0x00, 0x0A, 0x25, 0x80, 0xFB, 0x09, 0x00, 0x04, 0x14, 0x18, 0x73, 0x8E, 0x05, 0x00, 0x0A
)

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
    throw ("文件哈希不匹配，脚本不会修改它。`n  期望：{0}`n  实际：{1}`n请确认 PCL 版本为 2.99.0.0（VersionCode 413）。" -f $SourceHash, $hash)
}

# 构造替换字节：ldsfld PCL.ModDownload::DlClientListLoader + NOP 填充
$Replacement = New-Object byte[] $Expected.Length
$Replacement[0] = 0x7E                                     # ldsfld
[BitConverter]::GetBytes([int]0x0400040F).CopyTo($Replacement, 1)

$bytes = [System.IO.File]::ReadAllBytes($Path)
if ($bytes.Length -lt ($Offset + $Expected.Length)) {
    throw "文件大小异常，可能不是完整的 PCL 程序。"
}
for ($i = 0; $i -lt $Expected.Length; $i++) {
    if ($bytes[$Offset + $i] -ne $Expected[$i]) {
        throw ("目标位置字节不匹配（偏移 0x{0:X}），脚本已中止。" -f ($Offset + $i))
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

for ($i = 0; $i -lt $Replacement.Length; $i++) {
    $bytes[$Offset + $i] = $Replacement[$i]
}
[System.IO.File]::WriteAllBytes($Path, $bytes)

$newHash = Get-Sha256 $Path
if ($newHash -ne $TargetHash) {
    throw ("补丁写入后校验失败。`n  期望：{0}`n  实际：{1}`n请用 .bak 还原。" -f $TargetHash, $newHash)
}

Write-Host "patched 48 bytes at 0x2C0DA" -ForegroundColor Green
Write-Host "OK：PCL 2.99.0.0 已恢复「下载 → 原版游戏」入口。" -ForegroundColor Green
Write-Host "如需还原：把 $backup 改名覆盖回去即可。"
