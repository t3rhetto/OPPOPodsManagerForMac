using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace OppoPodsManager;

public sealed class MacRfcommStreamTransport : IPodTransport
{
    private const string Libc = "libc";
    // macOS BSD socket constants
    private const int AF_BLUETOOTH = 31;
    private const int SOCK_STREAM = 1;
    private const int BTPROTO_RFCOMM = 3;
    private const int SOL_SOCKET = 0xffff;
    private const int SO_SNDTIMEO = 0x1005;
    private const int SO_RCVTIMEO = 0x1006;
    private const int F_GETFL = 3;
    private const int F_SETFL = 4;
    private const int O_NONBLOCK = 0x0004;
    private const int EAGAIN = 35;
    private const int EWOULDBLOCK = 35;

    [DllImport(Libc, SetLastError = true)]
    private static extern int socket(int domain, int type, int protocol);

    [DllImport(Libc, SetLastError = true)]
    private static extern int connect(int sockfd, ref SockAddrRc addr, uint addrlen);

    [DllImport(Libc, SetLastError = true)]
    private static extern IntPtr read(int fd, byte[] buf, IntPtr count);

    [DllImport(Libc, SetLastError = true)]
    private static extern IntPtr write(int fd, byte[] buf, IntPtr count);

    [DllImport(Libc)]
    private static extern int close(int fd);

    [DllImport(Libc, SetLastError = true)]
    private static extern int setsockopt(int sockfd, int level, int optname, ref TimeVal optval, uint optlen);

    [DllImport(Libc, SetLastError = true)]
    private static extern int fcntl(int fd, int cmd, int arg);

    [DllImport(Libc, SetLastError = true)]
    private static extern int fcntl(int fd, int cmd);

    // macOS sockaddr_rc: 1 byte len + 1 byte family + 6 bytes addr + 1 byte channel + 1 byte pad
    [StructLayout(LayoutKind.Sequential, Size = 10)]
    private struct SockAddrRc
    {
        public byte rc_len;
        public byte rc_family;
        public byte b0, b1, b2, b3, b4, b5;
        public byte rc_channel;
        public byte rc_pad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TimeVal
    {
        public long tv_sec;
        public long tv_usec;
    }

    private const int MaxRfcommChannel = 30;
    private const int MaxIdleTimeouts = 200;
    private const int ReadBufferSize = 512;

    private readonly IFrameCodec _codec = new SppFrameCodec();
    private readonly List<byte> _framer = new();
    private readonly ConcurrentQueue<PodFrame> _rxQueue = new();
    private readonly IDeviceLocator _locator;
    private readonly object _sendLock = new();

    private int _socketFd = -1;
    private Thread? _readThread;
    private volatile bool _disposed;
    private volatile bool _readLoopActive;
    private int _idleCounter;

    public MacRfcommStreamTransport() : this(new MacBluetoothLocator()) { }
    public MacRfcommStreamTransport(IDeviceLocator locator) { _locator = locator; }

    public string? DeviceName { get; private set; }
    public string? LastError { get; private set; }
    public bool IsConnected { get; private set; }
    public event Action<PodFrame>? FrameReceived;
    public event Action? Disconnected;

    public bool Connect()
    {
        try
        {
            Log.D("MACRFC", "Connect: start");
            IsConnected = false; LastError = null; _disposed = false;

            var (addr, name) = _locator.Locate();
            if (addr == 0) { LastError = "No paired OPPO device found"; return false; }
            DeviceName = name;
            Log.D("MACRFC", $"Connect: found \"{name}\" addr=0x{addr:X12}");

            int fd = ScanAndConnect(addr);
            if (fd < 0) { LastError = $"No OPPO RFCOMM channel (1-{MaxRfcommChannel})"; return false; }

            _socketFd = fd;
            _framer.Clear();
            while (_rxQueue.TryDequeue(out _)) { }

            if (fcntl(_socketFd, F_SETFL, fcntl(_socketFd, F_GETFL) | O_NONBLOCK) < 0)
                Log.D("MACRFC", "Connect: fcntl O_NONBLOCK failed");

            IsConnected = true; LastError = null; _idleCounter = 0;
            StartReadLoop();
            Log.Result("MACRFC", "Connect", true, $"\"{name}\" fd={fd}");
            return true;
        }
        catch (Exception e) { LastError = e.Message; Log.Ex("MACRFC", "Connect", e); CleanupSocket(); return false; }
    }

    private int ScanAndConnect(ulong addr)
    {
        Log.D("MACRFC", $"Scan: starting ch 1-{MaxRfcommChannel}");
        var probeBytes = _codec.Encode(OppoProtocol.CmdBattery, Array.Empty<byte>());
        var recvBuf = new byte[64];
        int errRefused = 0, errTimeout = 0, errOther = 0;
        var openChannels = new List<string>();

        for (int ch = 1; ch <= MaxRfcommChannel; ch++)
        {
            int fd = socket(AF_BLUETOOTH, SOCK_STREAM, BTPROTO_RFCOMM);
            if (fd < 0) { errOther++; continue; }

            var sockAddr = BuildSockAddr(addr, ch);
            var tvConn = new TimeVal { tv_sec = 0, tv_usec = 200000 };
            setsockopt(fd, SOL_SOCKET, SO_SNDTIMEO, ref tvConn, 16);

            int cr = connect(fd, ref sockAddr, 10);
            if (cr < 0)
            {
                int err = Marshal.GetLastWin32Error();
                if (err == 61) errRefused++;       // ECONNREFUSED
                else if (err == 60) errTimeout++;   // ETIMEDOUT
                else { errOther++; Log.D("MACRFC", $"Scan: ch={ch} connect errno={err}"); }
                close(fd); continue;
            }

            IntPtr wrote = write(fd, probeBytes, (IntPtr)probeBytes.Length);
            if (wrote == (IntPtr)(-1) || wrote == IntPtr.Zero) { close(fd); continue; }

            var tvRead = new TimeVal { tv_sec = 0, tv_usec = 200000 };
            setsockopt(fd, SOL_SOCKET, SO_RCVTIMEO, ref tvRead, 16);

            IntPtr got = read(fd, recvBuf, (IntPtr)recvBuf.Length);
            int n = (int)(long)got;

            if (n > 0)
            {
                if (recvBuf[0] == SppFrameCodec.Header)
                {
                    Log.D("MACRFC", $"Scan: ch={ch} PROBE OK! reusing socket fd={fd}");
                    while (true)
                    {
                        Thread.Sleep(30);
                        got = read(fd, recvBuf, (IntPtr)recvBuf.Length);
                        if ((long)got <= 0) break;
                    }
                    return fd;
                }
                string hex = BitConverter.ToString(recvBuf, 0, Math.Min(n, 10));
                openChannels.Add($"ch={ch}:0x{recvBuf[0]:X2}({hex})");
            }
            close(fd);
        }

        Log.D("MACRFC", $"Scan: {MaxRfcommChannel} ch done. refused={errRefused} timeout={errTimeout} other={errOther}");
        if (openChannels.Count > 0) Log.D("MACRFC", $"Scan: open (wrong proto): {string.Join(", ", openChannels)}");
        return -1;
    }

    private static SockAddrRc BuildSockAddr(ulong addr, int channel) => new()
    {
        rc_len = 10,
        rc_family = (byte)AF_BLUETOOTH,
        b0 = (byte)(addr & 0xFF),
        b1 = (byte)((addr >> 8) & 0xFF),
        b2 = (byte)((addr >> 16) & 0xFF),
        b3 = (byte)((addr >> 24) & 0xFF),
        b4 = (byte)((addr >> 32) & 0xFF),
        b5 = (byte)((addr >> 40) & 0xFF),
        rc_channel = (byte)channel,
        rc_pad = 0,
    };

    private void StartReadLoop()
    {
        _readLoopActive = true;
        _readThread = new Thread(ReadLoop) { IsBackground = true, Name = "MACRFC-Read" };
        _readThread.Start();
    }

    private void ReadLoop()
    {
        var buf = new byte[ReadBufferSize];
        int fd = _socketFd;
        try
        {
            while (_readLoopActive && !_disposed)
            {
                if (fd < 0) break;
                IntPtr got;
                try { got = read(fd, buf, (IntPtr)buf.Length); }
                catch { break; }
                int n = (int)(long)got;
                if (n > 0)
                {
                    _idleCounter = 0;
                    lock (_framer)
                    {
                        for (int i = 0; i < n; i++)
                        {
                            _framer.Add(buf[i]);
                            while (_codec.TryDecode(_framer, out var frame))
                            {
                                _rxQueue.Enqueue(frame);
                                try { FrameReceived?.Invoke(frame); }
                                catch (Exception ex) { Log.Ex("MACRFC", "ReadLoop dispatch", ex); }
                            }
                        }
                    }
                }
                else if (n == 0) { Log.D("MACRFC", "ReadLoop: peer closed"); break; }
                else
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err == EAGAIN || err == EWOULDBLOCK)
                    {
                        _idleCounter++;
                        if (_idleCounter > MaxIdleTimeouts) { Log.D("MACRFC", "ReadLoop: idle timeout"); break; }
                        Thread.Sleep(50); continue;
                    }
                    Log.D("MACRFC", $"ReadLoop: read errno={err}"); break;
                }
            }
        }
        catch (Exception ex) { if (_readLoopActive) Log.Ex("MACRFC", "ReadLoop", ex); }
        finally { _readLoopActive = false; if (IsConnected) OnDisconnected(); }
    }

    public void Send(ushort cmd, byte[] payload)
    {
        _idleCounter = 0;
        if (!IsConnected || _socketFd < 0) return;
        byte[] bytes;
        lock (_sendLock) { bytes = _codec.Encode(cmd, payload); }
        try
        {
            lock (_sendLock)
            {
                IntPtr w = write(_socketFd, bytes, (IntPtr)bytes.Length);
                if (w == (IntPtr)(-1)) Log.D("MACRFC", $"Send 0x{cmd:X4} failed errno={Marshal.GetLastWin32Error()}");
            }
        }
        catch (Exception ex) { Log.Ex("MACRFC", $"Send 0x{cmd:X4}", ex); }
    }

    public void Poll(int timeoutMs)
    {
        _idleCounter = 0;
        if (!IsConnected) return;
        var end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (true)
        {
            while (_rxQueue.TryDequeue(out var frame)) FrameReceived?.Invoke(frame);
            if (!IsConnected || !_readLoopActive) return;
            if (DateTime.UtcNow >= end) break;
            Thread.Sleep(20);
        }
        while (_rxQueue.TryDequeue(out var frame)) FrameReceived?.Invoke(frame);
    }

    private void OnDisconnected() { if (!IsConnected) return; IsConnected = false; Disconnected?.Invoke(); }
    public void Close() { IsConnected = false; _readLoopActive = false; CleanupSocket(); }
    private void CleanupSocket() { var fd = Interlocked.Exchange(ref _socketFd, -1); if (fd >= 0) close(fd); }
    public void Dispose() { if (_disposed) return; _disposed = true; Close(); }
}
