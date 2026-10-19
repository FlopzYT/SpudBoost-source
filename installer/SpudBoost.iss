; SpudBoost installer (Inno Setup 6). Build with:  ISCC.exe installer\SpudBoost.iss
; Expects the published app in ..\release\SpudBoost.exe (dotnet publish GameTweaks -c Release -o release).

#define AppName "SpudBoost"
#define AppVersion "1.1.0"
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
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "..\release\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; The app adds this itself when "Start with Windows" is ticked; remove it on uninstall (creates nothing on install).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "SpudBoost"; ValueType: none; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
; In-app updates run this installer with /SILENT: reopen SpudBoost afterwards.
Filename: "{app}\{#AppExe}"; Flags: nowait; Check: WizardSilent

[UninstallRun]
; Exit cleanly first so Game Boost puts process priorities back.
Filename: "{app}\{#AppExe}"; Parameters: "--exit"; Flags: runhidden waituntilterminated; RunOnceId: "ExitSpudBoost"

[Code]
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
