<p align="center"><img src="GameTweaks/logo.png" width="120" alt="SpudBoost logo"></p>

<h1 align="center">SpudBoost</h1>
<p align="center"><b>Potato PC? Boost it.</b><br>A free Windows game optimizer with one-click tweaks, Fortnite potato graphics and a custom crosshair. Every change can be undone.</p>

## Download

Get the latest **SpudBoost-Setup-x.y.z.exe** from the [Releases page](../../releases/latest) and run it. There's also a portable `.zip` if you don't want to install anything.

> **"Windows protected your PC"?** SpudBoost isn't code-signed yet, so Windows SmartScreen warns about it. Click **More info → Run anyway**. Some antivirus programs may also flag it because it changes system settings and closes background apps; the full source code is in this repository.

Requires Windows 10 or 11 (64-bit). Nothing else to install. SpudBoost checks for new versions itself and updates in one click.

## Features

- **One-click Windows tweaks**: power plan, Game Mode, GPU scheduling, game priority, mouse acceleration, memory compression, Memory Integrity (with a clear warning) and more. Each has an on/off switch that restores the exact original value.
- **Game Boost**: when a game starts, raises its priority, lowers background apps and can close apps you don't need. Everything is put back when the game closes.
- **Network**: ping tester for every Fortnite region, adapter power-saving off, Nagle off, fast DNS.
- **Fortnite Potato**: edit Fortnite's own settings file beyond the in-game menu, with Potato, Ultra Potato and Competitive presets, Legacy Performance mode, stretched resolutions and NVIDIA potato textures. Backed up before every save.
- **Custom crosshair**: an overlay that only shows in Fortnite, switches between Default, Shotgun and Sniper crosshairs with your weapon keys, hides while aiming and can spread while shooting.
- **FPS Benchmark**: measure real average FPS and 1% lows before and after tweaks (uses Intel PresentMon).
- **NVIDIA driver settings** per game, a **startup apps** manager, a **health check** that spots the biggest issues on your PC (XMP off, 60 Hz monitor, old drivers and more), and a **restore point** before the first change.

## Safe by design

SpudBoost never touches game files or game memory, and never sends input to games. It only changes Windows settings, driver profiles and the games' own settings files, the same things you could change by hand. The crosshair is a separate click-through window drawn on top of the game.

Overlay crosshairs may not be allowed in some competitive tournaments. Check the rules before using it there.

## Building from source

Requires the .NET 8 SDK and, for the installer, Inno Setup 6.

```
dotnet publish GameTweaks -c Release -o release
"%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" installer\SpudBoost.iss
```

The installer is written to `dist\`.
