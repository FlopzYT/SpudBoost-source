using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using GameTweaks.Core;
using GameTweaks.Crosshair;

namespace GameTweaks.Overlay;

/// <summary>Small click-through readout drawn in a screen corner on top of the game.</summary>
public sealed class StatsOverlayWindow : Window
{
    const int GwlExStyle = -20;
    const int WsExTransparent = 0x20, WsExToolWindow = 0x80, WsExLayered = 0x80000, WsExNoActivate = 0x08000000;
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    readonly TextBlock _text = new()
    {
        Foreground = Brushes.White,
        FontFamily = new FontFamily("Consolas"),
        FontSize = 13,
        LineHeight = 18,
    };

    public StatsOverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        IsHitTestVisible = false;
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6, 10, 6),
            Child = _text,
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) | WsExTransparent | WsExToolWindow | WsExLayered | WsExNoActivate);
    }

    public void SetLines(string text) => _text.Text = text;

    /// <summary>Places the readout in a corner of the primary screen.</summary>
    public void PlaceIn(string corner)
    {
        UpdateLayout();
        const double margin = 12;
        var screenW = SystemParameters.PrimaryScreenWidth;
        var screenH = SystemParameters.PrimaryScreenHeight;
        Left = corner.Contains("left", StringComparison.OrdinalIgnoreCase) ? margin : screenW - ActualWidth - margin;
        Top = corner.Contains("Bottom", StringComparison.OrdinalIgnoreCase) ? screenH - ActualHeight - margin : margin;
    }

    public void KeepOnTop()
    {
        const uint NoSize = 0x1, NoMove = 0x2, NoActivate = 0x10;
        SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), 0, 0, 0, 0, NoSize | NoMove | NoActivate);
    }
}

/// <summary>
/// Shows GPU temperature and load, CPU and RAM use and live ping on top of every app and game while switched on.
/// Readings refresh every 2 seconds; a hotkey hides it mid-game.
/// </summary>
public sealed class StatsOverlayController : ObservableObject
{
    public static readonly string[] Corners = ["Top left", "Top right", "Bottom left", "Bottom right"];

    readonly AppSettings _settings;
    readonly SystemMonitor _monitor = new();
    readonly DispatcherTimer _fast = new() { Interval = TimeSpan.FromMilliseconds(100) };
    readonly DispatcherTimer _slow = new() { Interval = TimeSpan.FromSeconds(2) };
    StatsOverlayWindow? _window;
    string _ping = "…";
    string _stats = "Loading stats…";
    readonly LiveFps _fps = new();
    bool _downloadingPresentMon;
    int _toggleVk, _ticks;
    bool _toggleDown, _hiddenByHotkey, _updating;

    public StatsOverlayController(AppSettings settings)
    {
        _settings = settings;
        _toggleVk = KeyList.Parse(S.ToggleKey).FirstOrDefault();
        _fast.Tick += (_, _) => Tick();
        _slow.Tick += async (_, _) => await RefreshAsync();
        _fast.Start();
    }

    StatsOverlaySettings S => _settings.StatsOverlay;

    public bool Enabled { get => S.Enabled; set => Set(() => S.Enabled = value); }
    public string Corner { get => S.Corner; set => Set(() => S.Corner = value); }
    public string PingRegion { get => S.PingRegion; set => Set(() => S.PingRegion = value); }
    public string ToggleKey
    {
        get => S.ToggleKey;
        set => Set(() => { S.ToggleKey = value.Trim(); _toggleVk = KeyList.Parse(S.ToggleKey).FirstOrDefault(); });
    }

    void Set(Action apply, [CallerMemberName] string? name = null)
    {
        apply();
        _settings.Save();
        OnPropertyChanged(name);
        _window?.PlaceIn(S.Corner);
    }

    void Tick()
    {
        // Shown over every app and game while switched on; the hotkey hides and shows it anywhere.
        if (S.Enabled && _toggleVk != 0)
        {
            var down = ForegroundGame.IsDown(_toggleVk);
            if (down && !_toggleDown) _hiddenByHotkey = !_hiddenByHotkey;
            _toggleDown = down;
        }

        var show = S.Enabled && !_hiddenByHotkey;
        if (show)
        {
            if (_window == null)
            {
                _window = new StatsOverlayWindow();
                _window.SetLines("Loading stats…");
            }
            if (!_window.IsVisible)
            {
                _window.Show();
                _window.PlaceIn(S.Corner);
                _slow.Start();
                StartFps();
                _ = RefreshAsync();
            }
            if (++_ticks % 10 == 0) { _window.KeepOnTop(); ShowLines(); } // about once a second

        }
        else if (_window is { IsVisible: true })
        {
            _window.Hide();
            _slow.Stop(); // no readings (and no nvidia-smi calls) while hidden
            _fps.Stop();
        }
    }

    async Task RefreshAsync()
    {
        if (_window == null || _updating) return;
        _updating = true;
        try
        {
            await Task.WhenAll(_monitor.UpdateAsync(), PingAsync());
            var gpu = SystemMonitor.HasNvidiaSmi ? $"GPU  {_monitor.GpuTemp,-6}  {_monitor.Gpu,4}\n" : "";
            _stats = $"{gpu}CPU  {_monitor.Cpu,4}   RAM {_monitor.Ram,4}\nPING {_ping} ({S.PingRegion})";
            ShowLines();
        }
        finally { _updating = false; }
    }

    /// <summary>FPS line (refreshed every second) on top of the slower stats.</summary>
    void ShowLines()
    {
        if (_window == null) return;
        string fps;
        if (_fps.ForegroundFps() is { } live) fps = $"FPS  {live.Fps,4:0}   1% LOW {live.Low1:0}";
        else if (_downloadingPresentMon) fps = "FPS  getting PresentMon…";
        else if (_fps.Problem != null) fps = $"FPS  ({_fps.Problem})";
        else fps = "FPS  –";
        _window.SetLines($"{fps}\n{_stats}");
        _window.PlaceIn(S.Corner);
    }

    /// <summary>Starts live FPS, downloading PresentMon the first time if needed.</summary>
    async void StartFps()
    {
        if (!Benchmark.Installed && Shell.IsAdmin && !_downloadingPresentMon)
        {
            _downloadingPresentMon = true;
            try { await Benchmark.DownloadAsync(); }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or System.IO.IOException) { }
            finally { _downloadingPresentMon = false; }
        }
        if (_window is { IsVisible: true }) _fps.Start();
    }

    async Task PingAsync()
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(PingTester.HostFor(S.PingRegion), 1000);
            _ping = reply.Status == IPStatus.Success ? $"{reply.RoundtripTime} ms" : "timeout";
        }
        catch (PingException) { _ping = "n/a"; }
    }

    public void Shutdown()
    {
        _fast.Stop();
        _slow.Stop();
        _fps.Stop();
        _window?.Close();
        _window = null;
    }
}
