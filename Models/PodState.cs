using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OppoPodsManager;

/// <summary>已连接设备信息（多设备连接列表中的一项）</summary>
public class ConnectedDeviceInfo : INotifyPropertyChanged
{
    public string Address { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public int DeviceType { get; set; }         // 0=phone, 1=computer, 2=tablet, 3=watch
    public int ConnectionState { get; set; }    // 0=disconnected, 1=connecting, 2=connected
    public bool IsAudioActive { get; set; }

    private bool _isCurrentDevice;
    public bool IsCurrentDevice
    {
        get => _isCurrentDevice;
        set { if (_isCurrentDevice != value) { _isCurrentDevice = value; OnChanged(); OnChanged(nameof(DisplayName)); } }
    }

    public bool IsMainAudioDevice { get; set; }

    /// <summary>显示名称</summary>
    public string ConnectionStatus => ConnectionState switch
    {
        2 => IsCurrentDevice ? "当前设备" : "已连接",
        1 => "连接中",
        _ => "已断开"
    };

    public string DisplayName => DeviceName;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public class PodState
{
    // 后台线程写、UI 线程读，用并发字典避免竞态崩溃
    public ConcurrentDictionary<string, (int Level, bool Charging)?> Battery { get; } = new();
    public string AncMode { get; set; } = "?";
    public string EqPreset { get; set; } = "?";
    public string WearingL { get; set; } = "";
    public string WearingR { get; set; } = "";
    public bool Connected { get; set; }
    public bool SpatialSound { get; set; }
    public string SpatialMode { get; set; } = "Off";
    public bool GameMode { get; set; }
    public bool DualDevice { get; set; }

    /// <summary>多设备连接列表（由主动轮询同步）</summary>
    public List<ConnectedDeviceInfo> ConnectedDevices { get; set; } = new();

    /// <summary>多设备列表最近更新时间</summary>
    public DateTime MultiConnectListUpdatedAt { get; set; } = DateTime.MinValue;
}
