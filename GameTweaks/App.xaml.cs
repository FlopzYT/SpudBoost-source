using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using GameTweaks.Core;

namespace GameTweaks;

public partial class App : Application
{
    const string MutexName = @"Local\GameTweaks.SingleInstance";
    const string ShowEventName = @"Local\GameTweaks.Show";
    const string ExitEventName = @"Local\GameTweaks.Exit";

    /// <summary>"SpudBoost.exe --exit" cleanly closes a running copy (used when updating).</summary>
    public const string ExitArg = "--exit";

    /// <summary>Passed when SpudBoost restarts itself (e.g. as admin) so the new copy waits for the old one to exit.</summary>
    public const string RestartedArg = "--restarted";
    /// <summary>Passed by the "Start with Windows" entry: start hidden in the tray.</summary>
    public const string MinimizedArg = "--minimized";

    static string CrashLog => Path.Combine(BackupStore.Folder, "crash.log");

    Mutex? _instanceMutex;
    EventWaitHandle? _showEvent;
    EventWaitHandle? _exitEvent;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Errors in the tray menu (Windows Forms) go to our handler below instead of a separate WinForms dialog.
        System.Windows.Forms.Application.SetUnhandledExceptionMode(System.Windows.Forms.UnhandledExceptionMode.ThrowException);

        // Without these, an error during startup closes the app with no window and no message.
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Report(args.ExceptionObject as Exception, fatal: true);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log(args.Exception);
            args.SetObserved();
        };
        base.OnStartup(e);

        if (e.Args.Contains(ExitArg))
        {
            Signal(ExitEventName);
            Shutdown();
            return;
        }

        // Double-clicked .spudboost file: hand it over (to this copy, or to the one already running).
        if (e.Args.FirstOrDefault(a => a.EndsWith(PersonalProfiles.FileExtension, StringComparison.OrdinalIgnoreCase)) is { } config)
        {
            Directory.CreateDirectory(BackupStore.Folder);
            File.WriteAllText(GameTweaks.MainWindow.PendingImportFile, Path.GetFullPath(config));
        }

        if (!TakeSingleInstance(e.Args.Contains(RestartedArg)))
        {
            Shutdown();
            return;
        }

        // The window decides when the app ends (tray mode keeps it alive while hidden).
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var window = new MainWindow();
        MainWindow = window;
        if (!e.Args.Contains(MinimizedArg)) window.Show();

        // Another copy was started: bring this one to the front (or exit, for "--exit").
        var events = new WaitHandle[] { _showEvent!, _exitEvent! };
        new Thread(() =>
        {
            while (true)
            {
                if (WaitHandle.WaitAny(events) == 0) Dispatcher.BeginInvoke(window.ShowFromTray);
                else Dispatcher.BeginInvoke(window.ExitApp);
            }
        }) { IsBackground = true }.Start();
    }

    /// <summary>Returns false if another copy is running; that copy is asked to show itself.</summary>
    bool TakeSingleInstance(bool restarted)
    {
        try
        {
            _instanceMutex = new Mutex(true, MutexName, out var createdNew);
            if (!createdNew)
            {
                var acquired = false;
                try { acquired = _instanceMutex.WaitOne(restarted ? 15000 : 0); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired)
                {
                    Signal(ShowEventName);
                    return false;
                }
            }
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // The running copy is elevated, so this unelevated one can't open its objects.
            MessageBox.Show("SpudBoost is already running as administrator. Open it from the system tray (the orange potato icon near the clock).",
                "SpudBoost", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
    }

    static void Signal(string eventName)
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(eventName, out var ev)) ev.Set();
        }
        catch (UnauthorizedAccessException) { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showEvent?.Dispose();
        _exitEvent?.Dispose();
        base.OnExit(e);
    }

    void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        var fatal = MainWindow is not { IsLoaded: true };
        Report(e.Exception, fatal);
        if (fatal) Shutdown(1);
    }

    static void Report(Exception? ex, bool fatal)
    {
        Log(ex);
        MessageBox.Show(
            (fatal ? "SpudBoost couldn't start." : "Something went wrong.") +
            $"\n\n{ex?.Message}\n\nDetails were saved to:\n{CrashLog}",
            "SpudBoost", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    static void Log(Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(BackupStore.Folder);
            File.AppendAllText(CrashLog,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] admin={Shell.IsAdmin} {Environment.OSVersion}\n{ex}\n\n");
        }
        catch (IOException) { }
    }
}
