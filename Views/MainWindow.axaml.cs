using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SukiUI;
using SukiUI.Controls;
using SukiUI.Toasts;

namespace OppoPodsManager;

// 资源路径常量
file static class AppConst
{
    public const string IconConnected = "avares://OppoPodsManager/Assets/tuopan.ico";
    public const string IconDisconnected = "avares://OppoPodsManager/Assets/tuopandis.png";
}

// 从嵌入资源加载图标（适用于 Avalonia + AOT）

file static class AssetHelper
{
    public static WindowIcon? LoadIcon(string avaresPath)
    {
        try
        {
            var uri = new Uri(avaresPath, UriKind.Absolute);
            var stream = AssetLoader.Open(uri);
            return new WindowIcon(stream);
        }
        catch
        {
            return null;
        }
    }

    public static Bitmap? LoadBitmap(string avaresPath)
    {
        try
        {
            var uri = new Uri(avaresPath, UriKind.Absolute);
            var stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }
}

public partial class MainWindow : SukiWindow
{
    // 前端只依赖 IPodManager 契约；构造点耦合具体类，其余交互全走接口
    private readonly IPodManager _rfcomm = new RfcommService();
    private CancellationTokenSource? _pollCts;
    private string _ancMain = "", _ancLevel = "";
    private DateTime _ancUserSetAt = DateTime.MinValue;
    private DateTime _featureUserSetAt = DateTime.MinValue;
    private WindowIcon? _iconConnected, _iconDisconnected;
    private TrayIcon? _trayIcon;
    private bool _realClose;
    internal static ISukiToastManager ToastManager = new SukiToastManager();
    private string? _modelOverride;
    private bool _gameModeCompat;
    private bool _wasConnected;
    private bool _lowBatteryAlerted;
    private bool _criticalBatteryAlerted;
    private List<string> _allModelNames = new();

    // 缓存画刷，减少 GC 压力
    private static readonly SolidColorBrush BrushGreen = new(Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly SolidColorBrush BrushLightGreen = new(Color.FromRgb(0x88, 0xCC, 0x88));
    private static readonly SolidColorBrush BrushRed = new(Color.FromRgb(0xFF, 0x55, 0x55));
    private static readonly SolidColorBrush BrushLightRed = new(Color.FromRgb(0xFF, 0x88, 0x88));
    private static readonly SolidColorBrush BrushGray = new(Color.FromRgb(0xCC, 0xCC, 0xCC));
    private static readonly SolidColorBrush BrushAccent = new(Color.FromRgb(0x60, 0x90, 0xFF));
    private static readonly SolidColorBrush BrushTransparent = new SolidColorBrush(Colors.Transparent);
    private static readonly SolidColorBrush BrushWhite = new SolidColorBrush(Colors.White);

    // CheckBox 脏检查状态
    private bool _prevSpatialSound;
    private bool _prevGameMode;
    private bool _prevDualDevice;

    // 三级联动：品牌 → 子系列 → 机型
    private readonly ObservableCollection<string> _brandList = new();
    private readonly ObservableCollection<string> _seriesList = new();
    private readonly ObservableCollection<string> _modelList = new();
    private Dictionary<string, Dictionary<string, List<string>>> _brandTree = new();

    public MainWindow()
    {
        try
        {
            Log.D("UI", "MainWindow 构造开始");
            InitializeComponent();
            Log.D("UI", "InitializeComponent OK");

        // Wire events programmatically (Avalonia 12 compatibility)
            CbSpatial.IsCheckedChanged += CbSpatial_Changed;
            CbGame.IsCheckedChanged += CbGame_Changed;
        CbDualDevice.IsCheckedChanged += CbDualDevice_Changed;
        CbTray.IsCheckedChanged += CbTray_Changed;
        CbAuto.IsCheckedChanged += CbAuto_Changed;
        CbEq.SelectionChanged += CbEq_SelectionChanged;
        CbBrand.SelectionChanged += CbBrand_Changed;
        CbSeries.SelectionChanged += CbSeries_Changed;
        CbModel.SelectionChanged += CbModel_Changed;
        CbGameMode.SelectionChanged += CbGameMode_Changed;
        CbTheme.SelectionChanged += CbTheme_Changed;
        TbCustomName.TextChanged += TbCustomName_Changed;

        // 多设备下拉
        // 多设备下拉
        DeviceExpander.Expanded += DeviceExpander_Expanded;
        SyncMultiDeviceList();

        // Wire Spatial Audio radio buttons
        SpatialAudio_Init();

        // 主题：默认跟随系统
        var themeIndex = SettingsManager.GetInt("Theme", 0);
        ApplyTheme(themeIndex);
        CbTheme.SelectedIndex = themeIndex;

        // 开机自启时最小化启动
        var args = Environment.GetCommandLineArgs();
        if (args.Contains("--minimized"))
        {
            WindowState = WindowState.Minimized;
            Hide();
        }

        // 固定在屏幕右下角（已取消，使用默认居中）

        // 托盘图标
        _iconConnected = AssetHelper.LoadIcon(AppConst.IconConnected);
        _iconDisconnected = AssetHelper.LoadIcon(AppConst.IconDisconnected);
        SetupTrayIcon();
        // Setup SukiUI toast host
        Hosts = [new SukiToastHost { Manager = ToastManager }];

        // Load battery images
        var leftBmp = AssetHelper.LoadBitmap("avares://OppoPodsManager/Assets/left.png");
        if (leftBmp != null) LeftImage.Source = leftBmp;
        var caseBmp = AssetHelper.LoadBitmap("avares://OppoPodsManager/Assets/cang.png");
        if (caseBmp != null) CaseImage.Source = caseBmp;
        var rightBmp = AssetHelper.LoadBitmap("avares://OppoPodsManager/Assets/right.png");
        if (rightBmp != null) RightImage.Source = rightBmp;

        // 先填充默认 EQ 列表
        foreach (var kv in _rfcomm.Caps.EqPresets) CbEq.Items.Add(kv.Key);
        CbTray.IsChecked = SettingsManager.GetBool("TrayEnabled", false);
        CbAuto.IsChecked = SettingsManager.GetBool("AutoStart", false);

        // 设备型号选择
        _allModelNames = DeviceCapabilities.GetModelNames();
        _brandTree = BuildBrandTree(_allModelNames);

        CbBrand.ItemsSource = _brandList;
        CbSeries.ItemsSource = _seriesList;
        CbModel.ItemsSource = _modelList;

        _brandList.Add("自动检测");
        foreach (var brand in _brandTree.Keys.OrderBy(b => b)) _brandList.Add(brand);

        _modelOverride = SettingsManager.GetString("ModelOverride");
        if (string.IsNullOrEmpty(_modelOverride))
        {
            CbBrand.SelectedItem = "自动检测";
        }
        else
        {
            var (brand, series) = FindBrandSeries(_modelOverride, _brandTree);
            CbBrand.SelectedItem = brand ?? "自动检测";
            if (brand != null)
            {
                _seriesList.Clear();
                _seriesList.Add("（全部子系列）");
                foreach (var s in _brandTree[brand].Keys.OrderBy(s => s)) _seriesList.Add(s);
                CbSeries.SelectedItem = series ?? "（全部子系列）";
                _modelList.Clear();
                _modelList.Add("（全部机型）");
                foreach (var m in _brandTree[brand][series ?? _brandTree[brand].Keys.First()].OrderBy(m => m))
                    _modelList.Add(m);
                CbModel.SelectedItem = _modelOverride;
            }
        }

        _gameModeCompat = SettingsManager.GetBool("GameModeCompat", false);
        CbGameMode.SelectedIndex = _gameModeCompat ? 1 : 0;

        var customName = SettingsManager.GetString("CustomName");
        TbCustomName.Text = customName ?? "";
        UpdateTitle();

        _rfcomm.StateChanged += OnStateChanged;
        _rfcomm.StateChanged += () => Dispatcher.UIThread.Post(() => SyncMultiDeviceList());
        Closing += OnWindowClosing;
        Closed += (_, _) => { _pollCts?.Cancel(); _rfcomm.Dispose(); };
        _ = ConnectAsync();
        }
        catch (Exception ex)
        {
            Log.Ex("UI", "MainWindow 构造", ex);
            throw;
        }
    }

    private async Task ConnectAsync()
    {
        while (!_realClose)
        {
            Log.D("UI", "ConnectAsync: 尝试连接");
            await _rfcomm.ConnectAsync();
            if (_rfcomm.IsConnected)
            {
                Log.D("UI", "ConnectAsync: 已连接,进入轮询");
                _pollCts = new CancellationTokenSource();
                await _rfcomm.PollAsync(_pollCts.Token);
                Log.D("UI", "ConnectAsync: 轮询结束");
            }
            else
            {
                Log.D("UI", "ConnectAsync: 连接失败 -> " + (_rfcomm.LastError ?? "unknown"));
            }
            _ = Dispatcher.UIThread.InvokeAsync(() => OnStateChanged());
            if (!_realClose) { Log.D("UI", "ConnectAsync: 5s 后重试"); await Task.Delay(5000); }
        }
    }

    private void OnStateChanged() => Dispatcher.UIThread.Post(() =>
    {
        try
        {
            var s = _rfcomm.State;
            var caps = _modelOverride != null
                ? DeviceCapabilities.ForceModel(_modelOverride)
                : _rfcomm.Caps;

            if (s.Connected)
        {
            StatusDot.Fill = BrushGreen;
            StatusText.Text = caps.IsSupported
                ? $"已连接 — {caps.ModelName}"
                : $"已连接 — {caps.ModelName}（此型号可能未完整适配）";
            StatusText.Foreground = BrushLightGreen;
            BtnReconnect.IsVisible = false;

            if (!_wasConnected && s.Battery.Count > 0)
            {
                _wasConnected = true;
                _ = ToastWindow.ShowAsync(s, caps.ModelName);
            }
        }
        else
        {
            var wasConnected = _wasConnected;
            _wasConnected = false;
            _lowBatteryAlerted = false;
            _criticalBatteryAlerted = false;

            StatusDot.Fill = BrushRed;
            var err = _rfcomm.LastError;
            StatusText.Text = err ?? "未连接";
            StatusText.Foreground = BrushLightRed;
            BtnReconnect.IsVisible = true;
            // TrayIcon.SetIcon(this, _iconDisconnected); // 托盘图标切换在 SetupTrayIcon 中处理

            if (wasConnected)
                _ = ToastWindow.ShowDisconnectedAsync(caps.ModelName);

            ResetUi();
            return;
        }

        if (CbEq.ItemCount == 0 || (CbEq.SelectedItem is string first && !caps.EqPresets.ContainsKey(first)))
        {
            CbEq.SelectionChanged -= CbEq_SelectionChanged;
            CbEq.Items.Clear();
            foreach (var kv in caps.EqPresets) CbEq.Items.Add(kv.Key);
            CbEq.SelectionChanged += CbEq_SelectionChanged;
        }

        var batL = MergeCharge(s.Battery.GetValueOrDefault("L"), s.WearingL);
        var batR = MergeCharge(s.Battery.GetValueOrDefault("R"), s.WearingR);
        SetBat(LeftBar, LeftLabel, batL);
        SetBat(RightBar, RightLabel, batR);
        SetBat(CaseBar, CaseLabel, s.Battery.GetValueOrDefault("C"));

        if (!_lowBatteryAlerted)
        {
            if ((batL is { } l && l.Lvl <= 20) || (batR is { } r && r.Lvl <= 20))
            {
                _lowBatteryAlerted = true;
                _ = ToastWindow.ShowLowBatteryAsync(s, caps.ModelName);
            }
        }

        if (!_criticalBatteryAlerted)
        {
            if ((batL is { } l && l.Lvl <= 10) || (batR is { } r && r.Lvl <= 10))
            {
                _criticalBatteryAlerted = true;
                _ = ToastWindow.ShowCriticalBatteryAsync(s, caps.ModelName);
            }
        }

        var parts = new List<string>();
        if (batL is { } lb) parts.Add($"L:{lb.Lvl}%{(lb.Chg ? "⚡" : "")}");
        if (batR is { } rb) parts.Add($"R:{rb.Lvl}%{(rb.Chg ? "⚡" : "")}");
        if (s.Battery.GetValueOrDefault("C") is { } cb) parts.Add($"C:{cb.Level}%{(cb.Charging ? "⚡" : "")}");
        UpdateTrayTooltip(parts.Count > 0 ? $"{caps.ModelName}\n{string.Join(" ", parts)}" : caps.ModelName);

        // 佩戴状态 - 即使空也显示占位，排查显示问题
        var wearParts = new List<string>();
        if (!string.IsNullOrEmpty(s.WearingL)) wearParts.Add($"左耳{s.WearingL}");
        if (!string.IsNullOrEmpty(s.WearingR)) wearParts.Add($"右耳{s.WearingR}");
        WearStatus.Text = wearParts.Count > 0 ? string.Join("  ", wearParts) : "";
        WearStatus.IsVisible = wearParts.Count > 0;

        if (s.AncMode is not "?" && (DateTime.Now - _ancUserSetAt).TotalSeconds > 3)
        {
            if (s.AncMode is "Off" or "Adaptive" or "Transparency")
            { _ancMain = s.AncMode; AncSub.IsVisible = false; }
            else if (s.AncMode is "Smart" or "Light" or "Medium" or "Deep")
            {
                _ancMain = "Smart";
                if (string.IsNullOrEmpty(_ancLevel)) _ancLevel = s.AncMode;
                AncSub.IsVisible = true;
            }
        }
        Highlight();

        if (s.EqPreset != "?" && CbEq.SelectedItem == null) CbEq.SelectedItem = s.EqPreset;
        if ((DateTime.Now - _featureUserSetAt).TotalSeconds > 3)
        {
            if (s.SpatialSound != _prevSpatialSound) { _prevSpatialSound = s.SpatialSound; CbSpatial.IsChecked = s.SpatialSound; }
            if (s.GameMode != _prevGameMode) { _prevGameMode = s.GameMode; CbGame.IsChecked = s.GameMode; }
            if (s.DualDevice != _prevDualDevice) { _prevDualDevice = s.DualDevice; CbDualDevice.IsChecked = s.DualDevice; }
        }

        SpatialAudioPanel.IsVisible = caps.HasSpatialAudio;
        CbSpatial.IsVisible = caps.HasSpatialSound;
        CbDualDevice.IsVisible = caps.HasDualDevice;
        BtnAdaptive.IsVisible = caps.HasAdaptiveAnc;
        CbGame.IsVisible = caps.HasGameMode;

        ModelNote.Text = $"当前自动识别: {caps.ModelName}";
        UpdateTitle();
        }
        catch (Exception ex)
        {
            Log.Ex("UI", "OnStateChanged", ex);
        }
    });

    private static void SetBat(ProgressBar b, TextBlock l, (int Lvl, bool Chg)? d)
    {
        if (d is not { } v) { b.Value = 0; l.Text = "-%"; return; }
        b.Value = v.Lvl; l.Text = $"{v.Lvl}%{(v.Chg ? " ⚡" : "")}";
    }

    private static (int Lvl, bool Chg)? MergeCharge((int Lvl, bool Chg)? bat, string wear) =>
        bat is { } b ? (b.Lvl, b.Chg || wear == "入盒") : null;

    private void CbTray_Changed(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SettingsManager.SetBool("TrayEnabled", CbTray.IsChecked == true);
    }

    private void CbAuto_Changed(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (CbAuto.IsChecked == true)
                SettingsManager.SetString("AutoStartPath", Environment.ProcessPath);
            else
                SettingsManager.SetString("AutoStartPath", null);
        }
        catch { }
    }

    private void Highlight()
    {
        var btns = new[] {
            (BtnSmart, BgSmart, "Smart"), (BtnAdaptive, BgAdaptive, "Adaptive"),
            (BtnTrans, BgTrans, "Transparency"), (BtnOff, BgOff, "Off") };
        foreach (var (btn, bg, tag) in btns)
        {
            var active = !string.IsNullOrEmpty(_ancMain) && tag == _ancMain;
            bg.Background = active ? BrushAccent : BrushTransparent;
            btn.Foreground = active ? BrushWhite : BrushGray;
        }

        var subBtns = new[] {
            (BtnAncSmart, BgAncSmart, "Smart"), (BtnAncLight, BgAncLight, "Light"),
            (BtnAncMedium, BgAncMedium, "Medium"), (BtnAncDeep, BgAncDeep, "Deep") };
        foreach (var (btn, bg, tag) in subBtns)
        {
            var active = !string.IsNullOrEmpty(_ancLevel) && tag == _ancLevel;
            bg.Background = active ? BrushAccent : BrushTransparent;
            btn.Foreground = active ? BrushWhite : BrushGray;
        }
    }

    private void SwitchAncMain(string mode)
    {
        if (!_rfcomm.IsConnected) return;
        Log.D("UI", $"用户操作: ANC 主模式 -> {mode}");
        _ancUserSetAt = DateTime.Now;
        _ancMain = mode;
        AncSub.IsVisible = (mode == "Smart");
        if (mode != "Smart") _ancLevel = "";
        _rfcomm.SendAnc(mode);
        Highlight();
    }

    private void SwitchAncSub(string mode)
    {
        if (!_rfcomm.IsConnected) return;
        Log.D("UI", $"用户操作: ANC 子级别 -> {mode}");
        _ancUserSetAt = DateTime.Now;
        _ancLevel = mode;
        _rfcomm.SendAnc(mode);
        Highlight();
    }

    private void AncMain_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (s is Button btn && btn.Tag is string tag) SwitchAncMain(tag);
    }

    private void AncSub_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (s is Button btn && btn.Tag is string tag) SwitchAncSub(tag);
    }

    private void SpatialAudio_Changed(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (s is RadioButton rb && rb.Tag is string mode && _rfcomm.IsConnected)
        {
            Log.D("UI", $"用户操作: 空间音频 -> {mode}");
            _rfcomm.SendSpatialAudio(mode);
        }
    }

    private void CbSpatial_Changed(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (CbSpatial.IsChecked is { } on && _rfcomm.IsConnected)
        {
            Log.D("UI", $"用户操作: 空间声场开关 -> {on}");
            _featureUserSetAt = DateTime.Now;
            _rfcomm.SendSpatial(on);
        }
    }

    private void CbGame_Changed(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (CbGame.IsChecked is { } on && _rfcomm.IsConnected)
        {
            Log.D("UI", $"用户操作: 游戏模式开关 -> {on}");
            _featureUserSetAt = DateTime.Now;
            _rfcomm.SendGameMode(on, _gameModeCompat);
        }
    }

    private void CbDualDevice_Changed(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (CbDualDevice.IsChecked is { } on && _rfcomm.IsConnected)
        {
            Log.D("UI", $"用户操作: 双设备开关 -> {on}");
            _featureUserSetAt = DateTime.Now;
            _rfcomm.SendDualDevice(on);
        }
    }

    private void CbEq_SelectionChanged(object? s, SelectionChangedEventArgs e)
    {
        if (CbEq.SelectedItem is string name && _rfcomm.IsConnected)
        {
            Log.D("UI", $"用户操作: EQ 预设 -> {name}");
            _rfcomm.SendEq(name);
        }
    }

    private void CbBrand_Changed(object? s, SelectionChangedEventArgs e)
    {
        if (CbBrand.SelectedItem is not string brand || brand == "自动检测")
        {
            _seriesList.Clear();
            _modelList.Clear();
            return;
        }
        if (!_brandTree.TryGetValue(brand, out var series)) return;

        _seriesList.Clear();
        _seriesList.Add("（全部子系列）");
        foreach (var sn in series.Keys.OrderBy(x => x)) _seriesList.Add(sn);
        CbSeries.SelectedItem = "（全部子系列）";
    }

    private void CbSeries_Changed(object? s, SelectionChangedEventArgs e)
    {
        if (CbBrand.SelectedItem is not string brand || !_brandTree.TryGetValue(brand, out var series)) return;
        var sn = CbSeries.SelectedItem as string ?? "（全部子系列）";

        _modelList.Clear();
        _modelList.Add("（全部机型）");
        var models = sn == "（全部子系列）"
            ? series.SelectMany(kv => kv.Value).Distinct().OrderBy(x => x).ToList()
            : series.TryGetValue(sn, out var list) ? list.OrderBy(x => x).ToList() : new();
        foreach (var m in models) _modelList.Add(m);
        CbModel.SelectedItem = "（全部机型）";
    }

    private void CbModel_Changed(object? s, SelectionChangedEventArgs e)
    {
        if (CbModel.SelectedItem is string model && model != "（全部机型）")
        {
            Log.D("UI", $"用户操作: 手动指定机型 -> {model}");
            _modelOverride = model;
            SettingsManager.SetString("ModelOverride", model);
            SyncCaps();
        }
        else
        {
            _modelOverride = null;
            SettingsManager.SetString("ModelOverride", null);
            SyncCaps();
        }
    }

    private void SyncCaps()
    {
        var caps = _modelOverride != null
            ? DeviceCapabilities.ForceModel(_modelOverride)
            : _rfcomm.Caps;

        CbEq.SelectionChanged -= CbEq_SelectionChanged;
        CbEq.Items.Clear();
        foreach (var kv in caps.EqPresets) CbEq.Items.Add(kv.Key);
        CbEq.SelectionChanged += CbEq_SelectionChanged;

        SpatialAudioPanel.IsVisible = caps.HasSpatialAudio;
        CbSpatial.IsVisible = caps.HasSpatialSound;
        CbDualDevice.IsVisible = caps.HasDualDevice;
        BtnAdaptive.IsVisible = caps.HasAdaptiveAnc;
        CbGame.IsVisible = caps.HasGameMode;

        ModelNote.Text = _modelOverride == null
            ? $"当前自动识别: {_rfcomm.Caps.ModelName}"
            : $"已手动指定: {caps.ModelName}";

        UpdateTitle();
        if (_rfcomm.State.Connected)
        {
            StatusText.Text = $"已连接 — {caps.ModelName}";
            StatusText.Foreground = BrushLightGreen;
        }
    }

    private void CbGameMode_Changed(object? s, SelectionChangedEventArgs e)
    {
        _gameModeCompat = CbGameMode.SelectedIndex == 1;
        SettingsManager.SetBool("GameModeCompat", _gameModeCompat);
    }

    private void CbTheme_Changed(object? s, SelectionChangedEventArgs e)
    {
        var idx = CbTheme.SelectedIndex;
        Log.D("UI", $"用户操作: 切换主题 -> {idx}");
        ApplyTheme(idx);
        SettingsManager.SetInt("Theme", idx);
    }

    private static void ApplyTheme(int index)
    {
        var theme = SukiTheme.GetInstance();
        switch (index)
        {
            case 0:
                theme.ChangeBaseTheme(Avalonia.Styling.ThemeVariant.Default);
                break;
            case 1:
                theme.ChangeBaseTheme(Avalonia.Styling.ThemeVariant.Dark);
                break;
            case 2:
                theme.ChangeBaseTheme(Avalonia.Styling.ThemeVariant.Light);
                break;
        }
    }

    private void TbCustomName_Changed(object? s, TextChangedEventArgs e)
    {
        SettingsManager.SetString("CustomName",
            string.IsNullOrWhiteSpace(TbCustomName.Text) ? null : TbCustomName.Text.Trim());
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        var caps = _modelOverride != null
            ? DeviceCapabilities.ForceModel(_modelOverride)
            : _rfcomm.Caps;
        var custom = (TbCustomName.Text ?? "").Trim();
        var name = !string.IsNullOrEmpty(custom) ? custom : caps.ModelName;
        // TitleText.Text = name; // removed with custom title bar; system title bar shows Title
        Title = name;

        var s = _rfcomm.State;
        var parts = new List<string>();
        var bl = MergeCharge(s.Battery.GetValueOrDefault("L"), s.WearingL);
        var br = MergeCharge(s.Battery.GetValueOrDefault("R"), s.WearingR);
        if (bl is { } lb) parts.Add($"L:{lb.Lvl}%{(lb.Chg ? "⚡" : "")}");
        if (br is { } rb) parts.Add($"R:{rb.Lvl}%{(rb.Chg ? "⚡" : "")}");
        if (s.Battery.GetValueOrDefault("C") is { } cb) parts.Add($"C:{cb.Level}%{(cb.Charging ? "⚡" : "")}");
        UpdateTrayTooltip(parts.Count > 0 ? $"{name}\n{string.Join(" ", parts)}" : name);
    }

    private void Settings_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var showSettings = !SettingsPanel.IsVisible;
        MainPanel.IsVisible = !showSettings;
        SettingsPanel.IsVisible = showSettings;
        AboutPanel.IsVisible = false;
    }

    private void About_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var showAbout = !AboutPanel.IsVisible;
        MainPanel.IsVisible = !showAbout;
        AboutPanel.IsVisible = showAbout;
        SettingsPanel.IsVisible = false;
    }

    private void OpenUrl_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (s is Button btn && btn.Tag is string url)
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void Reconnect_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Log.D("UI", "用户操作: 点击重连");
        _rfcomm.Disconnect();
    }

    private void ResetUi()
    {
        SetBat(LeftBar, LeftLabel, null);
        SetBat(RightBar, RightLabel, null);
        SetBat(CaseBar, CaseLabel, null);
        WearStatus.Text = "";
        AncSub.IsVisible = false;
        CbSpatial.IsChecked = false;
        CbGame.IsChecked = false;
        DeviceList.Items.Clear();
    }

    private void DeviceExpander_Expanded(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_rfcomm.IsConnected)
        {
            Log.D("UI", "用户操作: 展开多设备列表");
            _rfcomm.SendMultiConnectInfo();
        }
    }

    private void OnWindowClosing(object? s, WindowClosingEventArgs e)
    {
        if (_realClose || CbTray.IsChecked != true) return;
        e.Cancel = true;
        Hide();
    }

    // ===== 设备列表 =====
    private void SyncMultiDeviceList()
    {
        DeviceList.Items.Clear();

        var all = _rfcomm.State.ConnectedDevices
            .Where(d => IsHeadphoneDevice(d)).ToList();
        
        // 更新标题文字
        var current = all.FirstOrDefault(d => d.IsCurrentDevice);
        DeviceHeaderText.Text = current != null
            ? $"{current.DeviceName} — {current.ConnectionStatus}"
            : (_rfcomm.State.Connected ? "已连接" : "未连接");

        if (all.Count == 0 && _rfcomm.State.Connected)
        {
            var caps = _modelOverride != null
                ? DeviceCapabilities.ForceModel(_modelOverride)
                : _rfcomm.Caps;
            all.Add(new ConnectedDeviceInfo
            {
                Address = "current",
                DeviceName = caps.ModelName,
                ConnectionState = 2,
                IsCurrentDevice = true,
            });
            DeviceHeaderText.Text = $"{caps.ModelName} — 已连接";
        }

        foreach (var d in all)
        {
            var dot = new Ellipse { Width = 10, Height = 10, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            dot.Fill = d.ConnectionState == 2 ? BrushGreen : BrushRed;

            var nameTb = new TextBlock { Text = d.DeviceName, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, FontSize = 12 };
            if (d.IsCurrentDevice) nameTb.Foreground = BrushLightGreen;

            var statusTb = new TextBlock { Text = d.ConnectionStatus, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, FontSize = 11, Opacity = 0.6 };

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("12,*,Auto") };
            grid.Children.Add(dot);
            Grid.SetColumn(nameTb, 1);
            grid.Children.Add(nameTb);
            Grid.SetColumn(statusTb, 2);
            grid.Children.Add(statusTb);

            var border = new Border { Padding = new Thickness(8, 6), Margin = new Thickness(0, 1), CornerRadius = new CornerRadius(4), Child = grid };
            if (d.IsCurrentDevice)
                border.Background = new SolidColorBrush(Color.FromArgb(0x15, 0x4C, 0xAF, 0x50));

            DeviceList.Items.Add(border);
        }
    }

    private static readonly string[] NonHeadphoneNamePrefixes = new[]
    {
        "DESKTOP-", "LAPTOP-",
        "iPhone", "iPad",
        "Galaxy ",
        "Xiaomi ", "Redmi ", "Mi ",
        "HUAWEI ", "HONOR ",
        "Pixel",
    };

    private static bool IsHeadphoneDevice(ConnectedDeviceInfo d)
    {
        var name = d.DeviceName ?? "";
        if (string.IsNullOrEmpty(name)) return false;
        foreach (var p in NonHeadphoneNamePrefixes)
            if (name.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ToggleFromTray()
    {
        if (IsVisible)
            Hide();
        else
            ShowFromTray();
    }

    private void UpdateTrayTooltip(string text)
    {
        if (_trayIcon != null)
            _trayIcon.ToolTipText = text;
    }

    private void SetupTrayIcon()
    {
        try
        {
            _trayIcon = new TrayIcon
            {
                Icon = _iconConnected ?? _iconDisconnected,
                ToolTipText = "OPPO Pods",
                IsVisible = true
            };
            var menu = new NativeMenu();
            var showItem = new NativeMenuItem("显示主页面");
            showItem.Click += (_, _) => ShowFromTray();
            menu.Add(showItem);
            menu.Add(new NativeMenuItemSeparator());
            var quitItem = new NativeMenuItem("退出");
            quitItem.Click += (_, _) => { _realClose = true; Environment.Exit(0); };
            menu.Add(quitItem);
            _trayIcon.Menu = menu;
            _trayIcon.Clicked += (_, _) => ToggleFromTray();

            var icons = new TrayIcons { _trayIcon };
            if (Application.Current != null)
                TrayIcon.SetIcons(Application.Current, icons);
        }
        catch (Exception ex)
        {
            Log.Ex("UI", "SetupTrayIcon", ex);
        }
    }

    private void SyncUi() => Dispatcher.UIThread.Post(() => OnStateChanged());

    private void SpatialAudio_Init()
    {
        // Wire RadioButton IsCheckedChanged events for spatial audio group
        if (SpatialAudioPanel.Child is StackPanel sp)
            foreach (var child in sp.Children)
                if (child is WrapPanel wp)
                    foreach (var c in wp.Children)
                        if (c is RadioButton rb)
                            rb.IsCheckedChanged += SpatialAudio_Changed;
    }

    // ========== 品牌/系列/机型树构建 ==========

    private static Dictionary<string, Dictionary<string, List<string>>> BuildBrandTree(List<string> allNames)
    {
        var tree = new Dictionary<string, Dictionary<string, List<string>>>();
        var brandKeywords = new Dictionary<string, string[]>
        {
            ["OPPO"] = new[] { "OPPO", "Enco" },
            ["OnePlus"] = new[] { "OnePlus", "Buds" },
            ["realme"] = new[] { "realme", "Buds" },
        };

        foreach (var name in allNames)
        {
            string brand = "其他";
            foreach (var (b, keywords) in brandKeywords)
                if (keywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase)))
                { brand = b; break; }

            if (!tree.ContainsKey(brand))
                tree[brand] = new Dictionary<string, List<string>>();

            var series = ExtractSeries(name);
            if (!tree[brand].ContainsKey(series))
                tree[brand][series] = new List<string>();
            tree[brand][series].Add(name);
        }
        return tree;
    }

    private static string ExtractSeries(string modelName)
    {
        // 从型号名提取子系列名（型号名中的第一个单词/数字+字母组合）
        var parts = modelName.Split(' ', '-', '(', '（');
        return parts.Length > 0 ? parts[0] : modelName;
    }

    private static (string? brand, string? series) FindBrandSeries(string modelName, Dictionary<string, Dictionary<string, List<string>>> tree)
    {
        foreach (var (brand, series) in tree)
            foreach (var (sn, models) in series)
                if (models.Contains(modelName))
                    return (brand, sn);
        return (null, null);
    }
}
