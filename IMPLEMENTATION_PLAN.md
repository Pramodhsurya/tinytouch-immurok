# immurok Windows 版 —— 详细实施方案

> 版本：v1（方案评审稿）
> 目标读者：负责实现 Windows 版的工程师 / AI Agent
> 约束：本方案及后续所有代码只在 `app-win/` 目录内新建与编辑，不修改仓库任何其他文件。
> 本文档中的所有协议常量均**以 macOS 源码为准**核对提取（`app-macos/Sources/*.swift`），并已修正历史 `windows/ARCHITECTURE.md` 中的占位/过时值。

---

## 0. TL;DR（结论先行）

| 决策项 | 结论 | 一句话理由 |
|--------|------|-----------|
| 托管组件运行时 | **.NET 8（LTS）** | immurok 的 IPC 是文本协议、不依赖 BinaryFormatter，因此没有把托管侧钉在 .NET Framework 4.8 的理由；.NET 8 是 LTS，更适合长期维护的安全产品 |
| 屏幕解锁 | **C++ Credential Provider + 命名管道**（基于微软官方 CP 示例原理） | Windows 锁屏在 Secure Desktop，拒绝 HID 注入；只有 CP 能在 LogonUI 里提交凭据 |
| BLE 协议 | **完整复刻 immurok 自有协议**（GATT UUID/命令枚举/HKDF-HMAC 与固件一致） | 设备是 immurok 自有硬件——解锁走标准 Credential Provider 原理，BLE 栈完整复刻固件协议 |
| 客户端 UI | **WPF + Fluent（WPF-UI / lepoco.wpfui）** | 用户选定 Windows 原生 Fluent 风格；WPF-UI 提供 WinUI 观感且原生支持 .NET 8 |
| IPC | 命名管道，两条：Client↔Service（文本协议，复用 macOS）+ Service↔CP（原始 UTF-16 用户名/密码） | 前者复用成熟文本协议、跨语言友好；后者与 CP 的 C++ 读取逻辑严格对齐 |
| 密钥存储 | DPAPI（LocalMachine）+ Windows Credential Manager | Windows 平台标准安全存储，替代 macOS Keychain |

**为什么不选 .NET Framework 4.8**：解锁的核心是 **C++ 的 Credential Provider DLL + 命名管道握手**，这部分与托管侧 .NET 版本完全无关。托管侧唯一可能把人钉在 4.8 的理由是 `BinaryFormatter` 序列化 IPC——但 immurok 的 IPC 本就是纯文本协议（`AUTH:user:service` → `OK`/`DENY`），直接复用 macOS 文本协议即可，根本用不到 `BinaryFormatter`。去掉这个约束后，.NET 8 在 WinRT BLE、Fluent UI（WPF-UI）、Windows Service 托管、长期支持上全面更优。

> 注：Credential Provider 必须是 C++/COM，**无论托管侧选哪个 .NET 版本都一样**。所以"标准 CP 解锁原理"这一硬要求，在 .NET 8 方案下 100% 满足。

---

## 1. 背景与目标

immurok 是一套蓝牙指纹认证系统（CH592F + R559S 指纹传感器），当前有 macOS（Swift Menu Bar App）与 Linux（Rust）版本。本方案为 **Windows 版**，目标是在功能与观感上尽量对齐 macOS 版：

- 指纹解锁 Windows 锁屏 / 登录
- 指纹替代密码做权限授权（Windows 无 PAM，用 Credential Provider 统一承接）
- 指纹管理（录入 / 删除 / 列表）
- 设备配对（ECDH P-256）
- OTA 固件升级
- 系统托盘常驻 + 配置界面

**固件不改动**，Windows 版必须完整复用现有 BLE 协议与安全机制。

### 与 macOS 的功能对齐清单（feature parity）

| 能力 | macOS 实现 | Windows 实现 | 备注 |
|------|-----------|-------------|------|
| 屏幕解锁 | 键盘模拟输入密码 | Credential Provider 提交凭据 | 机制不同，体验一致 |
| 系统权限授权 | PAM 模块（Unix Socket） | Credential Provider（登录/解锁场景） | Windows 无 sudo/PAM；UAC 提权无法被第三方 CP 接管，见 §11 风险 |
| BLE 通信 | App 进程内 CoreBluetooth | Windows Service + WinRT GATT | Service 在锁屏/未登录时也维持连接 |
| 安全配对 | CryptoKit ECDH+HKDF+HMAC | .NET `System.Security.Cryptography` 等价实现 | 参数必须逐字节一致 |
| 指纹管理 | FingerprintView | WPF FingerprintPage | UI 重绘为 Fluent |
| OTA 升级 | FirmwareUpdateService | Service OTA 模块（复用协议） | .imfw 包格式不变 |
| SSH agent / TOTP / API vault | 有 | **本期不做**（见 §12 范围） | 优先打通"解锁"闭环 |

---

## 2. 总体架构

```
┌───────────────────────────────────────────┐
│  ImmurokClient  (WPF, .NET 8, Fluent)      │  用户态 UI：配对/指纹/状态/OTA/托盘
└───────────────┬───────────────────────────┘
                │  命名管道  \\.\pipe\immurok        （文本协议，复用 macOS）
┌───────────────▼───────────────────────────┐
│  ImmurokService (Windows Service, .NET 8)  │  核心：WinRT BLE、安全协议、密钥、锁屏检测
│    Ble / Security / Ipc / System           │  运行于 Session 0，随开机自启
└───────┬───────────────────────────────────┘
        │  命名管道  \\.\pipe\ImmurokCredentialProvider （原始 UTF-16 user+password）
┌───────▼───────────────────────────────────┐
│  ImmurokCredentialProvider (C++/COM DLL)   │  运行于 LogonUI 进程，负责锁屏界面 & 提交凭据
└────────────────────────────────────────────┘

              ┌────────────────────────┐
              │ ImmurokCommon (.NET 8) │  共享库：IPC 协议常量、模型、BLE/安全常量
              └────────────────────────┘

        BLE (custom GATT + HKDF/HMAC)
ImmurokService  ◄──────────────────────►  immurok 设备 (CH592F + R559S)
```

### 解锁数据流（核心闭环）

```
1. 用户锁屏 → LogonUI 加载 ImmurokCredentialProvider.dll
2. CP 在自己线程里 CreateNamedPipe("\\.\pipe\ImmurokCredentialProvider")，阻塞等待连接
3. CP 显示磁贴"immurok 指纹解锁"
4. 用户触摸设备 → 设备经 BLE 发出 0x21 签名指纹匹配通知
5. Service（Session 0，锁屏时仍在跑）收到 0x21 → 用 shared_key 验 HMAC → 通过
6. Service 判定当前处于锁屏（WTSGetActiveConsoleSessionId + 会话锁状态）
7. Service 从 DPAPI/Credential Manager 取出登录用户名+密码
8. Service 连接 CP 的管道，WriteFile 写入 UTF-16 username\0 + password\0
9. CP 的 ReadFile 读到 → 置 _fUnlocked=TRUE → 调 provider->OnUnlockingStatusChanged()
10. CP 调 CredentialsChanged() → LogonUI 重新枚举 → 该磁贴 SetSelected 返回 autoLogon
11. LogonUI 调 GetSerialization → 用 username/password 构造 KERB_INTERACTIVE_UNLOCK_LOGON
12. 返回 CPGSR_RETURN_CREDENTIAL_FINISHED → 系统校验凭据 → 解锁成功
```

> 该链路遵循微软官方 SampleHardwareEventCredentialProvider（MIT）的模式：CP 端命名管道监听（immurok 独立实现）+ Service 端写管道（`ScreenUnlocker.Unlock`）+ `CSampleCredential::GetSerialization`（微软示例的标准 KERB 序列化）三块。

---

## 3. 从 macOS 源码提取的权威常量（实现时必须逐字节对齐）

> ⚠️ 以下值均来自 `app-macos/Sources/`，是唯一权威来源。历史 `windows/ARCHITECTURE.md` 中的 GATT UUID（`12340010-...`）、命令枚举、HKDF salt/info 均为占位/过时值，**以本节为准**。

### 3.1 GATT 服务与特征（`BLEManager.swift`）

| 名称 | UUID | 属性 |
|------|------|------|
| immurok Service | `45529919-7668-48f9-b9fe-e4eabe6595d9` | — |
| CMD 特征（写） | `8a537e1f-3992-4b2c-8b77-8d4e778186e1` | Write |
| RSP 特征（通知） | `76a1660d-8cf6-44d1-b3fc-70486028e289` | Notify |
| Device Info Service | `180A` | 标准 |
| Firmware Rev 特征 | `2A26` | Read |
| Battery Service | `180F` | 标准 |
| Battery Level 特征 | `2A19` | Read/Notify |
| OTA Service | `d29005de-1391-4a54-8168-bf4e3c080430` | — |
| OTA 特征 | `c75f4c30-9a2d-4445-92e0-0e034c53d092` | Write/Notify |

### 3.2 命令操作码 `ImmurokCommand`（写入 CMD 特征）

```
getStatus    = 0x01    getBattRaw   = 0x02
enrollStart  = 0x10    enrollCancel = 0x11    deleteFP = 0x12    fpList = 0x13
fpMatchAck   = 0x22
pairInit     = 0x30    pairConfirm  = 0x31    pairStatus = 0x32
authRequest  = 0x33    pairButton   = 0x34    gateCancel = 0x37    challenge = 0x38
slotStatus   = 0x39    slotClear    = 0x3C
keyCount = 0x60  keyRead = 0x61  keyWrite = 0x62  keyDelete = 0x63  keyCommit = 0x64
keySign  = 0x65  keyGetPub = 0x66  keyGenerate = 0x67  keyResult = 0x68  keyOTPGet = 0x69
```

### 3.3 响应状态 `ImmurokStatus`（RSP 通知 payload[1] 或 payload[0]）

```
ok = 0x00   errTimeout = 0x06   errFpNotMatch = 0x07   waitFingerprint = 0x11
errWaitButton = 0xF0  (PAIR_INIT：等待物理按键确认)
errNeedsReset = 0xF1  (PAIR_INIT：设备仍有指纹，需先复位)
errBusy = 0xFD   errInvalidParam = 0xFE   errUnknown = 0xFF
linkParams: 设备因 BLE 连接参数不达标拒绝 ECDH → 返回 0xE1
```

### 3.4 录入进度事件 `FpEnrollEvent`（enroll 过程通知，payload[0]==0x11, len==4）

```
waiting = 0x00  captured = 0x01  processing = 0x02  liftFinger = 0x03
complete = 0x04  overlap = 0x06 (重叠过多，移指重按)  failed = 0xFF (含 0xFD/0xFE)
```

### 3.5 关键异步通知帧格式（RSP 特征 Notify）

| 首字节 | 长度 | 含义 | 处理 |
|--------|------|------|------|
| `0x21` | 11B | **签名指纹匹配**：`[0x21][page_id:2B LE][hmac:8B]` | 验 HMAC，见 §3.6；解锁触发点 |
| `0x23` | 1B | 指纹匹配但未签名 / 特定事件 | 见 `BLEManager.swift:2221` |
| `0x11` | 4B | 录入进度事件 | 解析 `FpEnrollEvent` |
| `0xF0` | 6B | 配对相关状态 | 见 `BLEManager.swift:2154` |
| `0xE1` | 1B | 连接参数不达标/按键相关 | 重协商连接参数 |

### 3.6 安全协议（`ImmurokSecurity.swift`，必须精确复现）

- **曲线**：P-256（NIST secp256r1）。设备公钥为 **compressed 表示，33 字节**。
- **ECDH**：`sharedSecret = ECDH(ephemeralPriv, devicePub)`。
- **HKDF**（RFC 5869，HMAC-SHA256）：
  - IKM = ECDH 原始共享密钥（`sharedSecretFromKeyAgreement` 的原始 32B）
  - **Salt = ASCII `"immurok-pairing-salt"`**
  - **Info = ASCII `"immurok-shared-key"`**
  - 输出 = **32 字节** shared_key
  - ⚠️ 修正：旧 ARCHITECTURE.md 写的 salt=hostId / info="immurok-pairing" 是错的。
- **0x21 通知 HMAC 校验**：
  - `message = data[0..3]`（即 `0x21` + page_id 2B，共 3 字节）
  - `expected = HMAC-SHA256(shared_key, message)[0..8]`（**截断前 8 字节**）
  - 与 `data[3..11]` 逐字节比较，相等即有效，返回 page_id（`UInt16 LE`）。
- **挑战响应**（challenge = 0x38）：
  - 主机生成 8 字节随机 nonce 下发
  - 设备回 `HMAC-SHA256(shared_key, nonce)[0..8]`
  - 主机本地算同值比对，验证"设备确实持有 shared_key"。
- **.NET 8 对应实现**：`ECDiffieHellman`（`ECCurve.NamedCurves.nistP256`）导入 compressed 公钥需自行做点解压（.NET 的 `ECDiffieHellmanPublicKey`/`ECParameters` 只吃未压缩点，需实现 secp256r1 的 √ 解压，或引入 BouncyCastle 做 compressed→uncompressed）；HKDF 用 `System.Security.Cryptography.HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 32, salt, info)`；HMAC 用 `HMACSHA256`，再取前 8 字节。**建议引入 BouncyCastle.Cryptography 仅用于 compressed 公钥解压**，其余用 BCL。

### 3.7 IPC 文本协议（`PAMSocketServer.swift`，Client↔Service 复用）

macOS 侧监听 `~/.immurok/pam.sock`（Unix socket，`chmod 0600`）。Windows 侧改为命名管道 `\\.\pipe\immurok`，**文本协议原样复用**：

请求 → 响应：
```
STATUS                    → STATUS:1:deviceName | STATUS:0:
AUTH:username:service     → OK | DENY | SKIP | BUSY | TIMEOUT | REJECT | ERROR:*
FP:LIST                   → OK:count | ERROR:NOT_CONNECTED
FP:ENROLL:slot            → OK:ENROLL_STARTED | ERROR:ENROLL_FAILED | ERROR:INVALID_SLOT
FP:DELETE:slot            → OK:DELETED | ERROR:DELETE_FAILED | ERROR:NOT_CONNECTED
FP:STATUS                 → OK:IDLE | ...
OTA:VERSION|INFO|ERASE|HEADER|WRITE|VERIFY|END  → OK | ERROR:*（子协议见 §3.8）
AGENT_APPROVE:...         → OK | REJECT | DENY（本期可暂不实现）
```
错误族：`ERROR:INVALID_FORMAT` / `ERROR:UNKNOWN_COMMAND` / `ERROR:NOT_CONNECTED` / `ERROR:OTA_NOT_AVAILABLE` / `ERROR:INVALID_SLOT` / `ERROR:HMAC_MISMATCH` 等。

**CP 专用扩展**（Service↔CP 走独立管道，见 §5，不走本文本协议）：Service 直接写 `UTF-16 username\0 + password\0`。若后续需要 CP 主动查询状态，可加 `GETPASSWORD:username` / `WAITFP:timeout`，但**首版保持简单：Service 主动推送，CP 只读**。

### 3.8 OTA 子协议（复用 macOS，`PAMSocketServer.swift:879+`）
```
OTA:INFO    → 返回当前/可用固件版本信息
OTA:ERASE   → 擦除 OTA 分区 → OK | ERROR:OTA_NOT_AVAILABLE
OTA:HEADER:<size>:<base64>  → 写头 → OK | ERROR:INVALID_HEADER_SIZE | ERROR:HEADER_TIMEOUT
OTA:WRITE:<offset>:<base64> → 写数据块 → OK | ERROR:INVALID_OFFSET | ERROR:WRITE_FAILED
OTA:VERIFY  → 校验 → OK | ERROR:VERIFY_TIMEOUT | ERROR:HMAC_MISMATCH
OTA:END     → 收尾/跳转
```
.imfw 包为 AES-128-CTR 加密 + HMAC-SHA256 签名，格式与 macOS `FirmwareUpdateKit` 一致。

---

## 4. 解锁机制详解（基于微软官方 CP 示例，逐块说明）

### 4.1 三块实现对照

| 机制 | 作用 | immurok 实现 | 来源 |
|------|------|-------------|------|
| CP 端命名管道监听 | 起命名管道服务端，读 user/password | `ImmurokCredentialProvider/CPipeListener.cpp` | immurok 独立实现（Apache 2.0） |
| `CSampleCredential::GetSerialization` | 用 user/password 构造 `KERB_INTERACTIVE_UNLOCK_LOGON` 提交 | `ImmurokCredentialProvider/CSampleCredential.cpp` | 微软 CP 示例（MIT），改 CLSID |
| `CSampleProvider::OnUnlockingStatusChanged` | 收到凭据后调 `CredentialsChanged` 触发 LogonUI 重枚举 | `ImmurokCredentialProvider/CSampleProvider.cpp` | 微软 CP 示例（MIT） |
| Service 端写管道 | `WriteFile` 写 UTF-16 user+password | `ImmurokService/System/ScreenUnlocker.cs` | 标准 Win32 P/Invoke |

### 4.2 CP 端要点（C++/ATL）
- 管道名：`\\.\pipe\ImmurokCredentialProvider`，`CreateNamedPipe(PIPE_ACCESS_INBOUND, PIPE_TYPE_MESSAGE|PIPE_READMODE_MESSAGE|PIPE_WAIT, PIPE_UNLIMITED_INSTANCES, ...)`。
- **ACL**：当前给 Everyone（NULL DACL）。⚠️ 安全收紧建议见 §11——生产版应把管道 ACL 限到 `SYSTEM`（Service 以 LocalSystem 运行）以防本地提权面。
- 读到 user/password（UTF-16，含 `\0`）后置 `_fReady=TRUE`，调 `OnUnlockingStatusChanged`。
- `SetUsageScenario` 只在 `CPUS_LOGON` / `CPUS_UNLOCK_WORKSTATION` 创建 credential + 启动 PipeListener（沿用微软示例的生命周期）。
- `GetSerialization`：`KerbInteractiveUnlockLogonInit` → `Pack` → `RetrieveNegotiateAuthPackage` → 设 `clsidCredentialProvider = CLSID_ImmurokProvider` → `CPGSR_RETURN_CREDENTIAL_FINISHED`。
- 失败时 `ReportResult` 清空密码字段。

### 4.3 CP 注册（注册表）
新生成一个**唯一 CLSID**（用 `uuidgen` 独立生成，记为 `CLSID_ImmurokProvider`）：
```
[HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\{CLSID_ImmurokProvider}]
@="ImmurokCredentialProvider"
[HKCR\CLSID\{CLSID_ImmurokProvider}]
@="ImmurokCredentialProvider"
[HKCR\CLSID\{CLSID_ImmurokProvider}\InprocServer32]
@="C:\\Program Files\\immurok\\ImmurokCredentialProvider.dll"
"ThreadingModel"="Apartment"
```
提供 `Register.reg` / `Unregister.reg`（放 `ImmurokCredentialProvider/`），安装器写入、卸载器删除。

### 4.4 Service 端触发要点
- 锁屏检测：`WTSGetActiveConsoleSessionId` + 会话锁状态；或监听 `SessionSwitchReason.SessionLock/SessionUnlock`。⚠️ Service 在 Session 0 收不到交互会话的 `SystemEvents.SessionSwitch`，需用 `WTSRegisterSessionNotification`（配合隐藏消息窗）或轮询锁屏状态（本项目用 `WTSQuerySessionInformation` 查 `WTSSessionInfoEx`，见 `SessionMonitor.cs`）。
- 收到 0x21 且验签通过且判定锁屏 → 取用户名（当前控制台会话用户）+ DPAPI 密码 → `ScreenUnlocker.Unlock(user, password)`。
- **主动触发/预授权**：与 macOS 的 pre-auth 一致——若指纹先到、CP 还没准备好，可置 pre-auth 标志，CP 侧一连上就推送。

---

## 5. 组件文件级设计

> 目录约束：全部位于 `app-win/`。以下为规划目录树（本方案文档随附骨架说明，具体代码文件在后续阶段落地）。

```
app-win/
├── IMPLEMENTATION_PLAN.md            (本文档)
├── README.md                        (构建/安装/调试速查，随附)
├── immurok-win.sln                  (VS 解决方案，阶段1创建)
├── Directory.Build.props            (统一 TFM/版本号)
├── build.ps1 / install.ps1 / uninstall.ps1
│
├── ImmurokCommon/                   # 共享类库 (.NET 8, net8.0-windows10.0.19041.0)
│   ├── ImmurokCommon.csproj
│   ├── Protocol/
│   │   ├── IpcProtocol.cs           # ← PAMSocketServer.swift 文本协议常量/解析（§3.7）
│   │   └── PipeNames.cs             # \\.\pipe\immurok, \\.\pipe\ImmurokCredentialProvider
│   ├── Ble/
│   │   ├── BleConstants.cs          # ← §3.1 GATT UUID
│   │   ├── ImmurokCommand.cs        # ← §3.2 命令枚举
│   │   └── ImmurokStatus.cs         # ← §3.3/§3.4 状态与录入事件枚举
│   └── Models/
│       ├── DeviceStatus.cs / FingerprintSlot.cs / PairingResult.cs / OtaProgress.cs
│
├── ImmurokService/                  # Windows Service (.NET 8 Worker Service)
│   ├── ImmurokService.csproj        # Microsoft.Extensions.Hosting.WindowsServices
│   ├── Program.cs                   # HostBuilder + UseWindowsService()
│   ├── Worker.cs                    # 生命周期编排（← AppDelegate.swift 初始化职责）
│   ├── Ble/
│   │   ├── BleManager.cs            # ← BLEManager.swift：WinRT 扫描/连接/GATT 读写/重连
│   │   ├── BleReconnector.cs        # 断线自动重连、连接参数协商
│   │   └── NotificationRouter.cs    # RSP 通知帧分发（0x21/0x23/0x11/0xF0/0xE1）
│   ├── Security/
│   │   ├── ImmurokSecurity.cs       # ← ImmurokSecurity.swift：ECDH/HKDF/HMAC（§3.6）
│   │   ├── PairingStore.cs          # DPAPI 存 shared_key（§7）
│   │   └── CredentialStore.cs       # Credential Manager 存登录密码（§7）
│   ├── Ipc/
│   │   ├── PipeServer.cs            # ← PAMSocketServer.swift：命名管道文本协议服务端
│   │   └── CommandHandlers.cs       # STATUS/AUTH/FP:*/OTA:* 分发
│   ├── System/
│   │   ├── SessionMonitor.cs        # 锁屏检测（WTS/轮询，§4.4）
│   │   ├── ScreenUnlocker.cs        # 写 CP 管道（P/Invoke WaitNamedPipe/CreateFile/WriteFile）
│   │   ├── HostId.cs                # Machine GUID → 16B（§7）
│   │   └── PowerManager.cs          # 休眠/唤醒重连
│   ├── Ota/
│   │   └── OtaEngine.cs             # ← FirmwareUpdateKit：.imfw 解析、分块写、校验
│   └── install/
│       ├── ImmurokService.rc?       # 服务安装通过 sc.exe / installer，见 §9
│
├── ImmurokCredentialProvider/       # Credential Provider (C++/ATL, x64)
│   ├── ImmurokCredentialProvider.vcxproj
│   ├── dllmain.cpp / Dll.cpp / *.def # ← 微软 CP 示例 Dll.cpp / .def
│   ├── guid.h / guid.cpp            # 新 CLSID_ImmurokProvider（uuidgen 生成）
│   ├── CSampleProvider.{h,cpp}      # ← 微软 CP 示例：ICredentialProvider(2)
│   ├── CSampleCredential.{h,cpp}    # ← 微软 CP 示例：GetSerialization/KERB
│   ├── CPipeListener.{h,cpp}        # CP 端管道服务端（immurok 独立实现）
│   ├── helpers.{h,cpp} / common.h / guid… # ← 微软 CP 示例辅助
│   ├── resources.rc / tileimage.bmp # 磁贴图（换 immurok 图标）
│   ├── Register.reg / Unregister.reg
│   └── readme.txt
│
└── ImmurokClient/                   # WPF 客户端 (.NET 8, WPF-UI Fluent)
    ├── ImmurokClient.csproj         # UseWPF=true; PackageReference WPF-UI
    ├── App.xaml(.cs)                # 单实例、托盘启动
    ├── app.manifest                 # requireAdministrator? 见 §9（配置需写 HKLM/装 CP → 需提权入口）
    ├── Services/
    │   ├── PipeClient.cs            # ← 连接 \\.\pipe\immurok，文本协议
    │   └── ServiceController.cs     # 启停/查询 Windows Service
    ├── ViewModels/                  # ← AppViewModel.swift + 各 Tab VM
    │   ├── MainViewModel.cs / DeviceViewModel.cs / FingerprintViewModel.cs
    │   ├── SetupViewModel.cs / StatusViewModel.cs / AboutViewModel.cs
    │   ├── ViewModelBase.cs
    ├── Views/                       # ← ContentView.swift + SettingsTabViews.swift
    │   ├── MainWindow.xaml(.cs)     # NavigationView（Fluent 左侧导航）
    │   ├── DevicePage / FingerprintPage / SetupPage / StatusPage / AboutPage .xaml
    │   └── Dialogs/ (EnrollDialog, PairDialog, SetPasswordDialog)
    ├── Tray/
    │   └── TrayIcon.cs              # H.NotifyIcon（WPF-UI 生态，替代 macOS MenuBarExtra）
    ├── Assets/                      # 图标（复用 immurok-icon/、Resources/icon.png）
    └── Localization/                # 复用 macOS Resources/Localization/*.json（zh/en/...）
```

### 5.1 各组件职责与 macOS 源文件映射（重点）

| Windows 类 | 对应 macOS | 移植要点 |
|-----------|-----------|---------|
| `Ble/BleManager.cs` | `BLEManager.swift`（109KB，最核心） | 用 `Windows.Devices.Bluetooth.*` WinRT。扫描→匹配 Service UUID→`GetGattServicesAsync`→拿 CMD(写)/RSP(通知)→`WriteValueAsync`/`ValueChanged`。连接参数协商注意 §11。 |
| `Security/ImmurokSecurity.cs` | `ImmurokSecurity.swift` | §3.6 参数逐字节对齐；compressed 公钥解压需 BouncyCastle。 |
| `Ipc/PipeServer.cs` | `PAMSocketServer.swift` | 文本协议原样；`NamedPipeServerStream`（`PipeSecurity` 限当前用户）。 |
| `System/ScreenUnlocker.cs` | —（Windows 特有） | P/Invoke `WaitNamedPipe/CreateFile/WriteFile`，写 UTF-16 user+password。 |
| `Worker.cs` | `AppDelegate.swift` | 编排：启动 BLE、起管道服务端、注册会话监听、处理 0x21→解锁/AUTH。 |
| `CSampleCredential.cpp` | 微软 CP 示例 `CSampleCredential.cpp`（MIT） | `GetSerialization` 构造 KERB。 |
| `CPipeListener.cpp` | —（immurok 独立实现） | CP 端命名管道服务端。 |

---

## 6. UI 设计（Windows Fluent 原生风格）

- **框架**：WPF + **WPF-UI（lepoco/wpfui）** 提供 WinUI 3 观感的 Fluent 控件（`NavigationView`、`Card`、`ToggleSwitch`、Mica/Acrylic 背景、明暗自适应）。原生 .NET 8。
- **窗口结构**：主窗口用 `FluentWindow` + 左侧 `NavigationView`，导航项对应 macOS 的 Tab：

| macOS Tab | Windows 页面（Fluent NavigationViewItem） | 功能 |
|-----------|-------------------------------------------|------|
| FingerprintTabView | FingerprintPage | 指纹录入/删除/列表（进度用 §3.4 事件驱动动画） |
| DeviceTabView | DevicePage | 扫描/配对/连接状态/电量/固件版本 |
| PermissionsTabView | SetupPage | 安装 CP、配置登录密码、服务状态、开机自启 |
| StatusTabView | StatusPage | 整体健康检查（设备/服务/CP/配对/密码） |
| AboutTabView | AboutPage | 版本、OTA 升级入口 |

- **系统托盘**：`H.NotifyIcon`（WPF-UI 推荐），图标反映连接状态（已连/未连/未配对），右键菜单：打开设置、锁屏、退出。替代 macOS 的 `MenuBarExtra`（macOS 无 Dock 图标用 `LSUIElement`；Windows 用托盘常驻 + 主窗口按需显示）。
- **视觉对齐**：配色/圆角/留白参考 macOS 设置窗，但控件采用 Fluent 原生；明暗跟随系统主题。图标复用仓库 `immurok-icon/` 与 `app-macos/Resources/icon.png`。
- **本地化**：直接复用 `app-macos/Resources/Localization/*.json`（含中文——项目要求中文交流，UI 默认中文），阶段4 写一个 JSON→WPF 资源的加载器。

---

## 7. 安全存储映射

| 数据 | macOS | Windows | 实现 |
|------|-------|---------|------|
| 配对 shared_key (32B) | Keychain `com.immurok.shared-key` | DPAPI(LocalMachine) → `%ProgramData%\immurok\pairing.dat` | `ProtectedData.Protect(key, null, DataProtectionScope.LocalMachine)`（Service 以 LocalSystem 跑，需 LocalMachine 作用域，CP 侧不直接读 key） |
| 登录密码 | Keychain `com.immurok.password` | Windows Credential Manager（Generic）`immurok/login` | `CredWrite`/`CredRead`（P/Invoke 或 CredentialManagement 库）；Service 读取后经 CP 管道下发 |
| 已验证设备 UUID | Keychain `com.immurok.verified-device` | DPAPI → `%ProgramData%\immurok\verified.dat` | 同 shared_key |
| Host ID (16B) | IOPlatformUUID 前 16B | `HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid` → 取 16B（或 MD5 前 16B） | 仅用于本地标识，非密钥材料 |

> 说明：HKDF 的 salt/info 是**固定 ASCII 常量**（§3.6），与 Host ID 无关；Host ID 只用于设备标识/缓存。

---

## 8. IPC 序列化说明（为什么不用 BinaryFormatter）

一些 CP 解锁实现的 Client↔Service 管道用 `BinaryFormatter` 序列化对象。immurok 的 IPC 本是**文本行协议**（§3.7），我们直接复用：
- 帧格式建议：`4字节小端长度 + UTF-8 文本`（长度前缀 + 文本 payload，非二进制对象），避免 `BinaryFormatter`（.NET 8 已移除且不安全）。
- OTA 的二进制块用 base64 内嵌进文本命令（macOS 已如此，§3.8）。
- Service↔CP 管道**不用**这套：直接写裸 UTF-16 user/password。

---

## 9. 打包与安装

- **安装器**：推荐 **Inno Setup**（免费、脚本简单）或 WiX（MSI，企业友好）。首版 Inno Setup。
- 安装步骤：
  1. 拷贝 `ImmurokService.exe`、`ImmurokClient.exe`、`ImmurokCredentialProvider.dll`、依赖到 `C:\Program Files\immurok\`。
  2. 注册 Service：`sc create ImmurokService binPath= "...\ImmurokService.exe" start= auto obj= LocalSystem`（或安装器原生服务支持）。
  3. 注册 CP：导入 `Register.reg`（写 §4.3 注册表键）。
  4. Client 加入当前用户启动项（`HKCU\...\Run` 或计划任务）。
- **提权**：写 HKLM/注册 CP/装服务需管理员——安装器请求 UAC 提权；Client 内"安装 CP/配置密码"等操作走单独的提权子进程或引导用户以管理员运行配置向导。
- **卸载**：`Unregister.reg` 删 CP 键、`sc delete ImmurokService`、删文件、清 `%ProgramData%\immurok`、清 Credential Manager 条目。
- **签名**：Credential Provider DLL 与 Service 建议 Authenticode 签名（CP 未签名在部分策略下会被 LogonUI 拒载）。

---

## 10. 分阶段实施计划

> 每阶段末尾均有**可验证的里程碑**。CP 相关阶段务必在虚拟机中开发调试（崩溃会导致无法登录）。

### 阶段 0：脚手架（0.5 周）
1. 建 `app-win/immurok-win.sln` + 4 个项目 + `ImmurokCommon` 常量（§3.1–3.4 全部枚举）。
2. `Directory.Build.props` 统一 TFM `net8.0-windows10.0.19041.0`、版本号。
3. **验证**：`dotnet build` 全绿；C++ CP 空壳能编译出 DLL。

### 阶段 1：Service + BLE（2–3 周）
1. `BleManager.cs`：WinRT 扫描→连接→发现 immurok Service→CMD/RSP 特征→订阅通知→自动重连。
2. 实现 `GetStatus`/`FpList`/基础通知解析。
3. `PipeServer.cs` 起框架，实现 `STATUS`。
4. **验证**：命令行管道客户端发 `STATUS` → 返回 `STATUS:1:deviceName`，确认设备已连。⚠️ 早期就要验证 **Session 0 里 WinRT BLE 能否工作**（§11 风险1）。

### 阶段 2：安全配对 + 认证（1–2 周）
1. `ImmurokSecurity.cs`：ECDH/HKDF/HMAC，参数逐字节对齐 §3.6；写单元测试用 macOS 已知向量比对。
2. DPAPI 存 shared_key、Credential Manager 存密码。
3. 配对流程（pairInit/pairConfirm）+ 0x21 通知验签 + challenge。
4. 管道命令 `PAIR:*`、`FP:ENROLL/DELETE/LIST`、`AUTH`。
5. **验证**：完成配对；触摸设备 → 0x21 HMAC 验证通过；录入/删除指纹可用。

### 阶段 3：Credential Provider 解锁（2–3 周，VM 中）
1. 基于微软官方 CP 示例搭 C++ CP，换 CLSID 与磁贴图。
2. 实现 `CPipeListener`（独立）+ `GetSerialization` + `OnUnlockingStatusChanged`（微软示例）。
3. Service 侧 `SessionMonitor` + `ScreenUnlocker` 打通触发。
4. **验证**：锁屏 → 选 immurok 磁贴 → 触摸指纹 → 解锁。始终保留密码登录后备。

### 阶段 4：WPF 客户端（1–2 周）
1. WPF-UI Fluent 骨架 + `PipeClient`。
2. 5 个页面 + 3 个对话框 + 托盘图标。
3. 复用 macOS 本地化 JSON。
4. **验证**：纯 UI 完成"配对→录入指纹→配置密码→装 CP→锁屏解锁"全流程。

### 阶段 5：OTA + 打包 + 硬化（1–2 周）
1. `OtaEngine.cs` 复用 .imfw 协议，OTA 页面。
2. Inno Setup 安装器（服务/CP/客户端一键装）。
3. 安全硬化（§11：收紧管道 ACL、DLL 签名）、端到端回归。
4. **验证**：干净 VM 全新安装 → 全流程 → 卸载干净。

**总工期估算：约 7–13 周**（单人，含 CP 调试风险缓冲）。

---

## 11. 风险与验证策略

1. **Session 0 里 WinRT BLE 可用性（最高风险）**：Windows Service 运行在 Session 0，WinRT `Windows.Devices.Bluetooth` 在无交互会话/无包身份（unpackaged）下可能受限或需要额外 COM 初始化。**必须在阶段1最早验证**。缓解：`WinRT.Runtime` + 正确的 `[STAThread]`/COM apartment；若不可行，退路是把 BLE 逻辑放进一个随登录运行的用户态 broker 进程、Service 仅做锁屏时的中转（但会牺牲"未登录也连"能力，需与产品确认）。
2. **CP 调试致命性**：CP 崩溃/异常会让 LogonUI 无法登录。全程在快照可回滚的 VM 中开发；保留内置密码 CP 作后备；先跑通微软官方 `V2 sample` 再改。
3. **BLE HID 独占**：设备是 HID 键盘（连接锚点）+ 自定义 GATT。Windows HID 驱动可能独占，需验证 WinRT 能否同时访问自定义 GATT 特征。缓解：测试是否需要以非独占方式打开、或设备是否对 Windows 暴露 GATT。
4. **管道 ACL 提权面**：CP 管道当前 Everyone 可写（NULL DACL）——任意本地进程都能往里写 user/password 触发解锁尝试。**生产版应收紧**：CP 管道 ACL 限 `SYSTEM`（Service 以 LocalSystem 连接），Client↔Service 管道限当前登录用户 SID。硬化列入阶段5。
5. **UAC 提权 vs 锁屏解锁**：第三方 CP 能接管**登录/解锁**，但**无法接管 UAC 同意框**（`consent.exe` 只认内置凭据 UI）。所以 macOS 的"指纹替代 sudo/权限框"在 Windows 上只能覆盖到登录/解锁场景。需向产品明确 Windows 版权限授权的边界。
6. **单设备配对**：固件只允许一台主机配对，macOS/Windows 不能同时用同一设备。测试时注意先在设备上复位（enroll 存在时 pairInit 返回 `0xF1 errNeedsReset`）。
7. **compressed 公钥解压**：.NET BCL 不直接吃 compressed 点，引入 BouncyCastle 仅做解压；写单测确保与 macOS CryptoKit 产出的 shared_key 一致。
8. **DLL 签名策略**：部分组策略要求 CP DLL 签名；打包阶段准备 Authenticode 证书。

**验证工具**：阶段2 用 macOS 端跑一组已知 `(ephemeralPriv, devicePub) → shared_key`、`(shared_key, message) → hmac[0:8]` 向量，存进 `ImmurokCommon.Tests` 做跨平台一致性单测——这是保证"逐字节对齐固件"的关键防线。

---

## 12. 本期范围与后续

**本期（对齐 macOS 核心体验）**：设备配对、指纹管理、锁屏/登录指纹解锁、状态面板、OTA、Fluent UI + 托盘。

**明确暂不做（macOS 有，Windows 后续迭代）**：
- SSH agent（`SSHAgentServer.swift`）——Windows 走 OpenSSH agent / Pageant 协议，工程量单列。
- TOTP Quick Fill（`QuickFillPanel.swift`，`Ctrl+\`）。
- API key vault 读取（`imk://` URI）。
- AI agent 授权（`imk run --agent`，`AGENT_APPROVE` 管道命令）与 `imk` CLI 的 Windows 版。
- 1Password / Bitwarden 解锁注入（`BitwardenDetector.swift` 等）。

后续把这些作为独立里程碑排入路线图。

---

## 13. 待确认决策（请评审时拍板）

1. **BLE 承载进程**：确认接受 Service(Session 0) 直连 BLE 的方案（换取"未登录也维持连接"），还是允许退化为用户态 broker？（取决于阶段1 §11-1 验证结果）
2. **权限授权边界**：Windows 版是否只承诺"锁屏/登录解锁"，明确不覆盖 UAC 提权？（§11-5）
3. **安装器**：Inno Setup（首选）还是 WiX/MSI（企业）？
4. **本期范围**：是否同意把 SSH/TOTP/API vault/AI-agent 授权全部后置（§12）？
5. **代码签名**：是否已有可用的 Authenticode 证书用于 CP/Service 签名？

---

## 附录 A：关键参考源文件（仓库内）

- `app-macos/Sources/BLEManager.swift` — BLE 通信完整逻辑（Service `BleManager.cs` 直接参照）
- `app-macos/Sources/ImmurokSecurity.swift` — 安全协议，必须精确复现（§3.6 已提取）
- `app-macos/Sources/PAMSocketServer.swift` — IPC 文本协议定义（§3.7/§3.8 已提取）
- `app-macos/Sources/AppDelegate.swift` — 指纹匹配后的业务分派（PAM vs 解锁 vs pre-auth），`Worker.cs` 参照
- `app-macos/Sources/FirmwareUpdateService.swift` + `FirmwareUpdateKit/` — OTA 引擎参照
- 微软官方 Credential Provider 示例（MIT，github.com/microsoft/Windows-classic-samples）— CP 骨架与 `GetSerialization` 的参照；命名管道监听（`CPipeListener.cpp`）为 immurok 独立实现。
- 历史 `windows/ARCHITECTURE.md` — 早期规划，方向正确，但 GATT UUID / HKDF 参数为占位值，**以本文档 §3 为准**。
