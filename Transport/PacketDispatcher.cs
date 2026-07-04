using System;
using System.Collections.Generic;
using System.Threading;

namespace OppoPodsManager;

/// <summary>命令发送结果状态码（对齐官方：0=成功，0x6=超时，其余为设备返回的错误码）。</summary>
public enum CmdStatus
{
    Success = 0,
    Timeout = 0x6,   // 官方 PacketTimeoutProcessor 超时回送的状态字节
    Failed = 0xFF,   // 链路断开/未连接等本地失败
}

/// <summary>
/// 命令分发器：在 IPodTransport 之上实现官方式的发送鲁棒性（对齐 PacketTimeoutProcessor）：
///   · 每条"需要响应"的命令注册 10s 超时；
///   · 响应按 (TRANS_ID, 基础 cmd) 配对（响应 cmd = 请求 cmd | 0x8000）；
///   · 超时按重试次数重发，用尽则以 0x6 状态回调（官方超时状态字节）；
///   · 链路断开时所有挂起命令以 Failed 回调。
/// 本类是 IPodTransport 装饰器：普通 Send/Poll/Connect 原样转发；SendTracked 才走队列。
/// 采用"基础 cmd"配对（本机 SPP seq 固定，与官方 key 低 15 位=cmd 等价），
/// 我方串行发送不会同 cmd 并发，配对稳定。
/// </summary>
public sealed class PacketDispatcher : IPodTransport
{
    private const int DefaultTimeoutMs = 10000;   // 官方每命令超时 10s
    private const int SweepIntervalMs = 500;      // 超时扫描周期

    private sealed class Pending
    {
        public ushort Cmd;                 // 基础 cmd（不含 0x8000）
        public byte[] Payload = Array.Empty<byte>();
        public Action<CmdStatus, PodFrame?>? OnResult;
        public long DeadlineTicks;         // Environment.TickCount64 到期时刻
        public int RetriesLeft;
        public int TimeoutMs;
    }

    private readonly IPodTransport _inner;
    private readonly object _lock = new();
    private readonly Dictionary<ushort, Pending> _pending = new();  // key = 基础 cmd
    private Timer? _sweeper;
    private bool _disposed;

    public PacketDispatcher(IPodTransport inner)
    {
        _inner = inner;
        _inner.FrameReceived += OnInnerFrame;
        _inner.Disconnected += OnInnerDisconnected;
    }

    // ===== IPodTransport 透传 =====
    public string? DeviceName => _inner.DeviceName;
    public bool IsConnected => _inner.IsConnected;
    public string? LastError => _inner.LastError;
    public event Action<PodFrame>? FrameReceived;
    public event Action? Disconnected;

    public bool Connect()
    {
        var ok = _inner.Connect();
        if (ok) EnsureSweeper();
        return ok;
    }

    public void Send(ushort cmd, byte[] payload) => _inner.Send(cmd, payload);
    public void Poll(int timeoutMs) => _inner.Poll(timeoutMs);

    public void Close()
    {
        FailAllPending(CmdStatus.Failed);
        _inner.Close();
    }

    /// <summary>
    /// 发送一条"期望响应"的命令并注册超时/重试。收到匹配响应或超时后回调。
    /// onResult(status, respFrame)：status=Success 时 respFrame 为响应帧；超时/失败时为 null。
    /// </summary>
    public void SendTracked(ushort cmd, byte[] payload, Action<CmdStatus, PodFrame?>? onResult = null,
                            int retries = 1, int timeoutMs = DefaultTimeoutMs)
    {
        var baseCmd = (ushort)(cmd & 0x7FFF);
        lock (_lock)
        {
            _pending[baseCmd] = new Pending
            {
                Cmd = baseCmd,
                Payload = payload,
                OnResult = onResult,
                DeadlineTicks = Environment.TickCount64 + timeoutMs,
                RetriesLeft = retries,
                TimeoutMs = timeoutMs,
            };
        }
        EnsureSweeper();
        _inner.Send(cmd, payload);
    }

    private void OnInnerFrame(PodFrame frame)
    {
        // 响应帧（第 15 位置位）：按基础 cmd 配对并完成挂起项
        if ((frame.Cmd & 0x8000) != 0)
        {
            var baseCmd = (ushort)(frame.Cmd & 0x7FFF);
            Pending? p = null;
            lock (_lock)
            {
                if (_pending.TryGetValue(baseCmd, out p)) _pending.Remove(baseCmd);
            }
            if (p != null)
            {
                // 响应载荷首字节为状态码（官方 CommandUtil.j）；空载荷视为成功
                var status = (frame.Payload != null && frame.Payload.Length > 0)
                    ? (CmdStatus)frame.Payload[0] : CmdStatus.Success;
                try { p.OnResult?.Invoke(status == 0 ? CmdStatus.Success : status, frame); }
                catch (Exception ex) { Log.Ex("DISPATCH", "OnResult", ex); }
            }
        }
        // 无论是否配对，都把帧继续交给上层解析（DispatchFrame）
        FrameReceived?.Invoke(frame);
    }

    private void OnInnerDisconnected()
    {
        FailAllPending(CmdStatus.Failed);
        Disconnected?.Invoke();
    }

    private void EnsureSweeper()
    {
        if (_sweeper != null || _disposed) return;
        _sweeper = new Timer(_ => Sweep(), null, SweepIntervalMs, SweepIntervalMs);
    }

    /// <summary>超时扫描：到期项按剩余次数重发，用尽则以 0x6 回调（官方超时状态）。</summary>
    private void Sweep()
    {
        var now = Environment.TickCount64;
        List<Pending>? retries = null;
        List<Pending>? timeouts = null;

        lock (_lock)
        {
            if (_pending.Count == 0) return;
            foreach (var kv in new List<KeyValuePair<ushort, Pending>>(_pending))
            {
                var p = kv.Value;
                if (now < p.DeadlineTicks) continue;
                if (p.RetriesLeft > 0)
                {
                    p.RetriesLeft--;
                    p.DeadlineTicks = now + p.TimeoutMs;
                    (retries ??= new()).Add(p);
                }
                else
                {
                    _pending.Remove(kv.Key);
                    (timeouts ??= new()).Add(p);
                }
            }
        }

        if (retries != null)
            foreach (var p in retries)
            {
                Log.D("DISPATCH", $"重发超时命令 cmd=0x{p.Cmd:X4}（剩余重试 {p.RetriesLeft}）");
                try { _inner.Send(p.Cmd, p.Payload); } catch (Exception ex) { Log.Ex("DISPATCH", "重发", ex); }
            }

        if (timeouts != null)
            foreach (var p in timeouts)
            {
                Log.D("DISPATCH", $"命令超时 cmd=0x{p.Cmd:X4} status=0x6");
                try { p.OnResult?.Invoke(CmdStatus.Timeout, null); }
                catch (Exception ex) { Log.Ex("DISPATCH", "超时回调", ex); }
            }
    }

    private void FailAllPending(CmdStatus status)
    {
        List<Pending> all;
        lock (_lock)
        {
            if (_pending.Count == 0) return;
            all = new List<Pending>(_pending.Values);
            _pending.Clear();
        }
        foreach (var p in all)
        {
            try { p.OnResult?.Invoke(status, null); }
            catch (Exception ex) { Log.Ex("DISPATCH", "失败回调", ex); }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _sweeper?.Dispose(); } catch { }
        _sweeper = null;
        FailAllPending(CmdStatus.Failed);
        _inner.FrameReceived -= OnInnerFrame;
        _inner.Disconnected -= OnInnerDisconnected;
        _inner.Dispose();
    }
}
