# ImmurokCredentialProvider (C++)

Windows 锁屏/登录界面的指纹解锁 Credential Provider（COM DLL，运行于 LogonUI 进程）。

## 来源与归属

CP 骨架（`CSampleProvider` / `CSampleCredential` / `Dll` / `helpers` / `common.h`）**派生自微软官方 [Credential Provider 示例](https://github.com/microsoft/Windows-classic-samples/tree/main/Samples/CredentialProvider)（MIT 许可）**，这些文件保留其原始微软版权头。

`CPipeListener.cpp` / `.h`（命名管道监听）是 **immurok 独立编写的实现**（Apache 2.0），仅依据 Win32 命名管道 API 与本目录约定，不含第三方代码。

immurok 相对微软示例的改动：

- CLSID 换为独立值 `{C433E3A5-FA34-42B1-A94C-5D793D2EEA83}`（`guid.h`）
- 新增命名管道 `\\.\pipe\ImmurokCredentialProvider`，载荷改为 UTF-16 用户名/密码（`CPipeListener.cpp`）
- DLL/模块名 → `ImmurokCredentialProvider.dll`（`.def`、`Register.reg`）

## 解锁原理（三块）

1. `CPipeListener`：CP 在自己线程用 `CreateNamedPipe("\\.\pipe\ImmurokCredentialProvider")` + overlapped `ReadFile` 读取 UTF-16 用户名/密码（`<username>\0<password>\0`）。
2. Service（`ImmurokService/System/ScreenUnlocker.cs`）在指纹验签通过且锁屏时，连接该管道写入用户名+密码。
3. `CSampleProvider::OnUnlockingStatusChanged` → `CredentialsChanged` → LogonUI 重枚举 → `CSampleCredential::GetSerialization` 构造 `KERB_INTERACTIVE_UNLOCK_LOGON` 提交 → 解锁。

## 注册 / 反注册

以管理员导入 `Register.reg`（安装器执行）；卸载导入 `Unregister.reg`。
DLL 需部署到 `C:\Program Files\immurok\`（与 `Register.reg` 中路径一致）。

## ⚠️ 调试须知（务必在可回滚虚拟机中）

- CP 崩溃/异常会导致 **LogonUI 无法登录**。全程在带快照的 VM 中开发，始终保留内置密码 CP 作后备。
- 生产前的安全硬化（见实施方案 §11）：把 CP 管道 ACL 从 Everyone 收紧到仅 `SYSTEM`（当前 alpha 为便于跑通，管道用 NULL DACL，任意本地进程可写）。
- CP DLL 建议 Authenticode 签名，部分组策略会拒载未签名 CP。
- 替换 `tileimage.bmp` 为 immurok 磁贴图标。
