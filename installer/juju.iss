; juju Windows installer - install/upgrade/uninstall
; Build: publish Juju.App, Juju.Launcher and Juju.Updater before compiling.
; IMPORTANT: /DAppVersion and /DPayloadBytes should be supplied by the release build.
; PayloadBytes is the uncompressed size of all installed payload files.
#define AppName "juju"
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#define AppPublisher "juju"
#define LauncherExeName "Juju.Launcher.exe"
#define UpdaterExeName "Juju.Updater.exe"
#ifndef BootstrapFilesDir
  #define BootstrapFilesDir "..\artifacts\bootstrap"
#endif
#ifndef AppFilesDir
  #define AppFilesDir "..\artifacts\win-x64"
#endif
#ifndef PayloadBytes
  ; Conservative fallback. Production builds MUST pass the actual size.
  #define PayloadBytes "2147483648"
#endif
#ifndef ManifestSha256
  #define ManifestSha256 ""
#endif

[Setup]
AppId={{3E2E8E5A-917A-46A0-BF5F-C4F2B3619FC5}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\juju
UsePreviousAppDir=yes
DefaultGroupName=juju
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
CloseApplications=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=juju_v{#AppVersion}_setup_win64
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName=juju
UninstallDisplayIcon={app}\Juju.Launcher.exe
Compression=lzma2
SolidCompression=yes

[Files]
Source: "{#BootstrapFilesDir}\{#LauncherExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BootstrapFilesDir}\{#UpdaterExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AppFilesDir}\*"; DestDir: "{app}\versions\{#AppVersion}.installer-stage"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "..\artifacts\installer\juju-payload-manifest.json"; DestDir: "{app}\staging"; DestName: "installer-payload-manifest.json"; Flags: ignoreversion

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; Flags: unchecked

[Icons]
Name: "{autoprograms}\juju"; Filename: "{app}\{#LauncherExeName}"; Parameters: "--show-launcher"; IconFilename: "{app}\{#LauncherExeName}"; IconIndex: 0; WorkingDir: "{app}"
Name: "{autodesktop}\juju"; Filename: "{app}\{#LauncherExeName}"; Parameters: "--show-launcher"; IconFilename: "{app}\{#LauncherExeName}"; IconIndex: 0; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
; Only runs if activation in CurStepChanged succeeded; user can opt out.
Filename: "{app}\{#LauncherExeName}"; Parameters: "--show-launcher"; WorkingDir: "{app}"; Description: "启动 juju"; Flags: nowait postinstall skipifsilent

[Code]
const
  FileAttributeDirectory = $10;
  FileAttributeReparsePoint = $400;
  MoveFileDelayUntilReboot = $4;
  DiskSafetyMarginBytes = 536870912;  { 512 MiB extra headroom }

var
  KeepLocalAppDataOnUninstall: Boolean;
  RestartAfterUninstall: Boolean;
  InstallerIsUpgrade: Boolean;
  InstalledActiveVersion: String;
  InstallerLockHandle: Cardinal;
  InstallerLockErrorCode: Cardinal;

function MoveFileExW(ExistingFileName: String; NewFileName: Cardinal; Flags: Cardinal): LongBool;
  external 'MoveFileExW@kernel32.dll stdcall';
function GetFileAttributesW(FileName: String): Cardinal;
  external 'GetFileAttributesW@kernel32.dll stdcall';
function OpenMutexW(DesiredAccess: Cardinal; InheritHandle: Boolean; Name: String): Cardinal;
  external 'OpenMutexW@kernel32.dll stdcall';
function CloseHandle(Handle: Cardinal): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';
function CreateFileW(FileName: String; DesiredAccess: Cardinal; ShareMode: Cardinal;
  SecurityAttributes: Cardinal; CreationDisposition: Cardinal; FlagsAndAttributes: Cardinal;
  TemplateFile: Cardinal): Cardinal;
  external 'CreateFileW@kernel32.dll stdcall';
function GetLastError(): Cardinal;
  external 'GetLastError@kernel32.dll stdcall';

function IsReparsePoint(const Path: String): Boolean;
var
  Attributes: Cardinal;
begin
  Attributes := GetFileAttributesW(Path);
  Result := (Attributes <> $FFFFFFFF) and
    ((Attributes and FileAttributeReparsePoint) <> 0);
end;

function IsUnsafeInstallPath(const Directory: String): Boolean;
var
  CurrentPath: String;
  ParentPath: String;
  WindowsPath: String;
  ProgramFilesPath: String;
  CommonFilesPath: String;
begin
  Result := True;
  CurrentPath := RemoveBackslashUnlessRoot(ExpandFileName(Directory));
  WindowsPath := RemoveBackslashUnlessRoot(ExpandFileName(ExpandConstant('{win}')));
  ProgramFilesPath := RemoveBackslashUnlessRoot(ExpandFileName(ExpandConstant('{pf}')));
  CommonFilesPath := RemoveBackslashUnlessRoot(ExpandFileName(ExpandConstant('{cf}')));

  if (CompareText(CurrentPath, ExtractFileDrive(CurrentPath) + '\') = 0) or
     (CompareText(CurrentPath, WindowsPath) = 0) or
     (CompareText(CurrentPath, ProgramFilesPath) = 0) or
     (CompareText(CurrentPath, CommonFilesPath) = 0) then Exit;
  if (Pos(Uppercase(WindowsPath + '\'), Uppercase(CurrentPath + '\')) = 1) or
     (Pos(Uppercase(ProgramFilesPath + '\'), Uppercase(CurrentPath + '\')) = 1) or
     (Pos(Uppercase(CommonFilesPath + '\'), Uppercase(CurrentPath + '\')) = 1) then Exit;

  { Reject network/UNC paths and every existing junction/symlink ancestor. }
  if Copy(CurrentPath, 1, 2) = '\\' then Exit;
  while CurrentPath <> '' do
  begin
    if IsReparsePoint(CurrentPath) then Exit;
    ParentPath := ExtractFileDir(CurrentPath);
    if (ParentPath = CurrentPath) or (ParentPath = '') then Break;
    CurrentPath := ParentPath;
  end;
  Result := False;
end;

function HasForeignFiles(const Directory: String): Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if not DirExists(Directory) then Exit;
  if not FindFirst(PathCombine(Directory, '*'), FindRec) then Exit;
  try
    repeat
      if (FindRec.Name <> '.') and (FindRec.Name <> '..') and
         (CompareText(FindRec.Name, 'data') <> 0) and
         (CompareText(FindRec.Name, 'versions') <> 0) and
         (CompareText(FindRec.Name, 'state') <> 0) and
         (CompareText(FindRec.Name, 'staging') <> 0) and
         (CompareText(FindRec.Name, 'Juju.Launcher.exe') <> 0) and
         (CompareText(FindRec.Name, 'Juju.Updater.exe') <> 0) and
         (CompareText(FindRec.Name, 'data-location.json') <> 0) and
         (CompareText(FindRec.Name, 'active.json') <> 0) and
         (Copy(Lowercase(FindRec.Name), 1, 5) <> 'unins') then
      begin
        Result := True;
        Exit;
      end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;

function ReadJsonStringField(const Content, Key: String): String;
var
  AtPos: Integer;
  ColonPos: Integer;
  FirstQuote: Integer;
  LastQuote: Integer;
  ValueStart: Integer;
begin
  Result := '';
  AtPos := Pos('"' + Key + '"', Content);
  if AtPos = 0 then Exit;
  ColonPos := Pos(':', Copy(Content, AtPos + Length(Key) + 2, MaxInt));
  if ColonPos = 0 then Exit;
  ColonPos := AtPos + Length(Key) + 1 + ColonPos;
  ValueStart := ColonPos + 1;
  while (ValueStart <= Length(Content)) and
        ((Content[ValueStart] = ' ') or (Content[ValueStart] = #9) or
         (Content[ValueStart] = #10) or (Content[ValueStart] = #13)) do
    ValueStart := ValueStart + 1;
  { A JSON null/non-string value must not consume the next property's quoted name. }
  if (ValueStart > Length(Content)) or (Content[ValueStart] <> '"') then Exit;
  FirstQuote := ValueStart;
  LastQuote := Pos('"', Copy(Content, FirstQuote + 1, MaxInt));
  if LastQuote = 0 then Exit;
  Result := Copy(Content, FirstQuote + 1, LastQuote - 1);
end;

function ReadInstalledVersion(const Directory: String): String;
var
  Raw: AnsiString;
  StatePath: String;
begin
  Result := '';
  StatePath := PathCombine(PathCombine(Directory, 'state'), 'active.json');
  if FileExists(StatePath) and LoadStringFromFile(StatePath, Raw) then
    Result := ReadJsonStringField(String(Raw), 'activeVersion');
end;

function ReadInstalledPendingVersion(const Directory: String): String;
var
  Raw: AnsiString;
  StatePath: String;
begin
  Result := '';
  StatePath := PathCombine(PathCombine(Directory, 'state'), 'active.json');
  if FileExists(StatePath) and LoadStringFromFile(StatePath, Raw) then
    Result := ReadJsonStringField(String(Raw), 'pendingVersion');
end;

function ParseThreePartVersion(const Version: String; var Major, Minor, Patch: Integer): Boolean;
var
  FirstDot: Integer;
  SecondDot: Integer;
  MajorText: String;
  MinorText: String;
  PatchText: String;
  Index: Integer;
begin
  Result := False;
  FirstDot := Pos('.', Version);
  if FirstDot <= 1 then Exit;
  SecondDot := Pos('.', Copy(Version, FirstDot + 1, MaxInt));
  if SecondDot <= 1 then Exit;
  SecondDot := FirstDot + SecondDot;
  if (SecondDot >= Length(Version)) or (Pos('.', Copy(Version, SecondDot + 1, MaxInt)) > 0) then Exit;
  MajorText := Copy(Version, 1, FirstDot - 1);
  MinorText := Copy(Version, FirstDot + 1, SecondDot - FirstDot - 1);
  PatchText := Copy(Version, SecondDot + 1, MaxInt);
  if (MinorText = '') or (PatchText = '') then Exit;
  if ((Length(MajorText) > 1) and (MajorText[1] = '0')) or
     ((Length(MinorText) > 1) and (MinorText[1] = '0')) or
     ((Length(PatchText) > 1) and (PatchText[1] = '0')) then Exit;
  for Index := 1 to Length(Version) do
    if (Version[Index] <> '.') and ((Version[Index] < '0') or (Version[Index] > '9')) then Exit;
  try
    Major := StrToInt(MajorText);
    Minor := StrToInt(MinorText);
    Patch := StrToInt(PatchText);
    Result := (Major >= 0) and (Minor >= 0) and (Patch >= 0);
  except
    Result := False;
  end;
end;

function CompareSimpleVersions(const NewVersion, OldVersion: String; var Valid: Boolean): Integer;
var
  N1, N2, N3, O1, O2, O3: Integer;
begin
  Result := 0;
  Valid := ParseThreePartVersion(NewVersion, N1, N2, N3) and
           ParseThreePartVersion(OldVersion, O1, O2, O3);
  if not Valid then Exit;
  if N1 <> O1 then
  begin
    if N1 > O1 then Result := 1 else Result := -1;
  end
  else if N2 <> O2 then
  begin
    if N2 > O2 then Result := 1 else Result := -1;
  end
  else if N3 <> O3 then
  begin
    if N3 > O3 then Result := 1 else Result := -1;
  end;
end;

function CheckInstallPathAndVersion(const Directory: String): String;
var
  Valid: Boolean;
  Cmp: Integer;
  Active: String;
  Pending: String;
  ManagedPath: String;
begin
  Result := '';
  if IsUnsafeInstallPath(Directory) then
  begin
    Result := '安装目录不安全：不允许系统目录、程序文件目录、磁盘根目录、网络路径或目录联接/符号链接路径。';
    Exit;
  end;
  if HasForeignFiles(Directory) then
  begin
    Result := '目标目录包含非应用管理的文件。请使用原应用安装目录或选择空目录。';
    Exit;
  end;
  ManagedPath := PathCombine(Directory, 'versions');
  if IsReparsePoint(ManagedPath) or
     IsReparsePoint(PathCombine(Directory, 'state')) or
     IsReparsePoint(PathCombine(Directory, 'staging')) or
     IsReparsePoint(PathCombine(ManagedPath, '{#AppVersion}.installer-stage')) or
     IsReparsePoint(PathCombine(ManagedPath, '{#AppVersion}')) then
  begin
    Result := '安装目录中的应用管理目录包含联接点或符号链接。为避免触及目录外的数据，安装已取消。';
    Exit;
  end;
  if IsReparsePoint(PathCombine(PathCombine(Directory, 'state'), 'active.json')) then
  begin
    Result := '活动版本状态文件不能是联接点或符号链接。';
    Exit;
  end;
  Active := ReadInstalledVersion(Directory);
  Pending := ReadInstalledPendingVersion(Directory);
  if Active <> '' then
  begin
    Cmp := CompareSimpleVersions('{#AppVersion}', Active, Valid);
    if not Valid then
      Result := '安装器无法安全比较版本号（仅支持 major.minor.patch 数字版本）。请通过统一构建流程发布。'
    else if Cmp < 0 then
      Result := '禁止降级安装：当前版本 ' + Active + ' 高于安装包版本 {#AppVersion}。';
  end
  else if FileExists(PathCombine(PathCombine(Directory, 'state'), 'active.json')) then
    Result := '已有 active.json 无法解析。为防止覆盖已有版本，安装已取消。';
  if (Result = '') and (Pending <> '') then
  begin
    Cmp := CompareSimpleVersions('{#AppVersion}', Pending, Valid);
    if not Valid then
      Result := '安装器无法安全比较待激活版本号。为防止覆盖已有版本，安装已取消。'
    else if Cmp < 0 then
      Result := '禁止降级安装：当前已有待激活版本 ' + Pending + '，安装包版本为 {#AppVersion}。';
  end;
end;

function CheckFreeSpace(const Directory: String): String;
var
  Existing: String;
  Parent: String;
  FreeBytes, TotalBytes: Int64;
  RequiredBytes: Int64;
begin
  Result := '';
  Existing := Directory;
  while not DirExists(Existing) do
  begin
    Parent := ExtractFileDir(Existing);
    if (Parent = Existing) or (Parent = '') then
    begin
      Result := '无法找到安装目录所在的磁盘。';
      Exit;
    end;
    Existing := Parent;
  end;
  if not GetSpaceOnDisk64(Existing, FreeBytes, TotalBytes) then
  begin
    Result := '无法读取安装目标磁盘的剩余空间。';
    Exit;
  end;
  try
    RequiredBytes := StrToInt64('{#PayloadBytes}') + DiskSafetyMarginBytes;
  except
    Result := '安装包的 PayloadBytes 参数无效。';
    Exit;
  end;
  if FreeBytes < RequiredBytes then
    Result := '磁盘空间不足。至少需要 ' + IntToStr(RequiredBytes div 1048576) +
      ' MiB 可用空间，当前约 ' + IntToStr(FreeBytes div 1048576) + ' MiB。';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Problem: String;
begin
  Result := True;
  if CurPageID = wpSelectDir then
  begin
    Problem := CheckInstallPathAndVersion(WizardDirValue);
    if Problem = '' then Problem := CheckFreeSpace(WizardDirValue);
    if Problem <> '' then
    begin
      MsgBox(Problem, mbError, MB_OK);
      Result := False;
    end;
  end;
end;

function IsJujuProcessRunning(): Boolean;
var
  OutputFile: String;
  ProcessList: AnsiString;
  ExitCode: Integer;
begin
  Result := False;
  OutputFile := ExpandConstant('{tmp}\juju-process-check.csv');
  DeleteFile(OutputFile);
  if not Exec(ExpandConstant('{sys}\cmd.exe'),
      '/C tasklist /FI "IMAGENAME eq Juju.App.exe" /FO CSV /NH > "' + OutputFile + '"',
      '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    Exit;
  if (ExitCode <> 0) or not LoadStringFromFile(OutputFile, ProcessList) then
    Exit;
  Result := Pos('"Juju.App.exe"', String(ProcessList)) > 0;
  DeleteFile(OutputFile);
end;

function IsJujuRunning(): Boolean;
var
  MarkerFile: String;
  MutexName: String;
  MutexText: AnsiString;
  Handle: Cardinal;
begin
  Result := False;
  MarkerFile := PathCombine(ExpandConstant('{app}\state'), 'instance-name.txt');
  if FileExists(MarkerFile) and LoadStringFromFile(MarkerFile, MutexText) then
  begin
    MutexName := Trim(String(MutexText));
    if (Length(MutexName) >= 21) and (Copy(MutexName, 1, 21) = 'juju-single-instance-') then
    begin
      Handle := OpenMutexW($00100000, False, MutexName);
      Result := Handle <> 0;
      if Result then
        CloseHandle(Handle);
    end;
  end;

  { Older releases did not write the mutex marker; still detect their app process. }
  if not Result then
    Result := IsJujuProcessRunning();
end;

function CloseJujuBeforeInstaller(IsUninstall: Boolean): Boolean;
var
  Answer: Integer;
  Attempt: Integer;
  ExitCode: Integer;
  LauncherPath: String;
  Silent: Boolean;
begin
  Result := True;
  if not IsJujuRunning() then
    Exit;

  Silent := WizardSilent;
  if IsUninstall then
    Silent := UninstallSilent;

  if not Silent then
  begin
    Answer := MsgBox(
      '应用正在运行，选择“是”关闭应用。',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2);
    if Answer <> IDYES then
    begin
      Result := False;
      Exit;
    end;
  end;

  LauncherPath := ExpandConstant('{app}\Juju.Launcher.exe');
  if not FileExists(LauncherPath) or
     not Exec(LauncherPath, '--shutdown', ExpandConstant('{app}'), SW_HIDE,
        ewWaitUntilTerminated, ExitCode) then
  begin
    if not Silent then
      MsgBox('无法请求应用安全退出；操作已取消。', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  for Attempt := 1 to 60 do
  begin
    if not IsJujuRunning() then
    begin
      Result := True;
      Exit;
    end;
    Sleep(500);
  end;

  if not Silent then
    MsgBox('应用尚未完成安全退出；为避免文件损坏，本次操作已取消。请关闭应用后重试。',
      mbError, MB_OK);
  Result := False;
end;

function AcquireInstallerLock(): Boolean;
var
  StateDirectory: String;
  LockPath: String;
  Attempt: Integer;
  ErrorCode: Cardinal;
begin
  Result := InstallerLockHandle <> 0;
  if Result then Exit;
  InstallerLockErrorCode := 0;
  StateDirectory := ExpandConstant('{app}\state');
  if not ForceDirectories(StateDirectory) and not DirExists(StateDirectory) then
  begin
    InstallerLockErrorCode := GetLastError();
    Exit;
  end;
  LockPath := PathCombine(StateDirectory, 'install.lock');
  if IsReparsePoint(StateDirectory) or IsReparsePoint(LockPath) then
  begin
    InstallerLockErrorCode := 4390;
    Exit;
  end;

  for Attempt := 1 to 300 do
  begin
    InstallerLockHandle := CreateFileW(LockPath, $C0000000, 0, 0, 4, $80, 0);
    if InstallerLockHandle <> $FFFFFFFF then
    begin
      Result := True;
      Exit;
    end;
    ErrorCode := GetLastError();
    InstallerLockErrorCode := ErrorCode;
    if (ErrorCode <> 32) and (ErrorCode <> 33) then
      Exit;
    Sleep(100);
  end;
end;

procedure ReleaseInstallerLock();
begin
  if InstallerLockHandle <> 0 then
  begin
    CloseHandle(InstallerLockHandle);
    InstallerLockHandle := 0;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := CheckInstallPathAndVersion(ExpandConstant('{app}'));
  if Result <> '' then Exit;
  Result := CheckFreeSpace(ExpandConstant('{app}'));
  if Result <> '' then Exit;
  InstalledActiveVersion := ReadInstalledVersion(ExpandConstant('{app}'));
  InstallerIsUpgrade := InstalledActiveVersion <> '';
  if not CloseJujuBeforeInstaller(False) then
    Result := 'juju 未能安全退出，安装已取消。';
  if Result = '' then
    if not AcquireInstallerLock() then
      Result := '无法取得应用安装锁（Windows 错误码 ' + IntToStr(InstallerLockErrorCode) +
        '）。请确认应用已关闭且安装目录可写后重试。';
end;

function ScheduleDeleteTree(const Directory: String): Boolean;
var
  FindRec: TFindRec;
  ChildPath: String;
begin
  Result := True;
  if not DirExists(Directory) then
    Exit;
  if IsReparsePoint(Directory) then
  begin
    Result := MoveFileExW(Directory, 0, MoveFileDelayUntilReboot);
    Exit;
  end;

  if FindFirst(PathCombine(Directory, '*'), FindRec) then
  begin
    try
      repeat
        if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
        begin
          ChildPath := PathCombine(Directory, FindRec.Name);
          if (FindRec.Attributes and FileAttributeDirectory) <> 0 then
          begin
            if IsReparsePoint(ChildPath) then
            begin
              if not MoveFileExW(ChildPath, 0, MoveFileDelayUntilReboot) then
                Result := False;
            end
            else if not ScheduleDeleteTree(ChildPath) then
              Result := False;
          end
          else if not MoveFileExW(ChildPath, 0, MoveFileDelayUntilReboot) then
            Result := False;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end
  else
    Result := False;

  if not MoveFileExW(Directory, 0, MoveFileDelayUntilReboot) then
    Result := False;
end;

function DeleteTreeSafely(const Directory: String): Boolean;
var
  FindRec: TFindRec;
  ChildPath: String;
begin
  if not DirExists(Directory) then
  begin
    Result := True;
    Exit;
  end;

  { Remove a junction itself; never enumerate or delete its external target. }
  if IsReparsePoint(Directory) then
  begin
    Result := RemoveDir(Directory);
    Exit;
  end;

  Result := FindFirst(PathCombine(Directory, '*'), FindRec);
  if not Result then
    Exit;
  try
    repeat
      if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
      begin
        ChildPath := PathCombine(Directory, FindRec.Name);
        if (FindRec.Attributes and FileAttributeDirectory) <> 0 then
        begin
          if IsReparsePoint(ChildPath) then
          begin
            if not RemoveDir(ChildPath) then
              Result := False;
          end
          else if not DeleteTreeSafely(ChildPath) then
            Result := False;
        end
        else if not DeleteFile(ChildPath) then
          Result := False;
      end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;

  if not RemoveDir(Directory) then
    Result := False;
end;

function RemoveOwnedDirectory(const Directory: String; ScheduleIfBusy: Boolean): Boolean;
var
  Attempt: Integer;
begin
  Result := not DirExists(Directory);
  if Result then
    Exit;

  { Never recurse through an install-local junction into an external directory. }
  if IsReparsePoint(Directory) then
  begin
    Result := RemoveDir(Directory);
    if not Result and ScheduleIfBusy then
    begin
      Result := MoveFileExW(Directory, 0, MoveFileDelayUntilReboot);
      if Result then
        RestartAfterUninstall := True;
    end;
    Exit;
  end;

  for Attempt := 1 to 20 do
  begin
    if not DirExists(Directory) then
    begin
      Result := True;
      Exit;
    end;
    if DeleteTreeSafely(Directory) and not DirExists(Directory) then
    begin
      Result := True;
      Exit;
    end;
    Sleep(250);
  end;

  if ScheduleIfBusy and DirExists(Directory) and ScheduleDeleteTree(Directory) then
  begin
    RestartAfterUninstall := True;
    Log('Scheduled locked Juju directory for deletion after restart: ' + Directory);
    Result := True;
  end
  else
    Result := not DirExists(Directory);
end;

function InitializeUninstall(): Boolean;
var
  Answer: Integer;
  LocalDataDirectory: String;
begin
  if not CloseJujuBeforeInstaller(True) then
  begin
    Result := False;
    Exit;
  end;
  if not AcquireInstallerLock() then
  begin
    if not UninstallSilent then
      MsgBox('无法取得应用装锁（Windows 错误码 ' + IntToStr(InstallerLockErrorCode) +
        '）。请确认应用已关闭且安装目录可写后重试。', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  KeepLocalAppDataOnUninstall := True;
  RestartAfterUninstall := False;
  Result := True;

  if UninstallSilent then
    Exit;

  LocalDataDirectory := ExpandConstant('{app}\data');
  Answer := MsgBox(
    '是否保留应用安装目录中的数据文件？' + #13#10 + #13#10 +
    '是：保留 ' + LocalDataDirectory + #13#10 +
    '否：删除该目录',
    mbConfirmation, MB_YESNOCANCEL or MB_DEFBUTTON1);

  if Answer = IDCANCEL then
  begin
    Result := False;
    Exit;
  end;
  KeepLocalAppDataOnUninstall := Answer = IDYES;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  AppDirectory: String;
  LocalDataDirectory: String;
  VersionsDirectory: String;
  StagingDirectory: String;
  StateDirectory: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  ReleaseInstallerLock();

  AppDirectory := ExpandConstant('{app}');
  LocalDataDirectory := PathCombine(AppDirectory, 'data');
  VersionsDirectory := PathCombine(AppDirectory, 'versions');
  StagingDirectory := PathCombine(AppDirectory, 'staging');
  StateDirectory := PathCombine(AppDirectory, 'state');

  if not KeepLocalAppDataOnUninstall then
    if not RemoveOwnedDirectory(LocalDataDirectory, True) then
      MsgBox('无法完整清理安装目录数据：' + LocalDataDirectory + #13#10 +
        '请关闭占用该目录的程序后手动删除。外部数据目录未被触碰。', mbError, MB_OK);

  { Version directories only contain immutable application files and per-version WebView caches. }
  if not RemoveOwnedDirectory(VersionsDirectory, True) then
    MsgBox('无法完整清理版本目录：' + VersionsDirectory + #13#10 +
      '请关闭占用该目录的程序后手动删除。', mbError, MB_OK);
  if not RemoveOwnedDirectory(StagingDirectory, True) then
    MsgBox('无法完整清理更新暂存目录：' + StagingDirectory, mbError, MB_OK);
  if not RemoveOwnedDirectory(StateDirectory, True) then
    MsgBox('无法完整清理更新状态目录：' + StateDirectory, mbError, MB_OK);

  { The pointer may identify an external data root. Remove the pointer only; never follow its target. }
  DeleteFile(PathCombine(AppDirectory, 'data-location.json'));
  DeleteFile(PathCombine(AppDirectory, 'active.json'));
  if not DirExists(LocalDataDirectory) then
  begin
    if not RemoveDir(AppDirectory) and RestartAfterUninstall then
      MoveFileExW(AppDirectory, 0, MoveFileDelayUntilReboot);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Root: String;
  CandidateExe: String;
  LauncherPath: String;
  ExitCode: Integer;
  CommandLine: String;
  ActiveAfter: String;
begin
  if CurStep <> ssPostInstall then Exit;

  { Release the deployment lock before launching Launcher, which acquires the same root lock. }
  ReleaseInstallerLock();

  Root := ExpandConstant('{app}');
  CandidateExe := PathCombine(PathCombine(PathCombine(Root, 'versions'), '{#AppVersion}.installer-stage'), 'Juju.App.exe');
  LauncherPath := PathCombine(Root, '{#LauncherExeName}');
  if not FileExists(CandidateExe) then
    RaiseException('新版本安装不完整：找不到 ' + CandidateExe);
  if not FileExists(LauncherPath) then
    RaiseException('安装不完整：找不到 Launcher。');

  { REQUIRED NEW LAUNCHER CONTRACT:
    --installer-activate --version <semver> --root <root>
    Verify installed payload, run a real app readiness probe (READY pipe),
    atomically activate or roll back, prune old versions and delete active.json.bak
    ONLY after verified success. Must exit nonzero on ANY incomplete step.
    No normal user-facing app instance must remain running after this probe.
  }
  CommandLine := '--installer-activate --version "{#AppVersion}" --root "' + Root +
    '" --manifest "' + PathCombine(PathCombine(Root, 'staging'), 'installer-payload-manifest.json') +
    '" --manifest-sha256 "{#ManifestSha256}"';
  Log('Starting installer activation and health check: ' + CommandLine);
  if not Exec(LauncherPath, CommandLine, Root, SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    RaiseException('无法执行 Launcher 安装验证程序。安装未完成，请检查安装日志。');
  if ExitCode = 20 then
    RaiseException('新版本已激活，但清理旧版本或备份未完成。请检查 state\\installer-activation.log 后重试清理。');
  if ExitCode <> 0 then
    RaiseException('新版本启动健康检查、版本激活或旧版本清理失败（错误码 ' +
      IntToStr(ExitCode) + '）。已请求 Launcher 保留/恢复旧版本。');

  ActiveAfter := ReadInstalledVersion(Root);
  if ActiveAfter <> '{#AppVersion}' then
    RaiseException('安装验证返回成功，但 active.json 未指向新版本。实际版本：' + ActiveAfter);
  Log('Installer activation succeeded: {#AppVersion}');
end;

function UninstallNeedRestart(): Boolean;
begin
  Result := RestartAfterUninstall;
end;
