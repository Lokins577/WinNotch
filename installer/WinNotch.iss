; WinNotch Inno Setup Script
; Requires Inno Setup 6.x (https://jrsoftware.org/isinfo.php)

#define MyAppName "WinNotch"
; Version can be overridden by the build (e.g. ISCC /DMyAppVersion=1.2.3).
; The fallback below is used by local build.bat runs; keep it synced with WinNotch.csproj.
#ifndef MyAppVersion
  #define MyAppVersion "0.7.2"
#endif
#define MyAppPublisher "N3uralCreativity"
#define MyAppURL "https://github.com/N3uralCreativity/WinNotch"
#define MyAppExeName "WinNotch.exe"

[Setup]
AppId={{B8F3A2D1-7E4C-4F5A-9D6B-1C2E3F4A5B6C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=..\LICENSE
OutputDir=..\publish\installer
OutputBaseFilename=WinNotch-{#MyAppVersion}-Setup
SetupIconFile=..\src\WinNotch\Assets\Icons\WinNotch.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
DisableProgramGroupPage=yes
; Upgrade support: use previous install dir, close running app.
; RestartApplications=no: WinNotch is relaunched explicitly via [Run] in update
; mode, so the Restart Manager must NOT also restart it (that would race/duplicate).
UsePreviousAppDir=yes
CloseApplications=yes
RestartApplications=no
CloseApplicationsFilter=WinNotch.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupentry"; Description: "Launch WinNotch at Windows startup"; GroupDescription: "System:"

[Files]
Source: "..\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startupentry

[Run]
; Interactive install: offer to launch on the Finished page.
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent; Check: not IsUpdateMode
; Silent self-update: relaunch the app automatically once the new files are in place.
Filename: "{app}\{#MyAppExeName}"; Flags: nowait runasoriginaluser; Check: IsUpdateMode

[UninstallDelete]
Type: files; Name: "{localappdata}\WinNotch\*"
Type: dirifempty; Name: "{localappdata}\WinNotch"

[Code]

const
  // Inno's per-app uninstall registry key. The GUID is the [Setup] AppId, written
  // here as a literal with single braces so it matches the key Inno actually
  // creates. Deriving it from the AppId setting via the preprocessor emits a
  // doubled opening brace, so the registry lookup silently misses and an existing
  // install is never detected (the original bug this fixes).
  AppUninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{B8F3A2D1-7E4C-4F5A-9D6B-1C2E3F4A5B6C}_is1';

var
  MaintPage: TInputOptionWizardPage;

procedure KillRunningProcess();
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/F /IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  // Small wait to ensure the process is fully terminated and its file locks released.
  Sleep(1000);
end;

// True when launched by the in-app updater (WinNotch passes /UPDATE=1). In this
// mode the installer performs a clean silent in-place upgrade and relaunches the
// app, with no maintenance menu or prompts.
function IsUpdateMode(): Boolean;
begin
  Result := ExpandConstant('{param:UPDATE|0}') = '1';
end;

procedure InitializeUninstallProgressForm();
begin
  // Kill WinNotch before uninstalling
  KillRunningProcess();
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  AppDataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Always remove startup registry entry
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#MyAppName}');

    // Ask user if they want to delete all data during interactive uninstalls only
    if not UninstallSilent() then
    begin
      if MsgBox('Do you want to delete all WinNotch data?' + #13#10 + #13#10 +
                'This includes plugins, settings, logs, and all user data.' + #13#10 +
                '(Located in: ' + ExpandConstant('{userappdata}\WinNotch') + ')',
                mbConfirmation, MB_YESNO) = IDYES then
      begin
        // Delete %AppData%\WinNotch (plugins, settings, logs)
        AppDataDir := ExpandConstant('{userappdata}\WinNotch');
        if DirExists(AppDataDir) then
          DelTree(AppDataDir, True, True, True);

        // Delete %LocalAppData%\WinNotch
        AppDataDir := ExpandConstant('{localappdata}\WinNotch');
        if DirExists(AppDataDir) then
          DelTree(AppDataDir, True, True, True);
      end;
    end;
  end;
end;

function GetUninstallString(): String;
var
  sUnInstallString: String;
begin
  sUnInstallString := '';
  RegQueryStringValue(HKCU, AppUninstallKey, 'UninstallString', sUnInstallString);
  if sUnInstallString = '' then
    RegQueryStringValue(HKLM, AppUninstallKey, 'UninstallString', sUnInstallString);
  Result := sUnInstallString;
end;

function GetInstalledVersion(): String;
var
  sVersion: String;
begin
  sVersion := '';
  RegQueryStringValue(HKCU, AppUninstallKey, 'DisplayVersion', sVersion);
  if sVersion = '' then
    RegQueryStringValue(HKLM, AppUninstallKey, 'DisplayVersion', sVersion);
  Result := sVersion;
end;

function IsAppInstalled(): Boolean;
begin
  Result := GetUninstallString() <> '';
end;

// When the app is already installed and Setup is run interactively (not by the
// in-app updater), present a Repair / Uninstall menu instead of the install flow.
procedure InitializeWizard();
begin
  if IsAppInstalled() and (not IsUpdateMode()) then
  begin
    MaintPage := CreateInputOptionPage(wpWelcome,
      'WinNotch Maintenance',
      'WinNotch is already installed on this computer.',
      'Select what you would like to do, then click Next.',
      True, False);
    MaintPage.Add('Repair WinNotch (reinstall the current version)');
    MaintPage.Add('Uninstall WinNotch');
    MaintPage.SelectedValueIndex := 0;
  end;
end;

// In maintenance mode the menu replaces the standard install pages.
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;

  // The silent update path never displays pages.
  if IsUpdateMode() then
    Exit;

  if IsAppInstalled() then
  begin
    if (PageID = wpLicense) or (PageID = wpInfoBefore) or
       (PageID = wpSelectDir) or (PageID = wpSelectProgramGroup) or
       (PageID = wpSelectTasks) or (PageID = wpReady) then
      Result := True;
  end;
end;

// Act on the maintenance menu: Repair continues the install (overwriting files);
// Uninstall runs the uninstaller and terminates Setup.
function NextButtonClick(CurPageID: Integer): Boolean;
var
  ResultCode: Integer;
  UninstallString: String;
begin
  Result := True;

  if (MaintPage <> nil) and (CurPageID = MaintPage.ID) then
  begin
    if MaintPage.SelectedValueIndex = 1 then
    begin
      // Uninstall selected: hand off to the real uninstaller (shows its own progress
      // and the "delete all data?" prompt), then end Setup (nothing to install).
      UninstallString := RemoveQuotes(GetUninstallString());
      if UninstallString <> '' then
        Exec(UninstallString, '', '', SW_SHOW, ewNoWait, ResultCode);
      Abort;
    end;
    // Repair selected (index 0): fall through; the install overwrites the existing
    // files (the payload is flagged ignoreversion).
  end;
end;

function InitializeSetup(): Boolean;
begin
  // All "already installed" handling now happens through the maintenance menu
  // (InitializeWizard / ShouldSkipPage / NextButtonClick) for interactive runs, or
  // silently via /UPDATE=1 for the in-app updater. Nothing to gate here.
  Result := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  // Free the app's files before installing/upgrading. WinNotch normally exits on
  // its own first (releasing its single-instance mutex); this is the safety net.
  KillRunningProcess();
  Result := '';
end;
