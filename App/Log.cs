using System;
using System.Diagnostics;

namespace OppoPodsManager;

/// <summary>
/// 调试日志封装。通过 <see cref="Debug.WriteLine(string)"/> 输出（OutputDebugString），
/// VS 输出窗口 / DebugView 可见。Release 构建下 [Conditional("DEBUG")] 使调用被编译器移除。
/// 用法：
///   Log.D("BT", "connect ok");
///   Log.D("BT", $"recv {n} bytes");
///   Log.Ex("BT", "Connect failed", ex);
/// 输出格式：HH:mm:ss.fff [tag] message
/// </summary>
public static class Log
{
    /// <summary>写一条带时间戳和分类标签的调试日志。</summary>
    [Conditional("DEBUG")]
    public static void D(string tag, string message)
    {
        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{tag}] {message}");
    }

    /// <summary>记录操作结果（OK/FAIL）。</summary>
    [Conditional("DEBUG")]
    public static void Result(string tag, string operation, bool ok, string? detail = null)
    {
        var status = ok ? "OK" : "FAIL";
        var tail = string.IsNullOrEmpty(detail) ? "" : " — " + detail;
        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{tag}] {operation}: {status}{tail}");
    }

    /// <summary>记录异常（含上下文说明）。</summary>
    [Conditional("DEBUG")]
    public static void Ex(string tag, string context, Exception ex)
    {
        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{tag}] {context} EXCEPTION: {ex.GetType().Name}: {ex.Message}");
    }
}
