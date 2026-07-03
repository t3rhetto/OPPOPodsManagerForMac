using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace OppoPodsManager;

public partial class ToastWindow : Window
{
    public ToastWindow()
    {
        InitializeComponent();
    }

    /// <summary>Show at desktop bottom-right with semi-transparency, auto-close after ms</summary>
    public static async Task ShowAsync(PodState state, string deviceName, int durationMs = 5000)
    {
        var toast = new ToastWindow();
        toast.TitleBlock.Text = deviceName;
        SetBat(toast.LeftPct, TryGet(state.Battery, "L"));
        SetBat(toast.RightPct, TryGet(state.Battery, "R"));
        SetBat(toast.CasePct, TryGet(state.Battery, "C"));
        await ShowAndClose(toast, durationMs);
    }

    public static async Task ShowLowBatteryAsync(PodState state, string deviceName)
    {
        var toast = new ToastWindow();
        toast.TitleBlock.Text = deviceName;
        SetBat(toast.LeftPct, TryGet(state.Battery, "L"));
        SetBat(toast.RightPct, TryGet(state.Battery, "R"));
        SetBat(toast.CasePct, TryGet(state.Battery, "C"));
        toast.LowBatteryOverlay.IsVisible = true;
        await ShowAndClose(toast, 5000);
    }

    public static async Task ShowCriticalBatteryAsync(PodState state, string deviceName)
    {
        var toast = new ToastWindow();
        toast.TitleBlock.Text = deviceName;
        SetBat(toast.LeftPct, TryGet(state.Battery, "L"));
        SetBat(toast.RightPct, TryGet(state.Battery, "R"));
        SetBat(toast.CasePct, TryGet(state.Battery, "C"));
        toast.CriticalBatteryOverlay.IsVisible = true;
        await ShowAndClose(toast, 5000);
    }

    public static async Task ShowDisconnectedAsync(string deviceName)
    {
        var toast = new ToastWindow();
        toast.BatteryPanel.IsVisible = false;
        toast.DisconnectPanel.IsVisible = true;
        toast.DisconnectTitle.Text = deviceName;
        await ShowAndClose(toast, 3000);
    }

    private static async Task ShowAndClose(ToastWindow toast, int durationMs)
    {
        toast.Opacity = 0;
        toast.Show();

        // Wait for layout to resolve SizeToContent, then position
        var positioned = false;
        toast.LayoutUpdated += (_, _) =>
        {
            if (positioned) return;
            if (toast.Bounds.Width <= 1 || toast.Bounds.Height <= 1) return;
            positioned = true;
            PositionAtBottomRight(toast, (int)toast.Bounds.Width, (int)toast.Bounds.Height);
            toast.Opacity = 0.9;
        };

        // Poll up to 2s for layout to settle
        for (int i = 0; i < 40 && !positioned; i++)
            await Task.Delay(50);

        // Fallback: position using actual bounds (should be valid after 2s)
        if (!positioned)
        {
            var w = (int)toast.Bounds.Width;
            var h = (int)toast.Bounds.Height;
            if (w <= 1) w = 280;
            if (h <= 1) h = 120;
            PositionAtBottomRight(toast, w, h);
            toast.Opacity = 0.9;
        }

        await Task.Delay(durationMs);
        await Dispatcher.UIThread.InvokeAsync(() => toast.Close());
    }

    private static void PositionAtBottomRight(Window w, int width, int height)
    {
        try
        {
            var screen = w.Screens.Primary;
            if (screen == null) return;
            var wa = screen.WorkingArea;
            w.Position = new PixelPoint(wa.Right - width - 120, wa.Bottom - height - 48);
        }
        catch { }
    }

    private static void SetBat(TextBlock tb, (int Lvl, bool Chg)? d)
    {
        if (d is not { } v) { tb.Text = "- %"; return; }
        tb.Text = $"{v.Lvl}%{(v.Chg ? " ⚡" : "")}";
    }

    private static (int, bool)? TryGet(Dictionary<string, (int, bool)?> dict, string key) =>
        dict.TryGetValue(key, out var v) ? v : null;
}
