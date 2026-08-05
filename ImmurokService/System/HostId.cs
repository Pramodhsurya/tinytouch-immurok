using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace ImmurokService.Platform;

/// <summary>
/// 主机标识（16B），对应 macOS 的 IOPlatformUUID 前 16B。
/// 取 <c>HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid</c> 的 MD5 前 16 字节。
/// 仅用于本地设备标识/缓存，不作为密钥材料（HKDF salt/info 是固定 ASCII 常量，见 ImmurokSecurity）。
/// </summary>
public static class HostId
{
    public static byte[] Get()
    {
        string guid = ReadMachineGuid() ?? Environment.MachineName;
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(guid));
        return hash[..16];
    }

    private static string? ReadMachineGuid()
    {
        try
        {
            using RegistryKey? key = RegistryKey
                .OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid") as string;
        }
        catch
        {
            return null;
        }
    }
}
