#define MyAppName "MikaNote"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "MikaNote"
#define MyAppExeName "MikaNote.App.exe"
#define MyAppId "{{6C041A2E-553D-4B06-A41A-5C02C9C3E7B4}}"
#define DotNetDesktopRuntimeFileName "windowsdesktop-runtime-8.0.25-win-x64.exe"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\MikaNote
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\MikaNote.App\Assets\MikaIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=Output
OutputBaseFilename=MikaNote-Setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "startup"; Description: "Start MikaNote when Windows starts"; Flags: checkedonce
Name: "contextmenu"; Description: "Add desktop context menu"; Flags: checkedonce
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: checkedonce

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "redist\{#DotNetDesktopRuntimeFileName}"; DestDir: "{tmp}"; Flags: deleteafterinstall ignoreversion

[Icons]
Name: "{group}\MikaNote"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\MikaIcon.ico"
Name: "{group}\Uninstall MikaNote"; Filename: "{uninstallexe}"
Name: "{autodesktop}\MikaNote"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; IconFilename: "{app}\Assets\MikaIcon.ico"

[Run]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--disable-startup"; Flags: runhidden waituntilterminated runasoriginaluser
Filename: "{app}\{#MyAppExeName}"; Parameters: "--uninstall-shell"; Flags: runhidden waituntilterminated runasoriginaluser
Filename: "{app}\{#MyAppExeName}"; Parameters: "--enable-startup"; Flags: runhidden waituntilterminated runasoriginaluser; Tasks: startup
Filename: "{app}\{#MyAppExeName}"; Parameters: "--install-shell"; Flags: runhidden waituntilterminated runasoriginaluser; Tasks: contextmenu
Filename: "{app}\{#MyAppExeName}"; Parameters: "--show-welcome-splash"; Description: "Launch MikaNote"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--disable-startup"; Flags: runhidden waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Parameters: "--uninstall-shell"; Flags: runhidden waituntilterminated

[Code]
var
  RemoveUserData: Boolean;

function IsDotNetDesktopRuntime8Installed: Boolean;
var
  FindRec: TFindRec;
  RuntimeBaseDir: string;
begin
  RuntimeBaseDir := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App');
  Result := False;

  if not DirExists(RuntimeBaseDir) then
    exit;

  if FindFirst(RuntimeBaseDir + '\8.*', FindRec) then
  begin
    try
      Result := True;
    finally
      FindClose(FindRec);
    end;
  end;
end;

function InstallDotNetDesktopRuntimeIfNeeded: Boolean;
var
  ResultCode: Integer;
  RuntimeInstaller: string;
begin
  Result := True;

  if IsDotNetDesktopRuntime8Installed then
    exit;

  RuntimeInstaller := ExpandConstant('{tmp}\{#DotNetDesktopRuntimeFileName}');
  if not FileExists(RuntimeInstaller) then
  begin
    MsgBox('.NET 8 Desktop Runtime installer was not found in the setup package.', mbCriticalError, MB_OK);
    Result := False;
    exit;
  end;

  WizardForm.StatusLabel.Caption := 'Installing .NET 8 Desktop Runtime...';

  if not Exec(RuntimeInstaller, '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    MsgBox('Failed to launch the .NET 8 Desktop Runtime installer.', mbCriticalError, MB_OK);
    Result := False;
    exit;
  end;

  if (ResultCode <> 0) and (ResultCode <> 3010) then
  begin
    MsgBox(
      '.NET 8 Desktop Runtime installation failed.' + #13#10 +
      'Installer exit code: ' + IntToStr(ResultCode),
      mbCriticalError,
      MB_OK);
    Result := False;
    exit;
  end;
end;

function InitializeUninstall(): Boolean;
begin
  RemoveUserData :=
    SuppressibleMsgBox(
      'Keep your notes and settings in Documents\MikaNote?' + #13#10#13#10 +
      'Choose Yes to keep them, or No to remove them with the app.',
      mbConfirmation,
      MB_YESNO or MB_DEFBUTTON1,
      IDYES) = IDNO;
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and RemoveUserData then
  begin
    DelTree(ExpandConstant('{userdocs}\MikaNote'), True, True, True);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if not InstallDotNetDesktopRuntimeIfNeeded then
      RaiseException('.NET 8 Desktop Runtime installation failed.');
  end;
end;
