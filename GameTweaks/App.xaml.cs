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
            _showEvent = OpenSignal(ShowEventName);
            _exitEvent = OpenSignal(ExitEventName);
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

    [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl, int revision, out IntPtr descriptor, IntPtr size);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    static extern Microsoft.Win32.SafeHandles.SafeWaitHandle CreateEvent(ref SecurityAttributes attributes, bool manualReset, bool initialState, string name);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct SecurityAttributes { public int Length; public IntPtr Descriptor; public bool Inherit; }

    /// <summary>
    /// Creates a show/exit signal that a copy started WITHOUT admin can still set (the uninstaller, the updater,
    /// a second launch). Objects made by an admin copy are otherwise off-limits to normal programs, so "--exit"
    /// silently failed and the uninstaller couldn't remove the still-running SpudBoost.exe.
    /// Only the current user can set it ("OW" = owner rights); the label lets medium-integrity programs through.
    /// </summary>
    static EventWaitHandle OpenSignal(string name)
    {
        // Allow the user's own processes (owner + interactive user) to wait/set; integrity label: medium, no write-up blocked.
        const string Sddl = "D:(A;;0x1F0003;;;OW)(A;;0x1F0003;;;IU)(A;;0x1F0003;;;SY)S:(ML;;NW;;;ME)";
        if (ConvertStringSecurityDescriptorToSecurityDescriptor(Sddl, 1, out var descriptor, IntPtr.Zero))
        {
            try
            {
                var attributes = new SecurityAttributes
                {
                    Length = System.Runtime.InteropServices.Marshal.SizeOf<SecurityAttributes>(),
                    Descriptor = descriptor,
                };
                var handle = CreateEvent(ref attributes, false, false, name);
                if (!handle.IsInvalid)
                    return new EventWaitHandle(false, EventResetMode.AutoReset) { SafeWaitHandle = handle };
            }
            finally { LocalFree(descriptor); }
        }
        return new EventWaitHandle(false, EventResetMode.AutoReset, name);
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
