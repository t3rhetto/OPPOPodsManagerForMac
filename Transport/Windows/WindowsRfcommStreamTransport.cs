using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace OppoPodsManager;

/// <summary>
/// 官方形式的经典蓝牙 RFCOMM 传输（WinRT StreamSocket）。
/// 发现走 RfcommServiceFinder（按服务 UUID / 配对名枚举），连接用
/// StreamSocket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName)，
/// 无需像 Winsock 方案那样猜 UUID/port。帧格式与 Winsock SPP 一致（SppFrameCodec，0xAA 外壳）。
/// 作为首选 SPP 连接方式；失败时由 FallbackTransport 回退到 Winsock SppTransport。
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class WindowsRfcommStreamTransport : IPodTransport
{
    private const int ConnectTimeoutMs = 6000;

    private readonly IFrameCodec _codec = new SppFrameCodec();
    private readonly List<byte> _framer = new();
    private readonly ConcurrentQueue<PodFrame> _rxQueue = new();
    private readonly object _sendLock = new();

    private RfcommDeviceService? _service;
    private StreamSocket? _socket;
    private DataWriter? _writer;
    private DataReader? _reader;
    private CancellationTokenSource? _readCts;
    private Task? _readLoop;
    private bool _disposed;

    public string? DeviceName { get; private set; }
    public string? LastError { get; private set; }
    public bool IsConnected { get; private set; }

    public event Action<PodFrame>? FrameReceived;
    public event Action? Disconnected;

    public bool Connect()
    {
        try
        {
            Log.D("RFSOCK", "Connect: 开始");
            if (!RunSync(ConnectAsyncCore, ConnectTimeoutMs))
            {
                Cleanup();
                if (string.IsNullOrEmpty(LastError)) LastError = "RFCOMM StreamSocket 连接超时";
                Log.Result("RFSOCK", "Connect", false, LastError);
                return false;
            }

            IsConnected = true;
            LastError = null;
            StartReadLoop();
            Log.Result("RFSOCK", "Connect", true, $"name=\"{DeviceName}\"");
            return true;
        }
        catch (Exception e)
        {
            LastError = e.Message;
            Log.Ex("RFSOCK", "Connect", e);
            Cleanup();
            return false;
        }
    }

    private async Task<bool> ConnectAsyncCore()
    {
        _service = await RfcommServiceFinder.FindServiceAsync();
        if (_service == null) { LastError = "未发现 OPPO SPP RFCOMM 服务"; return false; }

        var dev = _service.Device;
        DeviceName = dev?.Name ?? "OPPO 耳机";
        Log.D("RFSOCK", $"Connect: 命中服务 name=\"{DeviceName}\" host={_service.ConnectionHostName} svc={_service.ConnectionServiceName}");

        _socket = new StreamSocket();
        // 官方形式：用服务自带的 ConnectionHostName / ConnectionServiceName 连接
        await _socket.ConnectAsync(_service.ConnectionHostName, _service.ConnectionServiceName);

        _writer = new DataWriter(_socket.OutputStream);
        _reader = new DataReader(_socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };

        _framer.Clear();
        while (_rxQueue.TryDequeue(out _)) { }
        Log.D("RFSOCK", "Connect: StreamSocket 就绪");
        return true;
    }

    private void StartReadLoop()
    {
        _readCts = new CancellationTokenSource();
        var ct = _readCts.Token;
        _readLoop = Task.Run(() => ReadLoopAsync(ct), ct);
    }

    /// <summary>后台读循环：持续从 InputStream 读字节，解帧后入队，由 Poll 交付上层。</summary>
    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var reader = _reader;
        if (reader == null) return;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                uint got = await reader.LoadAsync(512).AsTask(ct);
                if (got == 0) { Log.D("RFSOCK", "ReadLoop: 对端关闭 (load=0)"); break; }

                var chunk = new byte[got];
                reader.ReadBytes(chunk);
                lock (_framer)
                {
                    for (int i = 0; i < chunk.Length; i++)
                    {
                        _framer.Add(chunk[i]);
                        while (_codec.TryDecode(_framer, out var frame))
                            _rxQueue.Enqueue(frame);
                    }
                }
            }
        }
        catch (OperationCanceledException) { /* 正常取消 */ }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested) Log.Ex("RFSOCK", "ReadLoop", ex);
        }
        finally
        {
            if (!ct.IsCancellationRequested) OnDisconnected();
        }
    }

    public void Send(ushort cmd, byte[] payload)
    {
        var writer = _writer;
        if (writer == null) { Log.D("RFSOCK", $"Send cmd=0x{cmd:X4} 失败: 未连接"); return; }

        byte[] bytes = _codec.Encode(cmd, payload);
        try
        {
            lock (_sendLock)
            {
                writer.WriteBytes(bytes);
                RunSync(async () => { await writer.StoreAsync(); return true; }, 3000);
            }
            Log.D("RFSOCK", $"Send cmd=0x{cmd:X4} payload={payload?.Length ?? 0}B -> {bytes.Length}B");
        }
        catch (Exception ex) { Log.Ex("RFSOCK", $"Send cmd=0x{cmd:X4}", ex); }
    }

    public void Poll(int timeoutMs)
    {
        // 读在后台循环里做，这里在时间预算内取出已入队的帧交付上层
        var end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (true)
        {
            while (_rxQueue.TryDequeue(out var frame))
            {
                Log.D("RFSOCK", $"Poll: 交付帧 cmd=0x{frame.Cmd:X4} payload={frame.Payload.Length}B");
                FrameReceived?.Invoke(frame);
            }
            if (!IsConnected) return;
            if (DateTime.UtcNow >= end) break;
            Thread.Sleep(20);
        }
        while (_rxQueue.TryDequeue(out var frame))
        {
            Log.D("RFSOCK", $"Poll: 交付帧 cmd=0x{frame.Cmd:X4} payload={frame.Payload.Length}B");
            FrameReceived?.Invoke(frame);
        }
    }

    private void OnDisconnected()
    {
        if (!IsConnected) return;
        IsConnected = false;
        Disconnected?.Invoke();
    }

    public void Close()
    {
        IsConnected = false;
        Cleanup();
    }

    private void Cleanup()
    {
        try { _readCts?.Cancel(); } catch { }
        try
        {
            if (_writer != null) { _writer.DetachStream(); _writer.Dispose(); }
        }
        catch { }
        try
        {
            if (_reader != null) { _reader.DetachStream(); _reader.Dispose(); }
        }
        catch { }
        try { _socket?.Dispose(); } catch { }
        try { _service?.Dispose(); } catch { }
        _writer = null;
        _reader = null;
        _socket = null;
        _service = null;
        _readCts = null;
        _readLoop = null;
    }

    private static bool RunSync(Func<Task<bool>> op, int timeoutMs)
    {
        try
        {
            var task = op();
            return task.Wait(timeoutMs) && task.Result;
        }
        catch (Exception ex) { Log.Ex("RFSOCK", "RunSync", ex); return false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Close();
        _disposed = true;
    }
}
