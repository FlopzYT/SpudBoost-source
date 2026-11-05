; SpudBoost installer (Inno Setup 6). Build with:  ISCC.exe installer\SpudBoost.iss
; Expects the published app in ..\release\SpudBoost.exe (dotnet publish GameTweaks -c Release -o release).

#define AppName "SpudBoost"
#define AppVersion "1.6.4"
#define AppExe "SpudBoost.exe"

[Setup]
AppId={{6B1E2C57-4F0A-4D7E-9C3B-5A2F8E1D7C40}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppName} Setup
; Per-user install: no admin prompt, lands in %LOCALAPPDATA%\Programs\SpudBoost
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
SetupIconFile=..\GameTweaks\app.ico
OutputDir=..\dist
OutputBaseFilename={#AppName}-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=yes
; Tell Explorer the .spudboost file type changed
ChangesAssociations=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "startup"; Description: "&Start SpudBoost when Windows starts (in the tray, so Game Boost, the crosshair and update alerts are always ready)"; GroupDescription: "Startup:"

[Files]
Source: "..\release\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Start with Windows (same entry the app's Settings switch uses), hidden in the tray.
; Only on a fresh install or when coming from a version without this option, so a later "off" in Settings survives updates.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "SpudBoost"; ValueType: string; ValueData: """{app}\{#AppExe}"" --minimized"; Flags: uninsdeletevalue; Tasks: startup; Check: StartupNotDecidedYet
; Always remove it on uninstall, even if it was switched on later from Settings (creates nothing on install).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "SpudBoost"; ValueType: none; Flags: uninsdeletevalue

; Double-clicking a .spudboost config opens it in SpudBoost (per-user, removed on uninstall)
Root: HKCU; Subkey: "Software\Classes\.spudboost"; ValueType: string; ValueName: ""; ValueData: "SpudBoost.Config"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\SpudBoost.Config"; ValueType: string; ValueName: ""; ValueData: "SpudBoost config"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SpudBoost.Config\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe},0"
Root: HKCU; Subkey: "Software\Classes\SpudBoost.Config\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
; In-app updates run this installer with /SILENT: reopen SpudBoost afterwards.
Filename: "{app}\{#AppExe}"; Flags: nowait; Check: WizardSilent

[Code]
// True for a fresh install, or an upgrade from 1.0.x/1.1.x (before the startup option existed).
function StartupNotDecidedYet: Boolean;
var
  Previous: String;
begin
  if not RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{6B1E2C57-4F0A-4D7E-9C3B-5A2F8E1D7C40}_is1',
                             'DisplayVersion', Previous) then
    Result := True
  else
    Result := (Copy(Previous, 1, 4) = '1.0.') or (Copy(Previous, 1, 4) = '1.1.');
end;

// Before installing over an existing copy, ask it to exit cleanly (restores Game Boost changes).
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  ExistingExe: String;
begin
  ExistingExe := ExpandConstant('{app}\{#AppExe}');
  if FileExists(ExistingExe) then
  begin
    Exec(ExistingExe, '--exit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(1500);
  end;
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
    MsgBox('SpudBoost has been removed.' #13#10#13#10 +
           'Tweaks you applied are still in place. Your undo backups are kept in:' #13#10 +
           ExpandConstant('{localappdata}\GameTweaks') + #13#10#13#10 +
           'To undo tweaks, reinstall SpudBoost and use Backup & Undo, or delete that folder if you don''t need them.',
           mbInformation, MB_OK);
end;

// ---- Closing a running SpudBoost before uninstalling ----
// SpudBoost usually runs as admin and the uninstaller doesn't, so a plain "--exit" (or Windows' own "close apps")
// may not reach it, and its locked SpudBoost.exe then can't be removed.

function SpudBoostRunning: Boolean;
var
  ResultCode: Integer;
begin
  // tasklist sees admin processes too; find returns 0 when the name is in the list
  Result := Exec(ExpandConstant('{cmd}'), '/C tasklist /FI "IMAGENAME eq {#AppExe}" /NH | find /I "{#AppExe}"',
                 '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function WaitForExit(Seconds: Integer): Boolean;
var
  I: Integer;
begin
  for I := 1 to Seconds * 4 do
  begin
    if not SpudBoostRunning then
    begin
      Result := True;
      Exit;
    end;
    Sleep(250);
  end;
  Result := not SpudBoostRunning;
end;

function InitializeUninstall: Boolean;
var
  ResultCode: Integer;
  Exe: String;
begin
  Result := True;
  if not SpudBoostRunning then Exit;
  Exe := ExpandConstant('{app}\{#AppExe}');

  // 1. Ask it to exit cleanly (Game Boost puts process priorities back).
  Exec(Exe, '--exit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if WaitForExit(5) then Exit;

  // 2. It's running as admin: ask again as admin (one Windows prompt).
  ShellExec('runas', Exe, '--exit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if WaitForExit(8) then Exit;

  // 3. Still open: stop it as admin.
  ShellExec('runas', ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if WaitForExit(5) then Exit;

  MsgBox('SpudBoost is still running, so it can''t be removed yet.' #13#10#13#10 +
         'Right-click the orange potato icon near the clock, choose Exit, then run the uninstaller again.',
         mbError, MB_OK);
  Result := False;
end;
