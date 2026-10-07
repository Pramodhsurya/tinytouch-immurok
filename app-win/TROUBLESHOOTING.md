# immurok Windows 版 —— 排障与使用要点

实机踩坑记录，持续补充。

## SSH agent（用设备密钥让 ssh / git 签名）

### ✅ git 走不通、ssh 却能用 —— git 用了自带的 MSYS ssh

**现象**：`ssh -v` 能用设备钥匙签名（要按指纹、登录成功），但 `git pull/push` 却回落到问密码。

**原因**：Git for Windows 自带一个 MSYS/Cygwin 版 ssh（`C:\Program Files\Git\usr\bin\ssh.exe`），它**不认** Windows 命名管道 `\\.\pipe\openssh-ssh-agent`，只看 `SSH_AUTH_SOCK` 指向的 Cygwin 套接字——所以看不到我们的 agent。原生 `ssh.exe`（`C:\Windows\System32\OpenSSH\ssh.exe`）才走命名管道。

**修复（已验证）**：让 git 改用原生 OpenSSH：

```powershell
git config --global core.sshCommand "C:/Windows/System32/OpenSSH/ssh.exe"
```

之后 `git` 与手动 `ssh` 一致，走命名管道 → 设备钥匙 → 触发指纹。

### 启用前提：停用 Windows 内置 ssh-agent 服务

内置 `ssh-agent` 服务占用同一个管道名，不停掉我们的 agent 建不了管道（日志会报 `SSH agent 管道创建失败`）。

```powershell
Stop-Service ssh-agent
Set-Service ssh-agent -StartupType Disabled
```

然后在客户端「安装配置」页打开「启用 SSH agent」，重启 Service。

### 验证与端口

- `ssh-add -l` → 应列出**设备里**的 SSH 钥匙（名字/指纹和密钥页一致）。空的话去密钥页 SSH → 生成一把，并把公钥加到服务器 `~/.ssh/authorized_keys`。
- `SSH_AUTH_SOCK` 对原生 OpenSSH 应保持**为空**（它固定走命名管道，不看这个变量）。
- 端口非 22 时，在 `~/.ssh/config` 里配（推荐）：
  ```
  Host your-server           # 你的主机别名 / IP
      Port 2202              # 非默认端口时填写
      IdentityAgent \\.\pipe\openssh-ssh-agent
  ```

## 其他已解决的 Windows 特有坑

- **连接后"未找到 CMD/RSP 特征" / OTA `0x80070016`**：同设备上连续多次 `GetGattServicesForUuidAsync` 会触发 `ERROR_BAD_COMMAND`；改为**一次 `GetGattServicesAsync` 枚举全部服务**。也要清掉僵尸 `ImmurokService.exe` 进程（会占着 GATT）。
- **OTA 特征无 Notify**：OTA 走"写入后每 200ms 轮询读"，不要订阅 CCCD。
- **管道 `UnauthorizedAccessException`**：DACL 要含 `CreateNewInstance`，否则长命令占着实例时并发建新实例被拒。
- **托盘图标 `ArgumentException`**：H.NotifyIcon 的 `IconSource` 必须指向 `.ico`（BMP 帧），不能用 PNG。
- **BLE 连接模型**：不扫广播，而是枚举**已配对**设备找 immurok 服务（设备是 HID 键盘锚点，系统自动保持连接）。
- **命名空间勿用 `System`**：`ImmurokService.System` 会和全局 `System` 撞名，已改 `ImmurokService.Platform`。
