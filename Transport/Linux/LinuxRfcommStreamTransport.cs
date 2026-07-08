using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace OppoPodsManager;

public sealed class LinuxRfcommStreamTransport : IPodTransport
{
    private const int AF_BLUETOOTH = 31;
    private const int SOCK_STREAM = 1;
    private const int BTPROTO_RFCOMM = 3;
    private const int SOL_SOCKET = 1;
    private const int SO_RCVTIMEO = 20;
    private const int SO_SNDTIMEO = 21;
    private const int F_GETFL = 3;
    private const int F_SETFL = 4;
    private const int O_NONBLOCK = 0x800;
    private const short POLLOUT = 4;
    private const int RecvTimeoutMs = 400;
    private const int ConnectTimeoutMs = 4000;
    private const byte OppoChannel = 15;
    private const byte MaxRfcommChannel = 18;
    private const int MaxIdleTimeouts = 15;

    private readonly IFrameCodec _codec = new SppFrameCodec();
    private readonly IDeviceLocator _locator;
    private readonly List<byte> _framer = new();
    private readonly ConcurrentQueue<PodFrame> _rxQueue = new();
    private readonly object _lock = new();
    private int _socket = -1;
    private int _pollSeq;
    private int _recvTimeouts;
    private bool _disposed;

    public LinuxRfcommStreamTransport() : this(new LinuxBluetoothLocator()) { }
    public LinuxRfcommStreamTransport(IDeviceLocator locator) { _locator = locator; }
    public string? DeviceName { get; private set; }
    public string? LastError { get; private set; }
    public bool IsConnected { get; private set; }
    public event Action<PodFrame>? FrameReceived;
#pragma warning disable CS0067
    public event Action? Disconnected;
#pragma warning restore CS0067

    [DllImport("libc", SetLastError = true)] private static extern int socket(int domain, int type, int protocol);
    [DllImport("libc", SetLastError = true)] private static extern int connect(int sockfd, IntPtr addr, int addrlen);
    [DllImport("libc", SetLastError = true)] private static extern IntPtr send(int sockfd, byte[] buf, IntPtr len, int flags);
    [DllImport("libc", SetLastError = true)] private static extern IntPtr recv(int sockfd, byte[] buf, IntPtr len, int flags);
    [DllImport("libc", SetLastError = true)] private static extern int close(int fd);
    [DllImport("libc", SetLastError = true)] private static extern int setsockopt(int sockfd, int level, int optname, ref TimeValLinux optval, int optlen);
    [DllImport("libc", SetLastError = true)] private static extern int fcntl(int fd, int cmd, int arg);
    [DllImport("libc", SetLastError = true)] private static extern int poll(ref PollFd fds, int nfds, int timeout);

    [StructLayout(LayoutKind.Sequential)] private struct TimeValLinux { public long tv_sec; public long tv_usec; }
    [StructLayout(LayoutKind.Sequential)] private struct PollFd { public int fd; public short events; public short revents; }
    [StructLayout(LayoutKind.Sequential)] private struct SockAddrRc { public ushort rc_family; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public byte[] rc_bdaddr; public byte rc_channel; }

    public bool Connect()
    {
        try
        {
            Log.D("LXRFC", "Connect: 开始");
            var (addr, name) = _locator.Locate();
            if (addr == 0) { LastError = "未发现已配对的 OPPO 蓝牙设备"; return false; }
            DeviceName = name;
            if (!TryConnect(addr, OppoChannel))
            {
                Log.D("LXRFC", $"Connect: ch={OppoChannel} 失败，扫描 1-18");
                bool ok = false;
                for (byte ch = 1; ch <= MaxRfcommChannel; ch++)
                { if (ch == OppoChannel) continue; if (TryConnect(addr, ch)) { ok = true; break; } }
                if (!ok) { LastError = "所有 RFCOMM 通道均不可用"; return false; }
            }
            SetNonBlocking(_socket, false);
            var tv = new TimeValLinux { tv_sec = RecvTimeoutMs / 1000, tv_usec = (RecvTimeoutMs % 1000) * 1000 };
            setsockopt(_socket, SOL_SOCKET, SO_RCVTIMEO, ref tv, Marshal.SizeOf<TimeValLinux>());
            IsConnected = true; LastError = null; _framer.Clear(); _pollSeq = 0; _recvTimeouts = 0;
            Log.Result("LXRFC", "Connect", true, $"name=\"{DeviceName}\"");
            return true;
        }
        catch (Exception e) { LastError = e.Message; Log.Ex("LXRFC", "Connect", e); Cleanup(); return false; }
    }

    private bool TryConnect(ulong addr, byte ch)
    {
        Cleanup();
        _socket = socket(AF_BLUETOOTH, SOCK_STREAM, BTPROTO_RFCOMM);
        if (_socket < 0) return false;
        SetNonBlocking(_socket, true);
        var sa = new SockAddrRc { rc_family = AF_BLUETOOTH, rc_bdaddr = BtAddrToBytes(addr), rc_channel = ch };
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<SockAddrRc>());
        Marshal.StructureToPtr(sa, ptr, false);
        var r = connect(_socket, ptr, Marshal.SizeOf<SockAddrRc>());
        Marshal.FreeHGlobal(ptr);
        if (r != 0 && Marshal.GetLastWin32Error() != 115) { Cleanup(); return false; }
        if (r != 0)
        { var pfd = new PollFd { fd = _socket, events = POLLOUT }; if (poll(ref pfd, 1, ConnectTimeoutMs) <= 0) { Cleanup(); return false; } }
        Log.D("LXRFC", $"TryConnect: ch={ch} OK");
        return true;
    }

    public void Send(ushort cmd, byte[] payload)
    {
        var sock = _socket;
        if (sock < 0 || !IsConnected) return;
        byte[] bytes; lock (_lock) { bytes = _codec.Encode(cmd, payload); }
        try { send(sock, bytes, (IntPtr)bytes.Length, 0); } catch (Exception ex) { Log.Ex("LXRFC", $"Send cmd=0x{cmd:X4}", ex); }
    }

    public void Poll(int timeoutMs)
    {
        var sock = _socket;
        if (sock < 0 || !IsConnected) return;
        var end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        var buf = new byte[512];
        _pollSeq++;
        while (true)
        {
            int got; try { got = (int)recv(sock, buf, (IntPtr)buf.Length, 0); } catch { got = -1; }
            if (got > 0)
            { Log.D("LXRFC", $"Poll#{_pollSeq}: recv {got} bytes"); _recvTimeouts = 0;
              lock (_framer) { for (int i = 0; i < got; i++) { _framer.Add(buf[i]); while (_codec.TryDecode(_framer, out var f)) _rxQueue.Enqueue(f); } } }
            else if (got == 0) { Log.D("LXRFC", $"Poll#{_pollSeq}: recv=0, disconnected"); OnDisconnected(); return; }
            else
            {
                _recvTimeouts++;
                if (_recvTimeouts >= MaxIdleTimeouts)
                { Log.D("LXRFC", $"Poll#{_pollSeq}: idle timeout ({_recvTimeouts} timeouts), disconnecting"); OnDisconnected(); return; }
                if (_recvTimeouts == 1 || _recvTimeouts % 5 == 0) Log.D("LXRFC", $"Poll#{_pollSeq}: recv timeout (#{_recvTimeouts})");
            }
            while (_rxQueue.TryDequeue(out var frame)) FrameReceived?.Invoke(frame);
            if (!IsConnected || DateTime.UtcNow >= end) break;
            Thread.Sleep(20);
        }
        while (_rxQueue.TryDequeue(out var frame)) FrameReceived?.Invoke(frame);
    }

    private void OnDisconnected() { if (!IsConnected) return; IsConnected = false; Disconnected?.Invoke(); }
    public void Close() { IsConnected = false; Cleanup(); }
    private void Cleanup() { if (_socket >= 0) { lock (_lock) { try { close(_socket); } catch { } _socket = -1; } } }
    public void Dispose() { if (_disposed) return; Close(); _disposed = true; }
    private static void SetNonBlocking(int fd, bool nb) { var f = fcntl(fd, F_GETFL, 0); fcntl(fd, F_SETFL, nb ? f | O_NONBLOCK : f & ~O_NONBLOCK); }
    private static byte[] BtAddrToBytes(ulong addr) { var b = new byte[6]; for (int i = 0; i < 6; i++) b[i] = (byte)((addr >> (i * 8)) & 0xFF); return b; }
}
