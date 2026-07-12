using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace OppoPodsManager;

public sealed class MacBluetoothLocator : IDeviceLocator
{
    public (ulong addr, string? name) Locate()
    {
        try
        {
            var devices = GetPairedDevices();
            // 优先找耳机类设备
            var earbuds = devices.Where(d => IsEarbudDevice(d.name)).ToList();
            var candidates = earbuds.Count > 0 ? earbuds : devices;

            foreach (var d in candidates)
                if (IsSupportedBrand(d.name)) return d;

            Log.D("BT", "MacLocate: no OPPO device found");
            return (0, null);
        }
        catch (Exception ex)
        {
            Log.Ex("BT", "MacLocate", ex);
            return (0, null);
        }
    }

    public IReadOnlyList<(ulong addr, string name)> LocateAllConnected()
    {
        var result = new List<(ulong addr, string name)>();
        try
        {
            var devices = GetPairedDevices();
            foreach (var d in devices)
                if (d.name != null && IsSupportedBrand(d.name))
                    result.Add((d.addr, d.name));
        }
        catch (Exception ex) { Log.Ex("BT", "MacLocateAll", ex); }
        return result;
    }

    private static List<(ulong addr, string? name)> GetPairedDevices()
    {
        var devices = new List<(ulong addr, string? name)>();

        // system_profiler SPBluetoothDataType -json 输出配对设备
        var output = RunProcess("system_profiler", "SPBluetoothDataType -json");
        if (string.IsNullOrEmpty(output)) return devices;

        // 简单解析 JSON 中的 device 字段
        // 格式: "device_address" : "xx-xx-xx-xx-xx-xx"
        var addrRegex = new Regex(@"""([0-9a-fA-F]{2}(?:-[0-9a-fA-F]{2}){5})""", RegexOptions.Compiled);
        var nameRegex = new Regex(@"""name""\s*:\s*""([^""]+)""", RegexOptions.Compiled);

        var lines = output.Split('\n');
        string? currentName = null;
        for (int i = 0; i < lines.Length; i++)
        {
            var nameMatch = nameRegex.Match(lines[i]);
            if (nameMatch.Success) currentName = nameMatch.Groups[1].Value;

            var addrMatch = addrRegex.Match(lines[i]);
            if (addrMatch.Success && currentName != null)
            {
                var addr = ParseBtAddr(addrMatch.Groups[1].Value);
                if (addr != 0)
                {
                    devices.Add((addr, currentName));
                    Log.D("BT", $"MacLocate: found \"{currentName}\" addr=0x{addr:X12}");
                }
                currentName = null;
            }
        }

        return devices;
    }

    private static ulong ParseBtAddr(string addrStr)
    {
        var hex = addrStr.Replace("-", "").Replace(":", "");
        return ulong.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var a) ? a : 0;
    }

    private static string? RunProcess(string fileName, string arguments)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (proc == null) return null;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(10000);
            return output;
        }
        catch { return null; }
    }

    private static bool IsEarbudDevice(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var lower = name.ToLowerInvariant();
        foreach (var kw in new[] { "buds", "enco", "air", "clip", "free", "bullets" })
            if (lower.Contains(kw)) return true;
        return false;
    }

    private static bool IsSupportedBrand(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        foreach (var brand in OppoProtocol.SupportedBrands)
            if (name.Contains(brand, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
