using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace OppoPodsManager;

public sealed class LinuxBluetoothLocator : IDeviceLocator
{
    private static readonly Regex AnsiRegex = new(@"\x1b\[[0-9;]*m", RegexOptions.Compiled);
    private static string StripAnsi(string s) => AnsiRegex.Replace(s, "");

    public (ulong addr, string? name) Locate()
    {
        try
        {
            var result = FindViaBluetoothctl();
            if (result.addr != 0) return result;
        }
        catch (Exception ex) { Log.D("BT", $"bluetoothctl 失败: {ex.Message}"); }
        try
        {
            var result = FindViaFileSystem();
            if (result.addr != 0) return result;
        }
        catch (Exception ex) { Log.D("BT", $"文件扫描失败: {ex.Message}"); }
        Log.D("BT", "Locate: 未找到任何 OPPO 蓝牙设备");
        return (0, null);
    }

    private static (ulong addr, string? name) FindViaBluetoothctl()
    {
        using var proc = Process.Start(new ProcessStartInfo
        {
            FileName = "bluetoothctl",
            Arguments = "devices Paired",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        if (proc == null) return (0, null);
        var output = StripAnsi(proc.StandardOutput.ReadToEnd());
        proc.WaitForExit(3000);

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Log.D("BT", $"FindViaBluetoothctl: {lines.Length} lines");

        (ulong addr, string? name) fallback = (0, null);
        foreach (var line in lines)
        {
            if (!line.StartsWith("Device ")) continue;
            var parts = line.Substring(7).Split(' ', 2);
            if (parts.Length < 1) continue;
            var addr = ParseBtAddr(parts[0]);
            var name = parts.Length > 1 ? parts[1].Trim() : null;
            if (addr == 0 || !IsSupportedBrand(name)) continue;
            if (IsEarbudDevice(name)) return (addr, name);
            if (fallback.addr == 0) fallback = (addr, name);
        }
        return fallback.addr != 0 ? fallback : (0, null);
    }

    private static bool IsEarbudDevice(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var lower = name.ToLowerInvariant();
        foreach (var kw in new[] { "buds", "enco", "air", "clip", "free", "bullets" })
            if (lower.Contains(kw)) return true;
        return false;
    }

    private static (ulong addr, string? name) FindViaFileSystem()
    {
        var path = "/var/lib/bluetooth";
        if (!Directory.Exists(path)) return (0, null);
        foreach (var adapterDir in Directory.GetDirectories(path))
            foreach (var deviceDir in Directory.GetDirectories(adapterDir))
            {
                var infoFile = Path.Combine(deviceDir, "info");
                if (!File.Exists(infoFile)) continue;
                var (addr, name) = ParseInfoFile(infoFile);
                if (addr != 0 && IsSupportedBrand(name)) return (addr, name);
            }
        return (0, null);
    }

    private static (ulong addr, string? name) ParseInfoFile(string infoFile)
    {
        try
        {
            var lines = File.ReadAllLines(infoFile);
            string? name = null; ulong addr = 0;
            foreach (var line in lines)
            {
                if (line.StartsWith("Name=", StringComparison.OrdinalIgnoreCase))
                    name = line.Substring(5).Trim();
                else if (line.StartsWith("Address=", StringComparison.OrdinalIgnoreCase))
                    ulong.TryParse(line.Substring(8).Trim().Replace(":", ""),
                        System.Globalization.NumberStyles.HexNumber, null, out addr);
            }
            return (addr, name);
        }
        catch { return (0, null); }
    }

    private static ulong ParseBtAddr(string? addrStr)
    {
        if (string.IsNullOrEmpty(addrStr)) return 0;
        var hex = addrStr.Replace(":", "").Replace("-", "");
        return ulong.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var a) ? a : 0;
    }

    private static bool IsSupportedBrand(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        foreach (var brand in OppoProtocol.SupportedBrands)
            if (name.Contains(brand, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
