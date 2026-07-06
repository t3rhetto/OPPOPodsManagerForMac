using System;
using System.Threading.Tasks;
using System.Runtime.Versioning;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;

namespace OppoPodsManager;

/// <summary>
/// 经典蓝牙(RFCOMM/SPP)服务发现：用 WinRT DeviceInformation 枚举，
/// 返回可直接用于连接的 RfcommDeviceService。按优先级：
///   1) 按 OPPO SPP 服务 UUID 枚举（RfcommDeviceService.GetDeviceSelector）——最精确；
///   2) 按已配对经典设备的品牌名匹配，再取其上的 SPP 服务。
/// 供 WindowsRfcommLocator（取地址）与 WindowsRfcommStreamTransport（取服务连接）共用。
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal static class RfcommServiceFinder
{
    /// <summary>发现 OPPO SPP RfcommDeviceService，找不到返回 null。</summary>
    public static async Task<RfcommDeviceService?> FindServiceAsync()
    {
        var svc = await FindByServiceUuidAsync();
        if (svc != null) { Log.D("BT", "RfcommFinder: 命中(服务 UUID 枚举)"); return svc; }

        svc = await FindByPairedNameAsync();
        if (svc != null) { Log.D("BT", "RfcommFinder: 命中(配对经典名称)"); return svc; }

        return null;
    }

    /// <summary>按 OPPO SPP 服务 UUID 用 DeviceInformation 枚举，品牌名优先。</summary>
    private static async Task<RfcommDeviceService?> FindByServiceUuidAsync()
    {
        try
        {
            var serviceId = RfcommServiceId.FromUuid(OppoProtocol.OppoSppUuid);
            string selector = RfcommDeviceService.GetDeviceSelector(serviceId);
            var devices = await DeviceInformation.FindAllAsync(selector);
            Log.D("BT", $"RfcommFinder: 服务 UUID 枚举到 {devices.Count} 个候选");
            if (devices.Count == 0) return null;

            foreach (var di in devices)
                if (IsSupportedBrand(di.Name))
                {
                    var svc = await TryOpenAsync(di.Id);
                    if (svc != null) return svc;
                }
            foreach (var di in devices)
            {
                var svc = await TryOpenAsync(di.Id);
                if (svc != null) return svc;
            }
        }
        catch (Exception ex) { Log.Ex("BT", "RfcommFinder.FindByServiceUuidAsync", ex); }
        return null;
    }

    /// <summary>枚举已配对经典设备，按品牌名匹配，再取其上的 OPPO SPP 服务。</summary>
    private static async Task<RfcommDeviceService?> FindByPairedNameAsync()
    {
        try
        {
            string selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
            var devices = await DeviceInformation.FindAllAsync(selector);
            Log.D("BT", $"RfcommFinder: 已配对经典枚举到 {devices.Count} 个");
            var serviceId = RfcommServiceId.FromUuid(OppoProtocol.OppoSppUuid);

            foreach (var di in devices)
            {
                if (!IsSupportedBrand(di.Name)) continue;
                var dev = await BluetoothDevice.FromIdAsync(di.Id);
                if (dev == null) continue;

                var svcResult = await dev.GetRfcommServicesForIdAsync(serviceId, BluetoothCacheMode.Uncached);
                if (svcResult?.Services != null && svcResult.Services.Count > 0)
                    return svcResult.Services[0];
            }
        }
        catch (Exception ex) { Log.Ex("BT", "RfcommFinder.FindByPairedNameAsync", ex); }
        return null;
    }

    private static async Task<RfcommDeviceService?> TryOpenAsync(string deviceId)
    {
        try { return await RfcommDeviceService.FromIdAsync(deviceId); }
        catch (Exception ex) { Log.Ex("BT", "RfcommFinder.TryOpenAsync", ex); return null; }
    }

    private static bool IsSupportedBrand(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        foreach (var brand in OppoProtocol.SupportedBrands)
            if (name.Contains(brand, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
