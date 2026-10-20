using System.Diagnostics;
using System.Windows.Threading;
using GameTweaks.Core;

namespace GameTweaks.Boost;

/// <summary>
/// Watches for a running game, raises its priority and lowers background apps; everything is put back
/// when the game exits or boosting is turned off.
/// </summary>
public sealed class GameBooster : ObservableObject
{
    readonly AppSettings _settings;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    readonly Dictionary<int, ProcessPriorityClass> _lowered = new();
    Process? _game;
    string _gameName = "";
    ProcessPriorityClass _gameOriginal = ProcessPriorityClass.Normal;

    public GameBooster(AppSettings settings)
    {
        _settings = settings;
        // First check happens on the first timer tick, never during startup, so the window always appears straight away.
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    string _status = "Waiting for a game...";
    public string Status { get => _status; private set => SetField(ref _status, value); }

    public bool Enabled
    {
        get => _settings.BoostEnabled;
        set
        {
            if (_settings.BoostEnabled == value) return;
            _settings.BoostEnabled = value;
            _settings.Save();
            OnPropertyChanged();
            if (!value) EndBoost("Boost is off.");
            Tick();
        }
    }

    void Tick()
    {
        if (!Enabled)
        {
            Status = "Boost is off.";
            return;
        }

        if (_game != null)
        {
            if (!IsRunning(_game)) EndBoost($"{_gameName} closed. Background apps restored.");
            else if (++_ticksBoosted % 10 == 0) KeepAppsClosed(); // every 30 seconds
            return;
        }

        foreach (var name in _settings.GameProcesses)
        {
            var proc = Process.GetProcessesByName(name).FirstOrDefault();
            if (proc != null)
            {
                StartBoost(proc);
                return;
            }
        }
        Status = "Waiting for a game...";
    }

    /// <summary>Checks by PID rather than HasExited, which anti-cheat protected games can refuse.</summary>
    static bool IsRunning(Process p)
    {
        try { return Process.GetProcessById(p.Id).ProcessName == p.ProcessName; }
        catch (ArgumentException) { return false; }
    }

    void StartBoost(Process game)
    {
        _game = game;
        _gameName = game.ProcessName;
        var notes = new List<string>();

        if (_settings.RaiseGamePriority)
        {
            try
            {
                _gameOriginal = game.PriorityClass;
                game.PriorityClass = ProcessPriorityClass.High;
                notes.Add("priority raised to High");
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                notes.Add("priority unchanged (blocked by the game's anti-cheat)");
            }
        }

        if (_settings.CloseAppsOnGameStart)
        {
            // Closing waits on each app, so do it off the UI thread and report when finished.
            var names = _settings.AppsToClose.ToList();
            Task.Run(() => ProcessCleaner.Close(names)).ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully && t.Result.Count > 0) Status += $" Closed {string.Join(", ", t.Result)}.";
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        if (_settings.LowerBackgroundApps)
        {
            foreach (var name in _settings.BackgroundProcesses)
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    if (_lowered.ContainsKey(p.Id)) continue;
                    _lowered[p.Id] = p.PriorityClass;
                    p.PriorityClass = ProcessPriorityClass.BelowNormal;
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            }
            notes.Add($"{_lowered.Count} background process(es) lowered");
        }

        Status = $"Boosting {game.ProcessName}: {string.Join(", ", notes)}.";
    }

    int _ticksBoosted;
    bool _closing;

    /// <summary>Closes list apps that reopened during the game (OneDrive, Teams and others restart themselves).</summary>
    void KeepAppsClosed()
    {
        if (!_settings.CloseAppsOnGameStart || !_settings.KeepAppsClosed || _closing) return;
        _closing = true;
        var names = _settings.AppsToClose.ToList();
        Task.Run(() => ProcessCleaner.Close(names)).ContinueWith(t =>
        {
            _closing = false;
            if (t.IsCompletedSuccessfully && t.Result.Count > 0)
                Status = $"Boosting {_gameName}. Closed again: {string.Join(", ", t.Result)}.";
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    void EndBoost(string status)
    {
        _ticksBoosted = 0;
        if (_game != null && IsRunning(_game) && _settings.RaiseGamePriority)
        {
            try { _game.PriorityClass = _gameOriginal; }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        }
        _game = null;

        foreach (var (pid, priority) in _lowered)
        {
            try { Process.GetProcessById(pid).PriorityClass = priority; }
            catch (Exception ex) when (ex is ArgumentException or System.ComponentModel.Win32Exception or InvalidOperationException) { }
        }
        _lowered.Clear();
        Status = status;
    }

    /// <summary>Restores everything; call on app exit.</summary>
    public void Shutdown()
    {
        _timer.Stop();
        EndBoost("Stopped.");
    }
}
