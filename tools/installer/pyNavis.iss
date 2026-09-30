; pyNavis installer (Inno Setup 6).
;
; Per-user only: PrivilegesRequired=lowest, so there is no UAC prompt and nothing is
; written under Program Files. Two destinations:
;   {userappdata}\Autodesk\ApplicationPlugins\pyNavis.bundle   the loader + manifest
;   {userappdata}\pyNavis                                      runtime per year, extensions, cli
;
; config.json and the logs also live in {userappdata}\pyNavis and are never written or
; removed here. The uninstaller mops up the runtime trees, the shipped extension and the
; PyNavisPanes.dll the runtime may generate inside the bundle, and leaves everything else.
;
; Built by tools\package.ps1, which stages the payload and passes AppVersion and StageDir:
;   ISCC.exe /DAppVersion=0.4.0 /DStageDir=<repo>\dist\stage /DOutputDir=<repo>\dist ^
;            /DOutputBaseFilename=pyNavis-0.4.0-setup tools\installer\pyNavis.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef StageDir
  #define StageDir SourcePath + "..\..\dist\stage"
#endif
#ifndef OutputDir
  #define OutputDir SourcePath + "..\..\dist"
#endif
#ifndef OutputBaseFilename
  #define OutputBaseFilename "pyNavis-" + AppVersion + "-setup"
#endif

#define RepoDir SourcePath + "..\.."
#define BundleDir "{userappdata}\Autodesk\ApplicationPlugins\pyNavis.bundle"

[Setup]
; AppId is the product's UpgradeCode: it must never change, or an update would install
; alongside the previous version instead of replacing it.
AppId={{A1C4E7B2-3D58-4F9A-8B6C-5E2D7A9F1C43}
AppName=pyNavis
AppVersion={#AppVersion}
AppVerName=pyNavis {#AppVersion}
AppPublisher=pyNavis
AppPublisherURL=https://pynavis.com
AppSupportURL=https://docs.pynavis.com
AppUpdatesURL=https://pynavis.com
UninstallDisplayName=pyNavis {#AppVersion}
VersionInfoVersion={#AppVersion}
VersionInfoCompany=pyNavis
VersionInfoDescription=pyNavis setup
VersionInfoProductName=pyNavis

; Per-user install: no elevation, no Program Files, no machine-wide state.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=
ArchitecturesAllowed=x64compatible
DefaultDirName={userappdata}\pyNavis
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=no
; The welcome page carries the "close Navisworks first" note, so keep it.
DisableWelcomePage=no
UsePreviousAppDir=no
; Out of the way of config.json and the logs, which share this folder.
UninstallFilesDir={app}\uninstall

SetupIconFile={#RepoDir}\assets\logo\pynavis.ico
LicenseFile={#RepoDir}\LICENSE
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseFilename}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel2=This will install pyNavis {#AppVersion} for the current user only. No administrator rights are needed and nothing is written to the Navisworks program folder.%n%nClose Navisworks before you continue.

[InstallDelete]
; Before copying, the previous version's shipped extension and runtimes go, so a bundle
; folder that was renamed between releases cannot survive beside its replacement and
; put every tool on it on the ribbon twice. PrepareToInstall has already backed up any
; work of the user's in that extension, and moved what it could to My pyNavis.extension
; (see [Code]). Only what this installer owns is listed:
; config.json, secrets.json, the logs, the AI.extension the assistant writes and any
; other extension the user added are untouched. The loader bundle is not wiped either,
; because the runtime-generated PyNavisPanes.dll (extra panel slots) lives there; the
; loader DLL and manifest are simply overwritten by [Files].
Type: filesandordirs; Name: "{app}\extensions\pyNavis.extension"
Type: filesandordirs; Name: "{app}\2023\runtime"
Type: filesandordirs; Name: "{app}\2024\runtime"
Type: filesandordirs; Name: "{app}\2025\runtime"
Type: filesandordirs; Name: "{app}\2026\runtime"
Type: filesandordirs; Name: "{app}\2027\runtime"
Type: filesandordirs; Name: "{app}\cli"

[Files]
; The Autodesk bundle: PackageContents.xml plus Contents\<year>\PyNavis.dll for every
; Navisworks release this build supports.
Source: "{#StageDir}\bundle\*"; DestDir: "{#BundleDir}"; Flags: ignoreversion recursesubdirs createallsubdirs
; The runtime (one folder per year, pynavislib inside it), the shipped extension and
; cli\pynavis.exe.
Source: "{#StageDir}\appdata\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; The upgrade guard, unpacked to {tmp} only: this release's command line and every
; release's shipped-files manifest in one file. PrepareToInstall runs it before
; [InstallDelete] replaces pyNavis.extension.
Source: "{#StageDir}\appdata\cli\pynavis.exe"; Flags: dontcopy
Source: "{#StageDir}\manifests.txt"; Flags: dontcopy

[Run]
Filename: "https://tools.pynavis.com"; Description: "Open the tools guide at tools.pynavis.com"; Flags: postinstall shellexec nowait skipifsilent unchecked

[Code]
const
  BundleRelPath = '\Autodesk\ApplicationPlugins\pyNavis.bundle';

function BundlePath(): String;
begin
  Result := ExpandConstant('{userappdata}') + BundleRelPath;
end;

{ Navisworks memory-maps the loader DLL, so neither install nor uninstall can replace
  or delete it while the host is running. There is no AppMutex to wait on because the
  host is not ours, so the process list is the only signal. }
function IsNavisworksRunning(): Boolean;
var
  Locator, Services, Processes: Variant;
begin
  Result := False;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Services := Locator.ConnectServer('localhost', 'root\CIMV2');
    Processes := Services.ExecQuery('SELECT Name FROM Win32_Process WHERE Name = "Roamer.exe"');
    Result := Processes.Count > 0;
  except
    { If WMI is unavailable, do not block the user over it. }
    Result := False;
  end;
end;

function CheckNavisworksClosed(): Boolean;
begin
  Result := True;
  if IsNavisworksRunning() then
  begin
    MsgBox('Navisworks is running.' + #13#10#13#10 +
           'Close every Navisworks window and try again.', mbError, MB_OK);
    Result := False;
  end;
end;

{ A loader left in <Navisworks>\Plugins\PyNavis by a pre-bundle install still loads, so
  both copies would run and the ribbon would be built twice. Removing it needs
  administrator rights, so this only reports the folder. }
procedure WarnAboutLegacyInstall();
var
  Years: array[0..4] of String;
  Roots: array[0..1] of String;
  I, J: Integer;
  Candidate, Found: String;
begin
  Years[0] := '2023'; Years[1] := '2024'; Years[2] := '2025'; Years[3] := '2026'; Years[4] := '2027';
  Roots[0] := 'C:\Program Files\Autodesk\Navisworks Manage ';
  Roots[1] := 'D:\Program Files\Autodesk\Navisworks Manage ';
  Found := '';
  for I := 0 to 3 do
    for J := 0 to 1 do
    begin
      Candidate := Roots[J] + Years[I] + '\Plugins\PyNavis';
      if DirExists(Candidate) then
        Found := Found + Candidate + #13#10;
    end;
  if Found <> '' then
    MsgBox('An older pyNavis is still installed in the Navisworks program folder:' + #13#10#13#10 +
           Found + #13#10 +
           'Both copies would load and the pyNavis ribbon tab would be built twice. ' +
           'Delete the folder listed above (that needs administrator rights) before you start Navisworks.',
           mbInformation, MB_OK);
end;

function ProcessRunning(Pid: Integer): Boolean;
var
  Locator, Services, Processes: Variant;
begin
  Result := False;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Services := Locator.ConnectServer('localhost', 'root\CIMV2');
    Processes := Services.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE ProcessId = ' + IntToStr(Pid));
    Result := Processes.Count > 0;
  except
    Result := False;
  end;
end;

{ An update pyNavis downloaded starts itself as Navisworks closes, passing
  /WAITPID=<that Navisworks>: its loader DLL cannot be replaced while that process
  holds it, so wait, up to five minutes, for the process to be gone. }
procedure WaitForLauncher();
var
  Pid, Waited: Integer;
begin
  Pid := StrToIntDef(ExpandConstant('{param:WAITPID|0}'), 0);
  if Pid <= 0 then Exit;
  Log('Waiting for Navisworks (process ' + IntToStr(Pid) + ') to exit');
  Waited := 0;
  while ProcessRunning(Pid) and (Waited < 300000) do
  begin
    Sleep(500);
    Waited := Waited + 500;
  end;
end;

function InitializeSetup(): Boolean;
begin
  WaitForLauncher();
  Result := CheckNavisworksClosed();
  if Result then
    WarnAboutLegacyInstall();
end;

{ Before a single file is deleted: what the user added to or edited in
  pyNavis.extension is backed up to the backups folder, and the buttons and panels they
  added move to My pyNavis.extension, where they stay on the ribbon because tabs and
  panels merge by name (pynavis protect-extension, from this release's command line).
  When even that cannot be done, stop here, before anything changes, rather than
  lose their work. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Ext, Params: String;
  ResultCode: Integer;
begin
  Result := '';
  Ext := ExpandConstant('{app}\extensions\pyNavis.extension');
  if not DirExists(Ext) then Exit;
  ExtractTemporaryFile('pynavis.exe');
  ExtractTemporaryFile('manifests.txt');
  Params := 'protect-extension' +
    ' --extension "' + Ext + '"' +
    ' --manifests "' + ExpandConstant('{tmp}\manifests.txt') + '"' +
    ' --backup-root "' + ExpandConstant('{app}\backups') + '"' +
    ' --user-extension "' + ExpandConstant('{app}\extensions\My pyNavis.extension') + '"' +
    ' --version {#AppVersion}' +
    ' --report "' + ExpandConstant('{app}\last-upgrade.json') + '"';
  if not Exec(ExpandConstant('{tmp}\pynavis.exe'), Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    ResultCode := -1;
  Log('protect-extension exited with ' + IntToStr(ResultCode));
  if ResultCode <> 0 then
    Result := 'pyNavis could not back up what you added to pyNavis.extension, so nothing has been ' +
              'changed. Copy ' + Ext + ' somewhere safe, then run setup again.';
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpFinished) and FileExists(ExpandConstant('{app}\last-upgrade.json')) then
    WizardForm.FinishedLabel.Caption := WizardForm.FinishedLabel.Caption + #13#10#13#10 +
      'Your own work in pyNavis.extension was kept: buttons and panels you had added are now in ' +
      'My pyNavis.extension, and a copy of everything is in ' + ExpandConstant('{app}\backups') + '.';
end;

function InitializeUninstall(): Boolean;
begin
  Result := CheckNavisworksClosed();
end;

function LooksLikeYear(const Name: String): Boolean;
var
  I: Integer;
begin
  Result := Length(Name) = 4;
  if not Result then Exit;
  for I := 1 to 4 do
    if (Name[I] < '0') or (Name[I] > '9') then
    begin
      Result := False;
      Exit;
    end;
end;

{ Uninstall mop-up. The files listed in [Files] are already gone by now; what is left is
  what the runtime generated: PyNavisPanes.dll next to the loader, a rewritten manifest,
  and any .pyc or __pycache__ folders under the runtime. }
procedure CleanBundle();
var
  ContentsDir, ChildDir: String;
  FindRec: TFindRec;
begin
  ContentsDir := BundlePath() + '\Contents';
  if FindFirst(ContentsDir + '\*', FindRec) then
  begin
    try
      repeat
        if ((FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0) and
           (FindRec.Name <> '.') and (FindRec.Name <> '..') then
        begin
          ChildDir := ContentsDir + '\' + FindRec.Name;
          DeleteFile(ChildDir + '\PyNavisPanes.dll');
          RemoveDir(ChildDir);
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
  DeleteFile(BundlePath() + '\PackageContents.xml');
  RemoveDir(ContentsDir);
  RemoveDir(BundlePath());
end;

{ Only the folders this installer owns: <year>\runtime, extensions\pyNavis.extension and
  cli. config.json, the logs and any other extension the user added stay. }
procedure CleanAppData();
var
  Base, ChildDir: String;
  FindRec: TFindRec;
begin
  Base := ExpandConstant('{app}');
  if FindFirst(Base + '\*', FindRec) then
  begin
    try
      repeat
        if ((FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0) and
           LooksLikeYear(FindRec.Name) then
        begin
          ChildDir := Base + '\' + FindRec.Name;
          DelTree(ChildDir + '\runtime', True, True, True);
          RemoveDir(ChildDir);
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
  DelTree(Base + '\extensions\pyNavis.extension', True, True, True);
  { All three only succeed while empty, which is exactly the intent. }
  RemoveDir(Base + '\extensions');
  RemoveDir(Base + '\cli');
  RemoveDir(Base);
end;

procedure CurUninstallStepChanged(CurStep: TUninstallStep);
begin
  if CurStep = usPostUninstall then
  begin
    CleanBundle();
    CleanAppData();
  end;
end;
