using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace GameTweaks.Core;

/// <summary>Runs external commands and handles elevation.</summary>
public static class Shell
{
    public static bool IsAdmin { get; } =
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    public static (int ExitCode, string Output) Run(string file, string args)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {file}");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return (p.ExitCode, (stdout.Result + stderr.Result).Trim());
    }

    /// <summary>Runs a PowerShell script, passed encoded so no quoting is needed.</summary>
    public static (int ExitCode, string Output) PowerShell(string script)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference='Stop'\n" + script));
        return Run("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}");
    }

    public static void RestartAsAdmin()
    {
        // The new copy waits for this one to exit (single-instance lock) before opening its window.
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, App.RestartedArg) { UseShellExecute = true, Verb = "runas" });
        System.Windows.Application.Current.Shutdown();
    }

    public static void OpenFolder(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
}
