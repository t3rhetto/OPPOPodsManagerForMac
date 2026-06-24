using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace OppoPodsWPF;

public class RfcommService : IDisposable
{
    private IntPtr _socket = IntPtr.Zero;
    private bool _disposed;
    private readonly object _lock = new();  // socket 线程安全锁
    private byte[] _recvBuf = new byte[512];
    public PodState State { get; } = new();
    public DeviceCapabilities Caps { get; private set; } = DeviceCapabilities.Detect(null);

    public event Action? StateChanged;
    public string? LastError { get; private set; }

    public bool IsConnected => State.Connected;

    // WinSock2 imports
    [DllImport("ws2_32.dll", SetLastError = true)]
    private static extern int WSAStartup(ushort version, IntPtr data);

    [DllImport("ws2_32.dll", SetLastError = true)]
    private static extern int WSACleanup();

    [DllImport("ws2_32.dll", SetLastError = true)]
    private static extern IntPtr socket(int af, int type, int protocol);

    [DllImport("ws2_32.dll", SetLastError = true)]
    private static extern int connect(IntPtr s, IntPtr addr, int addrLen);

    [DllImport("ws2_32.dll", SetLastError = true)]
    private static extern int send(IntPtr s, byte[] buf, int len, int flags);

    [DllImport("ws2_32.dll", SetLastError = true)]
    private static extern int recv(IntPtr s, byte[] buf, int len, int flags);

    [DllImport("ws2_32.dll", SetLastError = true)]
    private static extern int closesocket(IntPtr s);

    private const int AF_BTH = 32;
    private const int SOCK_STREAM = 1;
    private const int BTHPROTO_RFCOMM = 3;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct SockAddrBth
    {
        public ushort family;
        public ulong btAddr;
        public Guid serviceClassId;
        public uint port;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WSAData
    {
        public ushort version;
        public ushort highVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string description;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 129)] public string systemStatus;
        public ushort maxSockets;
        public ushort maxUdpDg;
        public IntPtr vendorInfo;
    }

    public async Task ConnectAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                var wsaPtr = Marshal.AllocHGlobal(400);
                WSAStartup(0x0202, wsaPtr);
                Marshal.FreeHGlobal(wsaPtr);

                var (addr, name) = ReadBtDevice();
                if (addr == 0)
                {
                    LastError = "未找到已配对的 OPPO 设备";
                    return;
                }

                Caps = DeviceCapabilities.Detect(name);

                _socket = socket(AF_BTH, SOCK_STREAM, BTHPROTO_RFCOMM);
                if (_socket == IntPtr.Zero)
                {
                    LastError = "无法创建 BTH socket";
                    return;
                }

                var sa = new SockAddrBth
                {
                    family = AF_BTH,
                    btAddr = addr,
                    port = 15
                };
                int saSize = Marshal.SizeOf(sa);
                var saPtr = Marshal.AllocHGlobal(saSize);
                Marshal.StructureToPtr(sa, saPtr, false);

                if (connect(_socket, saPtr, saSize) != 0)
                {
                    Marshal.FreeHGlobal(saPtr);
                    closesocket(_socket);
                    _socket = IntPtr.Zero;
                    LastError = "无法连接耳机";
                    return;
                }
                Marshal.FreeHGlobal(saPtr);

                State.Connected = true;
                LastError = null;

                // 五连发 + 一次长读：先快速发完所有查询，等 500ms 后一口气读回全部响应
                Send(OppoProtocol.PktBatchQuery);
                Thread.Sleep(80);
                Send(OppoProtocol.PktBattery);
                Thread.Sleep(80);
                Send(OppoProtocol.PktQueryAnc);
                Thread.Sleep(80);
                Send(OppoProtocol.PktQueryEq);
                Thread.Sleep(80);
                Send(OppoProtocol.PktRegisterNotify);
                Thread.Sleep(500);
                ReadResponses(3000);
            }
            catch (Exception e)
            {
                LastError = e.Message;
            }
        });
    }

    private (ulong addr, string? name) ReadBtDevice()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters\Devices");
            if (key == null) return (0, null);

            foreach (var subName in key.GetSubKeyNames())
            {
                if (subName.Length != 12 || !ulong.TryParse(subName,
                    System.Globalization.NumberStyles.HexNumber, null, out var addr))
                    continue;

                // 尝试从蓝牙枚举注册表读设备名
                string? name = null;
                try
                {
                    using var devKey = key.OpenSubKey(subName);
                    if (devKey != null)
                    {
                        // 注册表 Name 值可能是 byte[] (ASCII) 或 string
                        var raw = devKey.GetValue("Name");
                        name = raw switch
                        {
                            byte[] bytes => System.Text.Encoding.ASCII.GetString(bytes).TrimEnd('\0'),
                            string s => s,
                            _ => null
                        };

                        // 也试 FriendlyName
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
                    }
                }
                catch { }

                // OPPO 设备优先匹配
                if (!string.IsNullOrEmpty(name) && name.Contains("OPPO", StringComparison.OrdinalIgnoreCase))
                    return (addr, name);
            }

            // 回退：没找到 OPPO 名称的，返回第一个 BT 地址（兼容所有设备）
            foreach (var subName in key.GetSubKeyNames())
            {
                if (subName.Length == 12 && ulong.TryParse(subName,
                    System.Globalization.NumberStyles.HexNumber, null, out var addr))
                    return (addr, null);
            }
        }
        catch { }
        return (0, null);
    }

    private void ReadResponses(int timeoutMs)
    {
        if (_socket == IntPtr.Zero) return;
        var endTime = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        var framer = new List<byte>();

        while (DateTime.UtcNow < endTime)
        {
            int got = recv(_socket, _recvBuf, _recvBuf.Length, 0);
            if (got <= 0) throw new Exception("连接已断开");  // 对端关闭或 socket 错误

            for (int i = 0; i < got; i++)
            {
                framer.Add(_recvBuf[i]);
                // Try to parse frames as they accumulate
                while (TryExtractFrame(framer, out var frame))
                    ParseFrame(frame);
            }
        }
    }

    private bool TryExtractFrame(List<byte> buffer, out byte[] frame)
    {
        frame = Array.Empty<byte>();
        int start = buffer.IndexOf(0xAA);
        if (start < 0) { buffer.Clear(); return false; }
        if (start > 0) buffer.RemoveRange(0, start);
        if (buffer.Count < 2) return false;

        int totalLen = buffer[1];
        int frameLen = totalLen + 2;
        if (totalLen < 7 || frameLen > 512) { buffer.RemoveAt(0); return false; }
        if (buffer.Count < frameLen) return false;

        frame = buffer.GetRange(0, frameLen).ToArray();
        buffer.RemoveRange(0, frameLen);
        return true;
    }

    private void ParseFrame(byte[] pkt)
    {
        if (pkt.Length < 9) return;
        ushort cmd = (ushort)(pkt[4] | (pkt[5] << 8));
        int payLen = pkt[7] | (pkt[8] << 8);
        int payloadStart = 9;

        switch (cmd)
        {
            case OppoProtocol.CmdBatteryResp:
                ParseBattery(pkt, payloadStart, payLen);
                break;
            case OppoProtocol.CmdAncResp:
                ParseAnc(pkt, payloadStart, payLen);
                break;
            case OppoProtocol.CmdActiveReport:
                ParseActiveReport(pkt, payloadStart, payLen);
                break;
            case OppoProtocol.CmdEqResp:
            case OppoProtocol.CmdEqNotify:
                ParseEq(pkt, payloadStart, payLen);
                break;
            case OppoProtocol.CmdBatchQueryResp:
                ParseBatchStatus(pkt, payloadStart, payLen);
                break;
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
            if (OppoProtocol.AncValues.TryGetValue((v1, v2), out var mode))
            {
                // Air2 Pro 旧版 ANC 反向交换（解析时换回来）
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
            int count = pkt[start + 1];
            for (int j = 0; j < count && start + 2 + j * 2 + 1 < start + len; j++)
            {
                int comp = pkt[start + 2 + j * 2];
                int st = pkt[start + 2 + j * 2 + 1];
                string status = st switch
                {
                    0 => "已断连", 4 => "入盒", 5 => "摘下", 7 => "佩戴", _ => "?"
                };
                if (comp == 1) State.WearingL = status;
                else if (comp == 2) State.WearingR = status;
            }
        }
        StateChanged?.Invoke();
    }

    private void ParseEq(byte[] pkt, int start, int len)
    {
        if (len >= 2)
            State.EqPreset = Caps.EqNames.GetValueOrDefault(pkt[start + 1], "?");
        StateChanged?.Invoke();
    }

    private void ParseBatchStatus(byte[] pkt, int start, int len)
    {
        // 0x810D 解析游戏模式、双设备、空间音效状态
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

    public void SendAnc(string mode)
    {
        // Air2 Pro 旧版 ANC 值交换
        if (Caps.IsLegacyAnc)
            mode = OppoProtocol.LegacyAncSwap(mode);
        Send(OppoProtocol.PktAncMode(mode));
    }
    public void SendBattery() => Send(OppoProtocol.PktBattery);
    public void SendSpatial(bool on) => Send(OppoProtocol.BuildFeaturePacket(OppoProtocol.FeatureSpatial, on));
    public void SendSpatialAudio(string mode) => Send(OppoProtocol.PktSpatialAudio(mode));
    public void SendDualDevice(bool on) => Send(OppoProtocol.BuildFeaturePacket(OppoProtocol.FeatureDualDevice, on));
    public void SendGameMode(bool on, bool compatible = false)
    {
        // 标准: 只发 28; 兼容: 发 28 + 06 (顺序执行, 无阻塞延迟)
        Send(OppoProtocol.BuildFeaturePacket(OppoProtocol.FeatureGameMain, on));
        if (compatible)
            Send(OppoProtocol.BuildFeaturePacket(OppoProtocol.FeatureGameLL, on));
    }
    public void SendEq(string name)
    {
        if (Caps.EqPresets.TryGetValue(name, out var id))
            Send(OppoProtocol.BuildPacket(OppoProtocol.CmdSetEq, new[] { id }));
    }

    public async Task PollAsync(CancellationToken ct)
    {
        await Task.Run(() =>
        {
            int tick = 0;
            int intervalMs = 2000;  // 初始 2 秒快速同步
            while (!ct.IsCancellationRequested && State.Connected)
            {
                try
                {
                    // 电池和 ANC 每轮都查（物理按键切换需快速响应）
                    SendBattery();
                    Thread.Sleep(100);
                    Send(OppoProtocol.PktQueryAnc);
                    Thread.Sleep(100);
                    ReadResponses(500);
                    tick++;

                    if (tick % 6 == 0)  // 每 6 轮查 EQ
                    {
                        Send(OppoProtocol.PktQueryEq);
                        Thread.Sleep(100);
                        ReadResponses(400);
                    }

                    if (tick % 4 == 0)  // 每 4 轮查功能状态（游戏模式/双设备/空间音效）
                    {
                        Send(OppoProtocol.PktBatchQuery);
                        Thread.Sleep(100);
                        ReadResponses(400);
                    }

                    // 前 10 轮 2s，之后放缓到 5s
                    if (tick == 10) intervalMs = 5000;
                    Thread.Sleep(intervalMs);
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    break;
                }
            }
            State.Connected = false;
            StateChanged?.Invoke();
        }, ct);
    }

    private void Send(byte[] pkt)
    {
        lock (_lock)
        {
            if (_socket != IntPtr.Zero)
                send(_socket, pkt, pkt.Length, 0);
        }
    }

    public void Disconnect()
    {
        lock (_lock)
        {
            State.Connected = false;
            if (_socket != IntPtr.Zero)
            {
                closesocket(_socket);
                _socket = IntPtr.Zero;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Disconnect();
        WSACleanup();
        _disposed = true;
    }
}
