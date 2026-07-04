using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OppoPodsManager;

/// <summary>
/// 编排层：在 IPodTransport 之上组织命令收发与状态解析。
/// 不关心底层是经典 SPP 还是 BLE GATT——只依赖 IPodTransport。
/// 对外公开 API 与旧版保持一致（MainWindow 无需改动）。
/// </summary>
public class RfcommService : IPodManager
{
    private readonly IPodTransport _transport;
    private bool _disposed;

    public PodState State { get; } = new();
    public DeviceCapabilities Caps { get; private set; } = DeviceCapabilities.Detect(null);

    public event Action? StateChanged;
    public string? LastError => _transport.LastError;
    public bool IsConnected => State.Connected;

    public RfcommService() : this(TransportFactory.Create()) { }

    public RfcommService(IPodTransport transport)
    {
        _transport = transport;
        _transport.FrameReceived += DispatchFrame;
        _transport.Disconnected += OnDisconnected;
    }

    private void OnDisconnected()
    {
        Log.D("RFCOMM", "OnDisconnected: 传输层报告断开");
        State.Connected = false;
        StateChanged?.Invoke();
    }

    private void DispatchFrame(PodFrame frame)
    {
        var p = frame.Payload;
        int len = p.Length;
        switch (frame.Cmd)
        {
            case OppoProtocol.CmdBatteryResp: ParseBattery(p, 0, len); break;
            case OppoProtocol.CmdAncResp: ParseAnc(p, 0, len); break;
            case OppoProtocol.CmdActiveReport: ParseActiveReport(p, 0, len); break;
            case OppoProtocol.CmdEqResp:
            case OppoProtocol.CmdEqNotify: ParseEq(p, 0, len); break;
            case OppoProtocol.CmdBatchQueryResp: ParseBatchStatus(p, 0, len); break;
            case OppoProtocol.CmdMultiConnectResp: ParseMultiConnect(p, 0, len); break;
            case OppoProtocol.CmdProductIdResp: ParseProductId(p, 0, len); break;
            default:
                Log.D("RFCOMM", $"DispatchFrame: 未处理 cmd=0x{frame.Cmd:X4} len={len}");
                break;
        }
    }

    public async Task ConnectAsync()
    {
        await Task.Run(() =>
        {
            Log.D("RFCOMM", "ConnectAsync: 开始连接");
            if (!_transport.Connect())
            {
                Log.D("RFCOMM", "ConnectAsync: 传输层连接失败 -> " + (_transport.LastError ?? "unknown"));
                return;
            }

            Caps = DeviceCapabilities.Detect(_transport.DeviceName);  // 名称预判（回退）
            State.Connected = true;
            Log.D("RFCOMM", $"ConnectAsync: 连接成功,开始握手序列 (名称预判 Caps={Caps.ModelName})");

            // 初始化握手：优先查 productId（官方精确识别）+ 批量查询 + 电池 + ANC + EQ + 订阅通知 + 多设备
            _transport.Send(OppoProtocol.CmdQueryProductId, OppoProtocol.PayEmpty);
            Thread.Sleep(80);
            _transport.Send(OppoProtocol.CmdBatchQuery, OppoProtocol.PayBatchQuery);
            Thread.Sleep(80);
            _transport.Send(OppoProtocol.CmdBattery, OppoProtocol.PayEmpty);
            Thread.Sleep(80);
            _transport.Send(OppoProtocol.CmdQueryAnc, OppoProtocol.PayQueryAnc);
            Thread.Sleep(80);
            _transport.Send(OppoProtocol.CmdQueryEq, OppoProtocol.PayEmpty);
            Thread.Sleep(80);
            _transport.Send(OppoProtocol.CmdRegisterNotify, OppoProtocol.PayRegisterNotify);
            Thread.Sleep(80);
            _transport.Send(OppoProtocol.CmdRegisterNotify, OppoProtocol.PayRegisterWear);
            Thread.Sleep(80);
            _transport.Send(OppoProtocol.CmdMultiConnectInfo, OppoProtocol.PayEmpty);
            Thread.Sleep(500);
            Log.D("RFCOMM", "ConnectAsync: 握手命令已发完,等待首批响应");
            _transport.Poll(3000);
            Log.D("RFCOMM", "ConnectAsync: 初始化完成");
        });
    }

    public void SendMultiConnectInfo()
    {
        Log.D("RFCOMM", "SendMultiConnectInfo");
        _transport.Send(OppoProtocol.CmdMultiConnectInfo, OppoProtocol.PayEmpty);
    }

    public void SendOperateHandheld(string targetAddress, bool connect = true)
    {
        Log.D("RFCOMM", $"SendOperateHandheld addr={targetAddress} connect={connect}");
        _transport.Send(OppoProtocol.CmdOperateHandheld, OppoProtocol.OperateHandheldPayload(targetAddress, connect));
    }

    public void SendAnc(string mode)
    {
        Log.D("RFCOMM", $"SendAnc mode={mode}");
        if (Caps.AncNameToIndex.TryGetValue(mode, out var idx))
        {
            _transport.Send(OppoProtocol.CmdAnc, OppoProtocol.AncPayloadByIndex(idx));
            return;
        }
        var m = Caps.IsLegacyAnc ? OppoProtocol.LegacyAncSwap(mode) : mode;
        _transport.Send(OppoProtocol.CmdAnc, OppoProtocol.AncPayloadByName(m));
    }

    public void SendBattery()
    {
        Log.D("RFCOMM", "SendBattery");
        _transport.Send(OppoProtocol.CmdBattery, OppoProtocol.PayEmpty);
    }

    public void SendSpatial(bool on)
    {
        Log.D("RFCOMM", $"SendSpatial on={on}");
        _transport.Send(OppoProtocol.CmdSetFeature, OppoProtocol.FeaturePayload(OppoProtocol.FeatureSpatial, on));
    }

    public void SendSpatialAudio(string mode)
    {
        Log.D("RFCOMM", $"SendSpatialAudio mode={mode}");
        _transport.Send(OppoProtocol.CmdSpatialAudio, OppoProtocol.SpatialPayload(mode));
    }

    public void SendDualDevice(bool on)
    {
        Log.D("RFCOMM", $"SendDualDevice on={on}");
        _transport.Send(OppoProtocol.CmdSetFeature, OppoProtocol.FeaturePayload(OppoProtocol.FeatureDualDevice, on));
    }

    public void SendGameMode(bool on, bool compatible = false)
    {
        Log.D("RFCOMM", $"SendGameMode on={on} compatible={compatible}");
        _transport.Send(OppoProtocol.CmdSetFeature, OppoProtocol.FeaturePayload(OppoProtocol.FeatureGameMain, on));
        if (compatible)
            _transport.Send(OppoProtocol.CmdSetFeature, OppoProtocol.FeaturePayload(OppoProtocol.FeatureGameLL, on));
    }

    public void SendEq(string name)
    {
        if (Caps.EqPresets.TryGetValue(name, out var id))
        {
            Log.D("RFCOMM", $"SendEq name={name} id={id}");
            _transport.Send(OppoProtocol.CmdSetEq, new byte[] { id });
        }
        else
        {
            Log.D("RFCOMM", $"SendEq: 未知 EQ 预设 \"{name}\",已忽略");
        }
    }


    /// <summary>
    /// 官方设备识别：0x8103 响应里拿到 productId 后，用它精确重定能力。
    /// 这是优先路径；名称匹配（ConnectAsync 里的 Detect）只作连接前预判/回退。
    /// </summary>
    private void ParseProductId(byte[] pkt, int start, int len)
    {
        var payload = new byte[len];
        Array.Copy(pkt, start, payload, 0, len);
        var productId = OppoProtocol.ParseProductId(payload);
        if (productId == null) return;

        Log.D("RFCOMM", $"ParseProductId: productId={productId}");
        var byId = DeviceCapabilities.DetectById(productId, _transport.DeviceName);
        if (byId != null)
        {
            Log.D("RFCOMM", $"ParseProductId: 精确识别为 {byId.ModelName}");
            Caps = byId;
            StateChanged?.Invoke();
        }
    }

    private void ParseBattery(byte[] pkt, int start, int len)
    {
        for (int i = 0; i + 1 < len; i += 2)
        {
            int idx = pkt[start + i];
            int raw = pkt[start + i + 1];
            int level = raw & 0x7F;
            bool charging = (raw & 0x80) != 0;
            var key = idx switch { 1 => "L", 2 => "R", 3 => "C", _ => null };
            if (key != null) State.Battery[key] = (level, charging);
        }
        StateChanged?.Invoke();
    }

    private void ParseAnc(byte[] pkt, int start, int len)
    {
        for (int i = 0; i + 3 < len; i++)
        {
            if (pkt[start + i] == 0x01 && pkt[start + i + 1] == 0x01)
            {
                byte v1 = pkt[start + i + 2], v2 = pkt[start + i + 3];

                // 优先：按型号 AncIndexToName，把位图里置位的 bit -> protocolIndex -> 模式名
                if (Caps.AncIndexToName.Count > 0)
                {
                    int value = v1 + v2 * 256;
                    int bit = 1;
                    for (int idx = 0; idx < 16; idx++)
                    {
                        if ((value & bit) != 0 && Caps.AncIndexToName.TryGetValue((byte)idx, out var name))
                        {
                            State.AncMode = name;
                            break;
                        }
                        bit *= 2;
                    }
                }
                // 回退：静态表（含旧版值交换）
                else if (OppoProtocol.AncValues.TryGetValue((v1, v2), out var mode))
                {
                    State.AncMode = Caps.IsLegacyAnc ? OppoProtocol.LegacyAncSwap(mode) : mode;
                }
            }
        }
        StateChanged?.Invoke();
    }

    private void ParseActiveReport(byte[] pkt, int start, int len)
    {
        if (len < 2) return;
        int reportType = pkt[start];
        Log.D("RFCOMM", $"ParseActiveReport: type=0x{reportType:X2} len={len}");
        if (reportType == 0x01)
        {
            int count = pkt[start + 1];
            for (int j = 0; j < count && start + 2 + j * 2 + 1 < start + len; j++)
            {
                int idx = pkt[start + 2 + j * 2];
                int raw = pkt[start + 2 + j * 2 + 1];
                int level = raw & 0x7F;
                bool charging = (raw & 0x80) != 0;
                var key = idx switch { 1 => "L", 2 => "R", 3 => "C", _ => null };
                if (key != null) State.Battery[key] = (level, charging);
            }
        }
        else if (reportType == 0x02)
        {
            ParseWearingData(pkt, start, len);
        }
        else if (reportType == 0x06)  // 实际是多设备信息，非佩戴数据
        {
            // 不要用 wearing 格式解析，只是记录
        }
        StateChanged?.Invoke();
    }

    private void ParseWearingData(byte[] pkt, int start, int len)
    {
        if (len < 3) return;
        int count = pkt[start + 1];
        for (int j = 0; j < count && start + 2 + j * 2 + 1 < start + len; j++)
        {
            int comp = pkt[start + 2 + j * 2];
            int st = pkt[start + 2 + j * 2 + 1];
            string status = st switch
            {
                0 => "已断连", 4 => "入盒", 5 => "摘下", 7 => "佩戴", _ => "?" + st
            };
            if (comp == 1) State.WearingL = status;
            else if (comp == 2) State.WearingR = status;
        }
        Log.D("RFCOMM", $"ParseWearingData: L='{State.WearingL}' R='{State.WearingR}'");
    }

    private void ParseEq(byte[] pkt, int start, int len)
    {
        if (len >= 2)
            State.EqPreset = Caps.EqNames.GetValueOrDefault(pkt[start + 1], "?");
        StateChanged?.Invoke();
    }

    private void ParseBatchStatus(byte[] pkt, int start, int len)
    {
        for (int i = 0; i + 1 < len; i += 2)
        {
            byte feature = pkt[start + i];
            byte value = pkt[start + i + 1];
            if (feature == OppoProtocol.FeatureGameMain)
                State.GameMode = value != 0;
            else if (feature == OppoProtocol.FeatureDualDevice)
                State.DualDevice = value != 0;
            else if (feature == OppoProtocol.FeatureSpatial)
                State.SpatialSound = value != 0;
        }
        StateChanged?.Invoke();
    }

    /// <summary>解析多设备连接列表响应 (cmd 0x8112)</summary>
    private void ParseMultiConnect(byte[] pkt, int start, int len)
    {
        try
        {
            Log.D("RFCOMM", "ParseMultiConnect: len=" + len + ", full=" + BitConverter.ToString(pkt, start, Math.Min(len, 48)));
            var devices = new List<ConnectedDeviceInfo>();
            if (len < 2) return;

            int count = pkt[start + 1];
            if (count <= 0 || count > 8)
            {
                Log.D("RFCOMM", "ParseMultiConnect: invalid count=" + count);
                return;
            }

            int pos = start + 2;
            for (int i = 0; i < count && pos + 8 < start + len; i++)
            {
                // Format: [addr(6)] [state(1)] [type(1)] [reserved(1)] [nameLen(1)] [name(nameLen)]
                var addr = string.Join(":", Enumerable.Range(0, 6).Select(j => pkt[pos + j].ToString("X2")));
                pos += 6;

                int stateFlags = pkt[pos++];   // bitmask: bit3=current, bit2=mainAudio, bit1=audioActive, bit0=connected?
                int connState = pkt[pos++];    // 0=disconnected, 2=connected
                int reserved = pkt[pos++];
                int nameLen = pkt[pos++];

                if (nameLen < 0 || pos + nameLen > start + len)
                {
                    Log.D("RFCOMM", "ParseMultiConnect: device[" + i + "] invalid nameLen=" + nameLen);
                    break;
                }

                string deviceName = nameLen > 0
                    ? System.Text.Encoding.UTF8.GetString(pkt, pos, nameLen).TrimEnd("\0".ToCharArray())
                    : "Device " + addr.Substring(Math.Max(0, addr.Length - 5));
                pos += Math.Max(nameLen, 0);

                bool isCurrent = (stateFlags & 0x08) != 0;
                bool isAudioActive = (stateFlags & 0x02) != 0;
                bool isMainAudio = (stateFlags & 0x04) != 0;

                devices.Add(new ConnectedDeviceInfo
                {
                    Address = addr,
                    DeviceName = deviceName,
                    ConnectionState = connState,
                    DeviceType = 0,
                    IsCurrentDevice = isCurrent,
                    IsAudioActive = isAudioActive,
                    IsMainAudioDevice = isMainAudio,
                });
                Log.D("RFCOMM", "ParseMultiConnect: device[" + i + "] addr=" + addr + ", name=\"" + deviceName + "\", connState=" + connState + ", flags=0x" + stateFlags.ToString("X2") + ", cur=" + isCurrent);
            }

            if (devices.Count > 0)
            {
                // 当前设备排最前
                devices = devices.OrderByDescending(d => d.IsCurrentDevice).ThenBy(d => d.DeviceName).ToList();
                State.ConnectedDevices = devices;
                State.MultiConnectListUpdatedAt = DateTime.Now;
                Log.D("RFCOMM", "ParseMultiConnect: 列表更新 " + devices.Count + " 个设备: " + string.Join(", ", devices.Select(d => d.DeviceName)));
                StateChanged?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Log.Ex("RFCOMM", "ParseMultiConnect", ex);
        }
    }

    public async Task PollAsync(CancellationToken ct)
    {
        await Task.Run(() =>
        {
            int tick = 0;
            int intervalMs = 2000;
            while (!ct.IsCancellationRequested && State.Connected)
            {
                try
                {
                    _transport.Send(OppoProtocol.CmdBattery, OppoProtocol.PayEmpty);
                    Thread.Sleep(100);
                    _transport.Send(OppoProtocol.CmdQueryAnc, OppoProtocol.PayQueryAnc);
                    Thread.Sleep(100);
                    _transport.Poll(500);
                    tick++;

                    if (tick % 6 == 0)
                    {
                        _transport.Send(OppoProtocol.CmdQueryEq, OppoProtocol.PayEmpty);
                        Thread.Sleep(100);
                        _transport.Poll(400);
                    }

                    if (tick % 4 == 0)
                    {
                        _transport.Send(OppoProtocol.CmdBatchQuery, OppoProtocol.PayBatchQuery);
                        Thread.Sleep(100);
                        _transport.Poll(400);

                        _transport.Send(OppoProtocol.CmdRegisterNotify, OppoProtocol.PayRegisterNotify);
                        Thread.Sleep(100);
                        _transport.Send(OppoProtocol.CmdRegisterNotify, OppoProtocol.PayRegisterWear);
                        Thread.Sleep(100);
                        _transport.Poll(400);

                        _transport.Send(OppoProtocol.CmdMultiConnectInfo, OppoProtocol.PayEmpty);
                        Thread.Sleep(100);
                        _transport.Poll(400);
                    }

                    if (tick == 10) intervalMs = 5000;
                    Thread.Sleep(intervalMs);
                }
                catch (Exception ex)
                {
                    Log.Ex("RFCOMM", "PollAsync 循环中断", ex);
                    break;
                }
            }
            Log.D("RFCOMM", $"PollAsync: 退出轮询循环 (cancelled={ct.IsCancellationRequested}, connected={State.Connected})");
            State.Connected = false;
            _transport.Close();
            StateChanged?.Invoke();
        }, ct);
    }

    public void Disconnect()
    {
        State.Connected = false;
        _transport.Close();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Disconnect();
        _transport.Dispose();
        _disposed = true;
    }
}
