<#
.SYNOPSIS
  以攻击者身份（普通用户、未提权）验证 Windows 端 IPC 加固。对标 Linux 的 scripts/test-isolation.sh。
  每一项都必须"失败"（被拒绝）才算通过。管理员身份下拒绝运行——那样测出来的绿色是假的。

.PARAMETER Squat
  抢占 CP 管道名 \\.\pipe\ImmurokCredentialProvider 并等待连接。运行后请锁屏、触摸指纹：
  期望脚本一个字节都收不到，Service 日志出现「CP 管道对端校验失败」。Ctrl+C 结束。

.PARAMETER OwnerFile
  检查 %ProgramData%\immurok\owner 是否存在、以普通用户身份是否不可写。

.PARAMETER DataDir
  以普通用户身份读 %ProgramData%\immurok\pairing.dat / settings.json、在目录里建文件：期望全部被拒；
  目录 DACL 里不应再有 BUILTIN\Users。

.PARAMETER Commands
  直接连 \\.\pipe\immurok / \\.\pipe\immurok-cli 发特权命令，打印应答。powershell.exe 不在安装目录里，
  所以即使是 owner 本人，只读命令应放行、其余应得 ERROR:CALLER_NOT_TRUSTED（§3.3 第一层）；
  在第二个本地账号下运行时同样如此。
#>
param(
    [switch]$Squat,
    [switch]$OwnerFile,
    [switch]$DataDir,
    [switch]$Commands,
    [int]$Rounds = 3
)

$id = [Security.Principal.WindowsIdentity]::GetCurrent()
if ((New-Object Security.Principal.WindowsPrincipal $id).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "refusing to run elevated: the checks are only meaningful as an ordinary user" -ForegroundColor Red
    exit 2
}
if (-not ($Squat -or $OwnerFile -or $DataDir -or $Commands)) { $OwnerFile = $true; $DataDir = $true; $Commands = $true }

function Send-Frame([string]$cmd) {
    $pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'immurok', [System.IO.Pipes.PipeDirection]::InOut)
    $pipe.Connect(3000)
    $payload = [Text.Encoding]::UTF8.GetBytes($cmd)
    $len = [BitConverter]::GetBytes([int]$payload.Length)
    $pipe.Write($len, 0, 4); $pipe.Write($payload, 0, $payload.Length); $pipe.Flush()
    $hdr = New-Object byte[] 4; $n = 0
    while ($n -lt 4) { $r = $pipe.Read($hdr, $n, 4 - $n); if ($r -le 0) { break }; $n += $r }
    $rlen = [BitConverter]::ToInt32($hdr, 0)
    $buf = New-Object byte[] $rlen; $n = 0
    while ($n -lt $rlen) { $r = $pipe.Read($buf, $n, $rlen - $n); if ($r -le 0) { break }; $n += $r }
    $pipe.Dispose()
    return [Text.Encoding]::UTF8.GetString($buf, 0, $n)
}

if ($OwnerFile) {
    $f = Join-Path $env:ProgramData 'immurok\owner'
    Write-Host "== owner file: $f"
    $exists = $null
    try { $exists = Test-Path $f -ErrorAction Stop } catch { $exists = $null }
    if ($null -eq $exists) { Write-Host "  OK: data dir not even listable by ordinary user (directory ACL tightened, §3.4)" -ForegroundColor Green }
    elseif (-not $exists) { Write-Host "  MISSING (expected after pairing / PASS:SET / migration)" -ForegroundColor Yellow }
    else {
        icacls $f | Select-Object -First 3 | ForEach-Object { "  $_" }
        try { Add-Content -Path $f -Value "x" -ErrorAction Stop; Write-Host "  FAIL: writable by ordinary user" -ForegroundColor Red }
        catch { Write-Host "  OK: write denied ($($_.Exception.GetType().Name))" -ForegroundColor Green }
        try { $null = Get-Content $f -ErrorAction Stop; Write-Host "  note: readable by ordinary user (directory ACL not tightened yet, §3.4)" }
        catch { Write-Host "  OK: read denied" -ForegroundColor Green }
    }
}

if ($DataDir) {
    $d = Join-Path $env:ProgramData 'immurok'
    Write-Host "== data dir: $d"
    $acl = icacls $d 2>&1 | Out-String
    if ($acl -match 'BUILTIN\\Users|Everyone|Authenticated Users') { Write-Host "  FAIL: Users/Everyone still in DACL" -ForegroundColor Red }
    elseif ($acl -match 'SYSTEM') { Write-Host "  OK: DACL has no Users/Everyone entry" -ForegroundColor Green }
    else { Write-Host "  OK: DACL not readable by ordinary user" -ForegroundColor Green }
    foreach ($f in @('pairing.dat', 'settings.json')) {
        $p = Join-Path $d $f
        try { $null = [IO.File]::ReadAllBytes($p); Write-Host "  FAIL: $f readable" -ForegroundColor Red }
        catch [System.IO.FileNotFoundException] { Write-Host "  note: $f not present" }
        catch { Write-Host "  OK: $f read denied ($($_.Exception.GetType().Name))" -ForegroundColor Green }
    }
    try { $t = Join-Path $d 'attacker.txt'; [IO.File]::WriteAllText($t, 'x'); Remove-Item $t; Write-Host "  FAIL: can create files in data dir" -ForegroundColor Red }
    catch { Write-Host "  OK: create file denied" -ForegroundColor Green }
}

if ($Commands) {
    Write-Host "== privileged commands over \\.\pipe\immurok (as $($id.Name))"
    $expect = [ordered]@{
        'PASS:STATUS'        = 'OK'                        # 只读：任何人
        'KEY:LIST:1'         = 'OK|ERROR:NOT_CONNECTED'    # 只读
        'SECURITY:STATUS'    = 'OK:cp_pipe='               # 只读：健康状态
        'AUTH:x:test'        = 'ERROR:CALLER_NOT_TRUSTED'  # 仅安装目录里的客户端
        'FEATURE:SET:otp:1'  = 'ERROR:CALLER_NOT_TRUSTED'
        'FP:ENROLL:0'        = 'ERROR:CALLER_NOT_TRUSTED'  # 最严重的一条：偷录指纹
        'PASS:SET:eA==:eA==' = 'ERROR:CALLER_NOT_TRUSTED'
        'PAIR:RESET'         = 'ERROR:CALLER_NOT_TRUSTED'
    }
    foreach ($c in $expect.Keys) {
        try { $r = Send-Frame $c } catch { $r = "EXC: $($_.Exception.Message)" }
        $ok = $r -match ('^(' + $expect[$c] + ')')
        Write-Host ("  {0,-20} -> {1}  {2}" -f $c, $r, $(if ($ok) { 'OK' } else { 'FAIL (expected ' + $expect[$c] + ')' })) -ForegroundColor $(if ($ok) { 'Green' } else { 'Red' })
    }
    # imk 管道：APPROVE 只认安装目录里的 imk.exe
    try {
        $cli = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'immurok-cli', [System.IO.Pipes.PipeDirection]::InOut)
        $cli.Connect(3000)
        $w = New-Object IO.StreamWriter($cli); $w.AutoFlush = $true; $w.WriteLine('APPROVE:whoami')
        $rd = New-Object IO.StreamReader($cli); $r = $rd.ReadToEnd(); $cli.Dispose()
    } catch { $r = "EXC: $($_.Exception.Message)" }
    $ok = $r -match '^ERROR:CALLER_NOT_TRUSTED'
    Write-Host ("  {0,-20} -> {1}  {2}" -f 'cli APPROVE', $r.Trim(), $(if ($ok) { 'OK' } else { 'FAIL' })) -ForegroundColor $(if ($ok) { 'Green' } else { 'Red' })
}

if ($Squat) {
    Write-Host "== squatting \\.\pipe\ImmurokCredentialProvider as $($id.Name); lock the screen and touch the sensor (Ctrl+C to stop)"
    $sec = New-Object System.IO.Pipes.PipeSecurity
    $sec.AddAccessRule((New-Object System.IO.Pipes.PipeAccessRule('Everyone', 'FullControl', 'Allow')))
    for ($round = 1; $round -le $Rounds; $round++) {
        $srv = New-Object System.IO.Pipes.NamedPipeServerStream('ImmurokCredentialProvider',
            [System.IO.Pipes.PipeDirection]::In, 1, [System.IO.Pipes.PipeTransmissionMode]::Byte,
            [System.IO.Pipes.PipeOptions]::None, 4096, 4096, $sec)
        Write-Host "  [$round] pipe created, waiting for the service to connect..."
        $srv.WaitForConnection()
        $buf = New-Object byte[] 4096; $total = 0
        try { while (($n = $srv.Read($buf, 0, $buf.Length)) -gt 0) { $total += $n } } catch { }
        $stamp = Get-Date -Format 'HH:mm:ss'
        if ($total -eq 0) { Write-Host "  [$round] $stamp connection, 0 bytes -> OK (service refused to write)" -ForegroundColor Green }
        else { Write-Host "  [$round] $stamp received $total bytes -> FAIL (credential leaked)" -ForegroundColor Red }
        $srv.Dispose()
    }
}
