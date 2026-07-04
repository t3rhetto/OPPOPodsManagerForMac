using System;

namespace OppoPodsManager;

/// <summary>
/// 按运行平台选择硬件传输实现。核心/编排层通过本工厂拿 IPodTransport，
/// 不直接依赖任何平台专属类（如 SppTransport 只在 Windows 下可用）。
/// 未来加 Linux/macOS：在此按 OperatingSystem.IsLinux() 等分支返回对应实现。
/// </summary>
public static class TransportFactory
{
    /// <summary>可选注入点：测试或自定义实现时设置；为 null 时按平台自动选择。</summary>
    public static Func<IPodTransport>? Override { get; set; }

    public static IPodTransport Create()
    {
        if (Override != null)
        {
            Log.D("FACTORY", "Create: 使用注入的 Override 传输实现");
            return Override();
        }

#if WINDOWS
        if (OperatingSystem.IsWindows())
        {
            Log.D("FACTORY", "Create: Windows 平台 -> SppTransport");
            return new SppTransport();
        }
#endif

        Log.D("FACTORY", "Create: 当前平台无传输实现,抛出 PlatformNotSupportedException");
        throw new PlatformNotSupportedException(
            "当前平台暂无硬件传输实现。请为该平台实现 IPodTransport（如 Linux BlueZ / macOS IOBluetooth），并在 TransportFactory 中按平台分支返回。");
    }
}
