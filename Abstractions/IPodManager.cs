using System;
using System.Threading;
using System.Threading.Tasks;

namespace OppoPodsManager;

/// <summary>
/// 前后端契约（防火墙）：前端只依赖本接口与其暴露的数据结构。
/// 后端（PodManager + Transport/Protocol）可任意重构内部逻辑，
/// 只要本接口签名与下列数据结构不变，前端界面无需改动；反之前端改界面也不影响后端。
///
/// 契约数据结构（改这些字段=破坏契约，需前后端同步）：
///   PodState               运行时状态（电量/ANC/佩戴/功能开关/多设备）
///   DeviceCapabilities     设备能力（型号/EQ/ANC 映射/功能标志）
///   ConnectedDeviceInfo    多设备列表项
/// </summary>
public interface IPodManager : IDisposable
{
    /// <summary>当前设备运行时状态（电量/ANC/佩戴/功能开关/多设备列表）。</summary>
    PodState State { get; }

    /// <summary>当前设备能力（型号识别、EQ、ANC 映射、功能标志）。</summary>
    DeviceCapabilities Caps { get; }

    /// <summary>最近一次错误信息，无错误为 null。</summary>
    string? LastError { get; }

    /// <summary>是否已连接。</summary>
    bool IsConnected { get; }

    /// <summary>状态变化通知（后端线程触发，UI 需自行 marshal 到 UI 线程）。</summary>
    event Action? StateChanged;

    /// <summary>设置类命令失败/超时通知（参数为可读描述，供 UI 提示）。后端线程触发。</summary>
    event Action<string>? CommandFailed;

    /// <summary>建立连接（内部完成设备发现 + 初始化握手）。</summary>
    Task ConnectAsync();

    /// <summary>连接后的持续轮询，直到取消或断开。</summary>
    Task PollAsync(CancellationToken ct);

    /// <summary>主动断开。</summary>
    void Disconnect();

    // ===== 控制命令 =====
    void SendAnc(string mode);
    void SendSpatial(bool on);
    void SendSpatialAudio(string mode);
    void SendDualDevice(bool on);
    void SendGameMode(bool on, bool compatible = false);
    void SendEq(string name);
    void SendBattery();
    void SendMultiConnectInfo();
    void SendOperateHandheld(string targetAddress, bool connect = true);
}
