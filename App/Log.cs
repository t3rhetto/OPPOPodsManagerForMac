using System;
using System.Diagnostics;

namespace OppoPodsManager;

/// <summary>
/// 日志封装。通过 <see cref="Trace.WriteLine(string)"/> 输出到所有已注册的 TraceListener
/// （含 UI 日志面板的 LogTraceListener），同时转发 Debug.WriteLine 供 DebugView 等工具捕获。
/// 用法：
///   Log.D("BT", "connect ok");
///   Log.D("BT", $"recv {n} bytes");
///   Log.Ex("BT", "Connect failed", ex);
/// 输出格式：HH:mm:ss.fff [tag] message
/// </summary>
public static class Log
{
    /// <summary>写一条带时间戳和分类标签的日志。</summary>
    public static void D(string tag, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{tag}] {message}";
        Trace.WriteLine(line);
        Debug.WriteLine(line);
    }

    /// <summary>记录操作结果（OK/FAIL）。</summary>
    public static void Result(string tag, string operation, bool ok, string? detail = null)
    {
        var status = ok ? "OK" : "FAIL";
        var tail = string.IsNullOrEmpty(detail) ? "" : " — " + detail;
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{tag}] {operation}: {status}{tail}";
        Trace.WriteLine(line);
        Debug.WriteLine(line);
    }

    /// <summary>记录异常（含上下文说明）。</summary>
    public static void Ex(string tag, string context, Exception ex)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{tag}] {context} EXCEPTION: {ex.GetType().Name}: {ex.Message}";
        Trace.WriteLine(line);
        Debug.WriteLine(line);
    }
}
