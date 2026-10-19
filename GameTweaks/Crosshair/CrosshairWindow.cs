using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using GameTweaks.Core;

namespace GameTweaks.Crosshair;

/// <summary>
/// A transparent, click-through, always-on-top window covering the primary screen, with the crosshair at its centre.
/// It never touches the game; it only draws on top of it (works in windowed/borderless fullscreen).
/// </summary>
public sealed class CrosshairWindow : Window
{
    const int GwlExStyle = -20;
    const int WsExTransparent = 0x20, WsExToolWindow = 0x80, WsExLayered = 0x80000, WsExNoActivate = 0x08000000;

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    readonly Canvas _canvas = new();

    public CrosshairWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;
        Content = _canvas;
        IsHitTestVisible = false;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Clicks and focus pass straight through to the game underneath.
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) | WsExTransparent | WsExToolWindow | WsExLayered | WsExNoActivate);
    }

    public void Render(CrosshairLook look, double offsetX, double offsetY, double extraGap) =>
        CrosshairRenderer.Draw(_canvas, look, Width / 2 + offsetX, Height / 2 + offsetY, extraGap);

    /// <summary>Hides the crosshair without closing the window (e.g. while aiming down a scope).</summary>
    public void SetBlank(bool blank) => _canvas.Visibility = blank ? Visibility.Hidden : Visibility.Visible;

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    /// <summary>Re-asserts top-most (a game going fullscreen can push other windows behind it) without moving or activating.</summary>
    public void KeepOnTop()
    {
        const uint NoSize = 0x1, NoMove = 0x2, NoActivate = 0x10;
        SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), 0, 0, 0, 0, NoSize | NoMove | NoActivate);
    }
}
