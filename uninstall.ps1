#requires -Version 5.1 -RunAsAdministrator
<#
.SYNOPSIS
  卸载 immurok：反注册 Credential Provider、删除服务、清理文件与数据。
.NOTES 必须以管理员运行。
#>
param(
    [string]$InstallDir = "$env:ProgramFiles\immurok"
)

$ErrorActionPreference = 'Continue'
$root = $PSScriptRoot

Write-Host "==> 反注册 Credential Provider ..." -ForegroundColor Cyan
reg.exe import "$root\ImmurokCredentialProvider\Unregister.reg"

Write-Host "==> 停止并删除服务 ..." -ForegroundColor Cyan
sc.exe stop ImmurokService | Out-Null
sc.exe delete ImmurokService | Out-Null

Write-Host "==> 清理凭据管理器条目 ..." -ForegroundColor Cyan
cmdkey /delete:immurok/login | Out-Null

Write-Host "==> 删除文件与数据 ..." -ForegroundColor Cyan
Remove-Item $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item "$env:ProgramData\immurok" -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "==> 卸载完成。" -ForegroundColor Green
