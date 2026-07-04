using System;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace OppoPodsManager;

/// <summary>
/// Windows 设备发现：从注册表 BTHPORT\Parameters\Devices 找已配对的 OPPO/SPP 耳机。
/// 仅 Windows 可用；其它平台请实现各自的 IDeviceLocator。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsBluetoothLocator : IDeviceLocator
{
    public (ulong addr, string? name) Locate() => ReadBtDevice();

    private (ulong addr, string? name) ReadBtDevice()
    {
        try
        {
            Log.D("BT", "Locate: 扫描注册表已配对蓝牙设备...");
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters\Devices");
            if (key == null) { Log.D("BT", "Locate: 打不开 BTHPORT\\Devices 注册表项"); return (0, null); }

            var subKeys = key.GetSubKeyNames();
            Log.D("BT", $"Locate: 共 {subKeys.Length} 个已配对设备");

            // 第一轮：按名称匹配 "OPPO"
            foreach (var subName in subKeys)
            {
                if (subName.Length != 12 || !ulong.TryParse(subName,
                    System.Globalization.NumberStyles.HexNumber, null, out var addr))
                    continue;

                string? name = ReadBtDeviceName(key, subName);

                if (!string.IsNullOrEmpty(name) && name.Contains("OPPO", StringComparison.OrdinalIgnoreCase))
                {
                    Log.D("BT", $"Locate: 按名称命中 OPPO 设备 addr={addr:X12} name=\"{name}\"");
                    return (addr, name);
                }
            }

            // 第二轮：按服务 UUID 匹配（即使名称不含 "OPPO" 也能找到）
            foreach (var subName in key.GetSubKeyNames())
            {
                if (subName.Length != 12 || !ulong.TryParse(subName,
                    System.Globalization.NumberStyles.HexNumber, null, out var addr))
                    continue;

                if (HasOppoSppService(key, subName))
                {
                    var name = ReadBtDeviceName(key, subName);
                    Log.D("BT", $"Locate: 按 SPP UUID 命中 OPPO 设备 addr={addr:X12} name=\"{name}\"");
                    return (addr, name);
                }
            }

            // 回退：没找到 OPPO 名称或 UUID 的，返回第一个 BT 地址
            foreach (var subName in key.GetSubKeyNames())
            {
                if (subName.Length == 12 && ulong.TryParse(subName,
                    System.Globalization.NumberStyles.HexNumber, null, out var addr))
                {
                    Log.D("BT", $"Locate: 未命中 OPPO,回退到第一个设备 addr={addr:X12}");
                    return (addr, null);
                }
            }
            Log.D("BT", "Locate: 未找到任何可用蓝牙设备");
        }
        catch (Exception ex) { Log.Ex("BT", "Locate", ex); }
        return (0, null);
    }

    /// <summary>从注册表读取蓝牙设备名称</summary>
    private static string? ReadBtDeviceName(RegistryKey devicesKey, string subKeyName)
    {
        try
        {
            using var devKey = devicesKey.OpenSubKey(subKeyName);
            if (devKey == null) return null;

            var raw = devKey.GetValue("Name");
            var name = raw switch
            {
                byte[] bytes => System.Text.Encoding.ASCII.GetString(bytes).TrimEnd('\0'),
                string s => s,
                _ => null
            };

            if (string.IsNullOrEmpty(name))
            {
                var fn = devKey.GetValue("FriendlyName");
                name = fn switch
                {
                    string s => s,
                    byte[] bytes => System.Text.Encoding.ASCII.GetString(bytes).TrimEnd('\0'),
                    _ => null
                };
            }

            return name;
        }
        catch (Exception ex) { Log.Ex("BT", $"ReadBtDeviceName({subKeyName})", ex); return null; }
    }

    /// <summary>检查注册表中是否有 OPPO SPP 服务的 SDP 记录</summary>
    private static bool HasOppoSppService(RegistryKey devicesKey, string subKeyName)
    {
        try
        {
            // Windows 存储 SDP 记录的路径
            using var sdpKey = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters\Services\{subKeyName}");
            if (sdpKey == null) return false;

            // OPPO SPP UUID: 0000079A-D102-11E1-9B23-00025B00A5A5
            // 注册表中服务子键名称为 UUID 去横线大写: 0000079AD10211E19B2300025B00A5A5
            foreach (var serviceName in sdpKey.GetSubKeyNames())
            {
                if (serviceName.Contains("0000079A", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (serviceName.Contains("000079A", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
        catch (Exception ex) { Log.Ex("BT", $"HasOppoSppService({subKeyName})", ex); return false; }
    }

}
