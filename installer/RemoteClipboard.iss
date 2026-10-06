; Remote Clipboard — Inno Setup 6 script.
;
; Build (Windows):
;   dotnet publish src\RemoteClipboard.App -c Release -r win-x64 --self-contained -p:PublishReadyToRun=true -o publish\RemoteClipboard
;   "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" /DAppVersion=0.3.0 /DSourceDir=..\publish\RemoteClipboard installer\RemoteClipboard.iss
;
; Installs per machine (Program Files), so every Windows user (also on a terminal server) can run it; each
; user's agent still runs in that user's own session with their own identity. The only machine-wide
; changes are the files, the shortcuts and two narrowly scoped inbound firewall rules.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\RemoteClipboard"
#endif

#define AppName "Remote Clipboard"
#define AppExe "RemoteClipboard.exe"
#define RuleTcp "Remote Clipboard (TCP)"
#define RuleUdp "Remote Clipboard (descubrimiento)"

[Setup]
; Never change AppId: it identifies the installation for upgrades and uninstall.
AppId={{6F1C3B52-8E0A-4C7B-9D3E-2A5F7C1B9E44}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Remote Clipboard
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
; Administrator rights are needed for Program Files and the firewall rules.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 1607 / Windows Server 2016 or later.
MinVersion=10.0.14393
OutputDir=Output
OutputBaseFilename=RemoteClipboardSetup-v{#AppVersion}
SetupIconFile=..\src\RemoteClipboard.App\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
; Running instances are stopped explicitly in [Code] (Restart Manager cannot close a tray-only app).
CloseApplications=no

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
es.FirewallStatus=Configurando el Firewall de Windows (solo redes privadas y de dominio)...
en.FirewallStatus=Configuring Windows Firewall (private and domain networks only)...

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; Firewall: delete first (upgrade/repair), then add. Inbound only, limited to this executable, to the
; local subnet and to private/domain profiles (never public networks).
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#RuleTcp}"""; Flags: runhidden; StatusMsg: "{cm:FirewallStatus}"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#RuleUdp}"""; Flags: runhidden; StatusMsg: "{cm:FirewallStatus}"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""{#RuleTcp}"" dir=in action=allow program=""{app}\{#AppExe}"" protocol=TCP localport=47800-47809 profile=private,domain remoteip=localsubnet enable=yes"; Flags: runhidden; StatusMsg: "{cm:FirewallStatus}"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""{#RuleUdp}"" dir=in action=allow program=""{app}\{#AppExe}"" protocol=UDP localport=47810 profile=private,domain remoteip=localsubnet enable=yes"; Flags: runhidden; StatusMsg: "{cm:FirewallStatus}"
; Start the app as the user who ran the installer (not elevated).
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#AppExe} /T"; Flags: runhidden; RunOnceId: "StopApp"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#RuleTcp}"""; Flags: runhidden; RunOnceId: "DeleteRuleTcp"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#RuleUdp}"""; Flags: runhidden; RunOnceId: "DeleteRuleUdp"

[Code]
procedure StopRunningInstances();
var
  ResultCode: Integer;
begin
  // Stops the agent in every session (upgrade/uninstall). Safe: all data files are written atomically.
  // Users' agents start again at their next logon (or now, for the installing user).
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExe} /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopRunningInstances();
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Start-with-Windows entry of the user running the uninstaller. Personal data in
    // %LOCALAPPDATA%\RemoteClipboard is kept on purpose (identity and paired devices survive a reinstall).
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'RemoteClipboard');
  end;
end;
