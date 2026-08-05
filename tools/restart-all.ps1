#requires -Version 5.1
<#
.SYNOPSIS
  一键重建并重启 immurok。可整体，也可单独只处理 server 或 client。
  流程：关闭目标进程 → 重新编译目标 → 重启 server →（若同时含 client）等待 N 秒 → 启动 client。

.DESCRIPTION
  两种运行模式：
    · 默认（开发进程模式）：直接以进程方式跑 ImmurokService.exe / ImmurokClient.exe，免管理员、迭代快。
      此模式下 Service 在用户会话（Session 1），锁屏解锁的 Credential Provider 走不通，
      但 BLE / 配对 / 指纹 / SSH agent 等功能都可测。
    · -Service（已安装服务模式）：sc 停服务 → 部署编译产物到安装目录 → sc 启服务，需管理员。

  执行顺序固定为「先停后编译」：正在运行的 exe 会锁定文件，不先关掉会导致 build 失败。

.PARAMETER Target
  All（默认）：server + client 都重编重启。
  Server：只重编并重启 server（不动 client）。
  Client：只重编并重启 client（不动 server）。
  说明：C++ Credential Provider 仅在 Target=All 时随 build.ps1 一起编。只改了 CP 源码时请用 All。

.PARAMETER Configuration
  Debug（默认，迭代快）或 Release。

.PARAMETER Service
  以「已安装 Windows 服务」方式停/启 server（需管理员）。省略则用开发进程模式。

.PARAMETER InstallDir
  服务模式下的安装目录，默认 %ProgramFiles%\immurok。

.PARAMETER ClientDelaySeconds
  server 起来后、启动 client 前的等待秒数（默认 5）。仅在同时启动 server 与 client 时生效。

.EXAMPLE
  .\tools\restart-all.ps1                # 全部（等价 -t all，并把 imk 加入用户 PATH）
  .\tools\restart-all.ps1 -t server      # 只重编重启 server
  .\tools\restart-all.ps1 -t client      # 只重编重启 client
  .\tools\restart-all.ps1 -t imk         # 只编译 imk，并加入用户 PATH（不涉及停/启进程）
  .\tools\restart-all.ps1 -t server -Service   # 以管理员运行，重启已安装服务
#>
param(
    [Alias('t')]
    [ValidateSet('All','Server','Client','Imk')]
    [string]$Target = 'All',
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Debug',
    [switch]$Service,
    [string]$InstallDir = "$env:ProgramFiles\immurok",
    [int]$ClientDelaySeconds = 5
)

$ErrorActionPreference = 'Stop'
$toolsDir  = $PSScriptRoot
$root      = Split-Path $toolsDir -Parent
$tfm       = 'net8.0-windows10.0.19041.0'
$svcBin    = Join-Path $root "ImmurokService\bin\x64\$Configuration\$tfm"
$cliBin    = Join-Path $root "ImmurokClient\bin\x64\$Configuration\$tfm"
$imkBin    = Join-Path $root "ImmurokCli\bin\x64\$Configuration\$tfm"
$svcExeBin = Join-Path $svcBin 'ImmurokService.exe'
$cliExeBin = Join-Path $cliBin 'ImmurokClient.exe'

$doServer = $Target -in @('All','Server')
$doClient = $Target -in @('All','Client')

function Info($m) { Write-Host $m -ForegroundColor Cyan }
function Ok($m)   { Write-Host $m -ForegroundColor Green }
function Warn($m) { Write-Host $m -ForegroundColor Yellow }

function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole(
        [Security.Principal.WindowsBuiltinRole]::Administrator)
}

function Stop-Proc($name) {
    $ps = Get-Process -Name $name -ErrorAction SilentlyContinue
    if ($ps) {
        Info "    停止进程 $name (PID: $($ps.Id -join ', '))"
        $ps | Stop-Process -Force -ErrorAction SilentlyContinue
    }
}

# 把开发态的 imk.exe 目录加入用户 PATH（免管理员，新开终端即可 imk），并让当前会话立即可用。
function Ensure-ImkPath {
    if (-not (Test-Path (Join-Path $imkBin 'imk.exe'))) {
        Warn "    未找到 $imkBin\imk.exe，跳过 PATH 设置（确认已编译）。"
        return
    }
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    if ([string]::IsNullOrEmpty($userPath) -or ($userPath -notlike "*$imkBin*")) {
        $newPath = if ([string]::IsNullOrEmpty($userPath)) { $imkBin } else { "$userPath;$imkBin" }
        [Environment]::SetEnvironmentVariable('Path', $newPath, 'User')
        Ok  "    imk 已加入用户 PATH：$imkBin（新开终端生效）。"
    } else {
        Info "    imk 目录已在用户 PATH。"
    }
    # 当前 PowerShell 会话也立即可用。
    if ($env:Path -notlike "*$imkBin*") { $env:Path = "$env:Path;$imkBin" }
}

function Deploy-InstallDir([bool]$server, [bool]$client) {
    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
    if ($server) {
        Info "    部署 server → $InstallDir"
        Copy-Item "$svcBin\*" $InstallDir -Recurse -Force
        $cpDll = Join-Path $root "ImmurokCredentialProvider\x64\$Configuration\ImmurokCredentialProvider.dll"
        if (Test-Path $cpDll) { Copy-Item $cpDll $InstallDir -Force }
    }
    if ($client) {
        Info "    部署 client → $InstallDir"
        Copy-Item "$cliBin\*" $InstallDir -Recurse -Force
    }
}

function Start-Server {
    if ($Service) {
        Deploy-InstallDir -server $true -client $false
        Info "    sc start ImmurokService"
        sc.exe start ImmurokService | Out-Null
        Ok  "    服务已启动（日志见 C:\ProgramData\immurok\logs\）。"
    }
    else {
        if (-not (Test-Path $svcExeBin)) { throw "未找到 $svcExeBin，请确认编译成功。" }
        $svcOut = Join-Path $toolsDir 'service.console.out.log'
        $svcErr = Join-Path $toolsDir 'service.console.err.log'
        Remove-Item $svcOut, $svcErr -Force -ErrorAction SilentlyContinue
        $p = Start-Process -FilePath $svcExeBin -WorkingDirectory $svcBin `
            -RedirectStandardOutput $svcOut -RedirectStandardError $svcErr `
            -PassThru -WindowStyle Hidden
        Ok  "    server 已启动 (PID=$($p.Id))，日志见 C:\ProgramData\immurok\logs\ 及 $svcOut"
    }
}

function Start-Client {
    if ($Service) {
        Deploy-InstallDir -server $false -client $true
        $cli = Join-Path $InstallDir 'ImmurokClient.exe'
    }
    else {
        $cli = $cliExeBin
    }
    if (-not (Test-Path $cli)) { throw "未找到 client：$cli" }
    Start-Process -FilePath $cli -WorkingDirectory (Split-Path $cli -Parent) | Out-Null
    Ok  "    client 已启动。"
}

# imk 是命令行工具，不涉及停/启进程：只重编 + 加入用户 PATH。
if ($Target -eq 'Imk') {
    Info "==> 编译 imk ($Configuration) ..."
    dotnet build "$root\ImmurokCli\ImmurokCli.csproj" -c $Configuration -p:Platform=x64
    if ($LASTEXITCODE -ne 0) { throw "编译 imk 失败（退出码 $LASTEXITCODE）。" }
    Ensure-ImkPath
    Ok "==> 完成：imk 已编译并加入用户 PATH（新开终端可直接 imk）。"
    return
}

if ($Service -and -not (Test-Admin)) {
    throw "服务模式需要管理员权限。请以管理员身份重新运行 PowerShell 后再执行本脚本。"
}

Info "==> 目标: $Target  配置: $Configuration  模式: $(if($Service){'已安装服务'}else{'开发进程'})"

# ============ 1) 先关闭目标（释放文件占用，才能重编译） ============
Info "==> [1/3] 关闭目标进程 ..."
if ($doServer) {
    if ($Service) {
        Info "    sc stop ImmurokService"
        sc.exe stop ImmurokService | Out-Null
        for ($i = 0; $i -lt 20; $i++) {
            $st = (sc.exe query ImmurokService) -join "`n"
            if ($st -match 'STOPPED' -or $st -notmatch 'ImmurokService') { break }
            Start-Sleep -Milliseconds 300
        }
    }
    Stop-Proc 'ImmurokService'
}
if ($doClient) { Stop-Proc 'ImmurokClient' }
Start-Sleep -Milliseconds 500

# ============ 2) 重新编译目标 ============
Info "==> [2/3] 编译 ($Configuration) ..."
if ($Target -eq 'All') {
    # 整体编译（含 C++ Credential Provider）
    & (Join-Path $root 'build.ps1') -Configuration $Configuration
}
else {
    if ($doServer) {
        dotnet build "$root\ImmurokService\ImmurokService.csproj" -c $Configuration -p:Platform=x64
        if ($LASTEXITCODE -ne 0) { throw "dotnet 编译 ImmurokService 失败（退出码 $LASTEXITCODE）。" }
    }
    if ($doClient) {
        dotnet build "$root\ImmurokClient\ImmurokClient.csproj" -c $Configuration -p:Platform=x64
        if ($LASTEXITCODE -ne 0) { throw "dotnet 编译 ImmurokClient 失败（退出码 $LASTEXITCODE）。" }
    }
}
Ok "    编译完成。"

# ============ 3) 启动目标 ============
Info "==> [3/3] 启动目标 ..."
if ($doServer) { Start-Server }
if ($doClient) {
    if ($doServer) {
        Info "    等待 $ClientDelaySeconds 秒后启动 client ..."
        Start-Sleep -Seconds $ClientDelaySeconds
    }
    Start-Client
}
# All 模式也编译了 imk（build.ps1），顺带确保它在用户 PATH。
if ($Target -eq 'All') { Ensure-ImkPath }
Ok "==> 完成。"
