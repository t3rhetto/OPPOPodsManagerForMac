using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;

namespace OppoPodsWPF;

public partial class ToastWindow : Window
{
    public ToastWindow()
    {
        InitializeComponent();
    }

    public static async Task ShowAsync(PodState state, string deviceName)
    {
        await Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            var toast = new ToastWindow();
            toast.TitleBlock.Text = deviceName;

            SetBat(toast.LeftPct, TryGet(state.Battery, "L"));
            SetBat(toast.RightPct, TryGet(state.Battery, "R"));
            SetBat(toast.CasePct, TryGet(state.Battery, "C"));

            toast.WindowStartupLocation = WindowStartupLocation.Manual;
            toast.Opacity = 0;
            var tcs = new TaskCompletionSource();
            toast.ContentRendered += (_, _) =>
            {
                var sw = SystemParameters.WorkArea.Width;
                var sh = SystemParameters.WorkArea.Height;
                toast.Left = sw - toast.ActualWidth - 16;
                toast.Top = sh - toast.ActualHeight - 16;
                tcs.TrySetResult();
            };
            toast.Show();
            await tcs.Task; // 等布局完成再动画

            // 淡入动画
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            toast.BeginAnimation(OpacityProperty, fadeIn);

            // 停留 3 秒后淡出
            await Task.Delay(3000);
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
            fadeOut.Completed += (_, _) => toast.Close();
            toast.BeginAnimation(OpacityProperty, fadeOut);
        });
    }

    private static void SetBat(System.Windows.Controls.TextBlock tb, (int Lvl, bool Chg)? d)
    {
        if (d is not { } v) { tb.Text = "- %"; return; }
        tb.Text = $"{v.Lvl}%{(v.Chg ? " ⚡" : "")}";
    }

    private static (int, bool)? TryGet(Dictionary<string, (int, bool)?> dict, string key) =>
        dict.TryGetValue(key, out var v) ? v : null;
}
