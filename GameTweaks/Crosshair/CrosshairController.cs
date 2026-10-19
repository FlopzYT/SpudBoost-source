using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using GameTweaks.Core;

namespace GameTweaks.Crosshair;

/// <summary>
/// Bindable crosshair settings plus the overlay itself. Shown while enabled and (by default) only while Fortnite is
/// the window in front. While in game it reads (never sends) your key presses: a weapon's keys switch to that weapon's
/// crosshair, right mouse can hide it for scopes, and left mouse can spread it like bloom.
/// </summary>
public sealed class CrosshairController : ObservableObject
{
    const string Fortnite = "FortniteClient-Win64-Shipping";
    const int VkLeftButton = 0x01, VkRightButton = 0x02;

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);

    readonly AppSettings _settings;
    // ~60 checks a second so switching feels instant; each one is a handful of cheap Windows calls.
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    CrosshairWindow? _window;
    uint _lastForegroundPid;
    bool _foregroundIsFortnite;
    int _ticks;

    // Live state while playing
    Dictionary<int, CrosshairLook> _keyMap = new();
    readonly HashSet<int> _down = new();
    CrosshairLook? _active;
    bool _aiming, _firing, _drawnAiming, _drawnFiring;
    CrosshairLook? _drawnLook;

    /// <summary>Raised after any change, so the preview can redraw.</summary>
    public event Action? Changed;

    public CrosshairController(AppSettings settings)
    {
        _settings = settings;
        RebuildKeyMap();
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    CrosshairSettings S => _settings.Crosshair;

    // ---- Which crosshair is being edited on the page ----

    public List<string> EditTargets => ["Default", .. S.Weapons.Select(w => w.Name)];

    int _editIndex;
    public int EditIndex
    {
        get => _editIndex;
        set
        {
            if (!SetField(ref _editIndex, Math.Clamp(value, 0, S.Weapons.Count))) return;
            foreach (var name in LookProperties) OnPropertyChanged(name);
            Changed?.Invoke();
        }
    }

    static readonly string[] LookProperties =
        [nameof(Style), nameof(Color), nameof(Size), nameof(Thickness), nameof(Gap), nameof(Opacity), nameof(Outline), nameof(CenterDot),
         nameof(Keys), nameof(IsWeapon), nameof(WeaponEnabled)];

    /// <summary>The look being edited: the default crosshair or one of the weapons.</summary>
    public CrosshairLook Edited => _editIndex == 0 ? S : S.Weapons[_editIndex - 1];
    WeaponCrosshair? EditedWeapon => _editIndex == 0 ? null : S.Weapons[_editIndex - 1];

    public bool IsWeapon => EditedWeapon != null;

    // ---- Look of the edited crosshair ----

    public string Style { get => Edited.Style; set => Set(() => Edited.Style = value, Edited.Style != value); }
    public string Color { get => Edited.Color; set => Set(() => Edited.Color = value, Edited.Color != value); }
    public double Size { get => Edited.Size; set => Set(() => Edited.Size = Math.Round(value), Edited.Size != Math.Round(value)); }
    public double Thickness { get => Edited.Thickness; set => Set(() => Edited.Thickness = Math.Round(value), Edited.Thickness != Math.Round(value)); }
    public double Gap { get => Edited.Gap; set => Set(() => Edited.Gap = Math.Round(value), Edited.Gap != Math.Round(value)); }
    public double Opacity { get => Edited.Opacity; set => Set(() => Edited.Opacity = Math.Round(value, 2), Edited.Opacity != Math.Round(value, 2)); }
    public bool Outline { get => Edited.Outline; set => Set(() => Edited.Outline = value, Edited.Outline != value); }
    public bool CenterDot { get => Edited.CenterDot; set => Set(() => Edited.CenterDot = value, Edited.CenterDot != value); }

    /// <summary>Keys that switch to the edited crosshair.</summary>
    public string Keys
    {
        get => EditedWeapon?.Keys ?? S.DefaultKeys;
        set
        {
            if (EditedWeapon is { } w) Set(() => w.Keys = value, w.Keys != value);
            else Set(() => S.DefaultKeys = value, S.DefaultKeys != value);
        }
    }

    public bool WeaponEnabled
    {
        get => EditedWeapon?.Enabled ?? true;
        set { if (EditedWeapon is { } w) Set(() => w.Enabled = value, w.Enabled != value); }
    }

    // ---- Overall behaviour ----

    public bool Enabled { get => S.Enabled; set => Set(() => S.Enabled = value, S.Enabled != value); }
    public bool OnlyInGame { get => S.OnlyInGame; set => Set(() => S.OnlyInGame = value, S.OnlyInGame != value); }
    public bool HideWhileAiming { get => S.HideWhileAiming; set => Set(() => S.HideWhileAiming = value, S.HideWhileAiming != value); }
    public double FireSpread { get => S.FireSpread; set => Set(() => S.FireSpread = Math.Round(value), S.FireSpread != Math.Round(value)); }
    public double OffsetX { get => S.OffsetX; set => Set(() => S.OffsetX = Math.Round(value), S.OffsetX != Math.Round(value)); }
    public double OffsetY { get => S.OffsetY; set => Set(() => S.OffsetY = Math.Round(value), S.OffsetY != Math.Round(value)); }

    string _status = "";
    public string Status { get => _status; private set => SetField(ref _status, value); }

    void Set(Action apply, bool changed, [CallerMemberName] string? name = null)
    {
        if (!changed) return;
        apply();
        _settings.Save();
        OnPropertyChanged(name);
        RebuildKeyMap();
        _drawnLook = null; // force a redraw with the new settings
        Changed?.Invoke();
    }

    public void ResetPosition()
    {
        OffsetX = 0;
        OffsetY = 0;
    }

    /// <summary>Key -> crosshair. Weapons win if a key is listed twice; disabled weapons are ignored.</summary>
    void RebuildKeyMap()
    {
        var map = new Dictionary<int, CrosshairLook>();
        foreach (var vk in KeyList.Parse(S.DefaultKeys)) map[vk] = S;
        foreach (var weapon in S.Weapons.Where(w => w.Enabled))
            foreach (var vk in KeyList.Parse(weapon.Keys)) map[vk] = weapon;
        _keyMap = map;
        if (_active is WeaponCrosshair { Enabled: false }) _active = S;
    }

    /// <summary>True when the window in front belongs to Fortnite. The process is only looked up when focus changes.</summary>
    bool FortniteInFront()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        if (pid != _lastForegroundPid)
        {
            _lastForegroundPid = pid;
            try { _foregroundIsFortnite = pid != 0 && Process.GetProcessById((int)pid).ProcessName == Fortnite; }
            catch (ArgumentException) { _foregroundIsFortnite = false; }
        }
        return _foregroundIsFortnite;
    }

    static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    void Tick()
    {
        var inFront = FortniteInFront();
        var show = S.Enabled && (!S.OnlyInGame || inFront);

        if (!show)
        {
            if (_window is { IsVisible: true }) _window.Hide();
            _down.Clear();
            UpdateStatus(inFront);
            return;
        }

        // Read input only while Fortnite is in front, so typing elsewhere never switches crosshairs.
        if (inFront) ReadInput();
        else _aiming = _firing = false;

        if (_window == null) _window = new CrosshairWindow();
        if (!_window.IsVisible)
        {
            _window.Show();
            _drawnLook = null;
        }
        if (++_ticks % 60 == 0) _window.KeepOnTop();

        var look = _active ?? S;
        if (look != _drawnLook || _firing != _drawnFiring)
        {
            _window.Render(look, S.OffsetX, S.OffsetY, _firing ? S.FireSpread : 0);
            _drawnLook = look;
            _drawnFiring = _firing;
        }
        var hidden = _aiming && S.HideWhileAiming;
        if (hidden != _drawnAiming)
        {
            _window.SetBlank(hidden);
            _drawnAiming = hidden;
        }
        UpdateStatus(inFront);
    }

    void ReadInput()
    {
        foreach (var (vk, look) in _keyMap)
        {
            var down = IsDown(vk);
            if (down && _down.Add(vk)) _active = look; // switch on the key press, not while held
            else if (!down) _down.Remove(vk);
        }
        _aiming = IsDown(VkRightButton);
        _firing = S.FireSpread > 0 && IsDown(VkLeftButton);
    }

    void UpdateStatus(bool inFront)
    {
        var name = (_active as WeaponCrosshair)?.Name ?? "Default";
        Status = !S.Enabled ? "Crosshair is off."
            : S.OnlyInGame && !inFront ? "Will show when Fortnite is the window in front."
            : $"Showing: {name} crosshair{(inFront ? " (switches with your weapon keys)" : "")}.";
    }

    public void Shutdown()
    {
        _timer.Stop();
        _window?.Close();
        _window = null;
    }
}
