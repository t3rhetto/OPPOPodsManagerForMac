using System;
using System.Threading.Tasks;
using System.Runtime.Versioning;

namespace OppoPodsManager;

/// <summary>
/// 官方形式的经典蓝牙(RFCOMM/SPP)设备定位：用 RfcommServiceFinder 做 WinRT 枚举，
/// 从命中的服务取 48 位蓝牙地址与名称，供 SppTransport 的 Winsock AF_BTH 连接使用。
/// WinRT 枚举未命中时回退注册表扫描（WindowsBluetoothLocator）。
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class WindowsRfcommLocator : IDeviceLocator
{
    private const int TimeoutMs = 6000;
    private readonly IDeviceLocator _registryFallback;

    public WindowsRfcommLocator() : this(new WindowsBluetoothLocator()) { }
    public WindowsRfcommLocator(IDeviceLocator registryFallback) { _registryFallback = registryFallback; }

    public (ulong addr, string? name) Locate()
    {
        var hit = RunSync(FindViaWinRtAsync, TimeoutMs);
        if (hit.addr != 0)
        {
            Log.D("BT", $"Locate: 命中(WinRT 枚举) addr={hit.addr:X12} name=\"{hit.name}\"");
            return hit;
        }

        Log.D("BT", "Locate: WinRT 枚举未命中，回退注册表扫描");
        return _registryFallback.Locate();
    }

    private static async Task<(ulong addr, string? name)> FindViaWinRtAsync()
    {
        var svc = await RfcommServiceFinder.FindServiceAsync();
        var dev = svc?.Device;
        if (dev != null && dev.BluetoothAddress != 0)
            return (dev.BluetoothAddress, dev.Name);
        return (0, null);
    }

    private static (ulong addr, string? name) RunSync(Func<Task<(ulong addr, string? name)>> op, int timeoutMs)
    {
        try
        {
            var task = op();
            return task.Wait(timeoutMs) ? task.Result : (0, null);
        }
        catch (Exception ex) { Log.Ex("BT", "RunSync", ex); return (0, null); }
    }
}
