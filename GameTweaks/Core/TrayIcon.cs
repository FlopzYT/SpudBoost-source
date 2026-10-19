using System.Drawing;
using Forms = System.Windows.Forms;

namespace GameTweaks.Core;

/// <summary>The orange G next to the clock: reopen the window, toggle Game Boost and the crosshair, or exit.</summary>
public sealed class TrayIcon : IDisposable
{
    readonly Forms.NotifyIcon _icon;
    readonly Forms.ToolStripMenuItem _boostItem;
    readonly Forms.ToolStripMenuItem _crosshairItem;

    public event Action? OpenRequested;
    public event Action? ExitRequested;
    public event Action<bool>? BoostToggled;
    public event Action<bool>? CrosshairToggled;

    public TrayIcon()
    {
        _boostItem = new Forms.ToolStripMenuItem("Game Boost") { CheckOnClick = true };
        _boostItem.CheckedChanged += (_, _) => { if (!_updating) BoostToggled?.Invoke(_boostItem.Checked); };
        _crosshairItem = new Forms.ToolStripMenuItem("Crosshair") { CheckOnClick = true };
        _crosshairItem.CheckedChanged += (_, _) => { if (!_updating) CrosshairToggled?.Invoke(_crosshairItem.Checked); };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open SpudBoost", null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add(_boostItem);
        menu.Items.Add(_crosshairItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());

        _icon = new Forms.NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application,
            Text = "SpudBoost",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
        // Clicking a notification (e.g. "update available") opens the app.
        _icon.BalloonTipClicked += (_, _) => OpenRequested?.Invoke();
    }

    bool _updating;

    /// <summary>Updates the menu checkmarks without raising the toggle events.</summary>
    public void SetStates(bool boost, bool crosshair)
    {
        _updating = true;
        _boostItem.Checked = boost;
        _crosshairItem.Checked = crosshair;
        _updating = false;
    }

    public void ShowHint(string text) => _icon.ShowBalloonTip(4000, "SpudBoost", text, Forms.ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
