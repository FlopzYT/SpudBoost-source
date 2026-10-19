using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GameTweaks.Core;

/// <summary>
/// Cheap checks used by the overlays: is Fortnite the window in front, and is a key held down.
/// Only reads state; never sends anything to the game.
/// </summary>
public sealed class ForegroundGame
{
    public const string Fortnite = "FortniteClient-Win64-Shipping";

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);

    uint _lastPid;
    bool _isFortnite;

    /// <summary>True when the window in front belongs to Fortnite. The process is only looked up when focus changes.</summary>
    public bool FortniteInFront()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        if (pid != _lastPid)
        {
            _lastPid = pid;
            try { _isFortnite = pid != 0 && Process.GetProcessById((int)pid).ProcessName == Fortnite; }
            catch (ArgumentException) { _isFortnite = false; }
        }
        return _isFortnite;
    }

    public static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
}
