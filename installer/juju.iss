; Build after `dotnet publish src\Juju.App\Juju.App.csproj -c Release -r win-x64 --self-contained true -o artifacts\win-x64`.
#define AppName "juju"
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#define AppPublisher "juju"
#define AppExeName "Juju.App.exe"

#define AppMutexName "juju-single-instance-v1"

[Setup]
AppId={{3E2E8E5A-917A-46A0-BF5F-C4F2B3619FC5}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
; Juju stores its configuration and data beside the executable, so install per-user.
DefaultDirName={localappdata}\Programs\juju
DefaultGroupName=juju
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
; The app owns this mutex while running; Inno Setup refuses to install/uninstall until it exits.
AppMutex={#AppMutexName}
CloseApplications=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=juju_v{#AppVersion}_setup_win64
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName=juju
Compression=lzma2
SolidCompression=yes

[Files]
Source: "..\artifacts\win-x64\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"

[Icons]
Name: "{group}\juju"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\juju"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Parameters: "--show-launcher"; WorkingDir: "{app}"; Description: "启动 juju"; Flags: nowait postinstall skipifsilent

[Code]
const
  FileAttributeDirectory = $10;
  FileAttributeReparsePoint = $400;
  MoveFileDelayUntilReboot = $4;

var
  KeepLocalAppDataOnUninstall: Boolean;
  RestartAfterUninstall: Boolean;

function MoveFileExW(ExistingFileName: String; NewFileName: Cardinal; Flags: Cardinal): LongBool;
  external 'MoveFileExW@kernel32.dll stdcall';
function GetFileAttributesW(FileName: String): Cardinal;
  external 'GetFileAttributesW@kernel32.dll stdcall';

function IsReparsePoint(const Path: String): Boolean;
var
  Attributes: Cardinal;
begin
  Attributes := GetFileAttributesW(Path);
  Result := (Attributes <> $FFFFFFFF) and
    ((Attributes and FileAttributeReparsePoint) <> 0);
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
  KeepLocalAppDataOnUninstall := True;
  RestartAfterUninstall := False;
  Result := True;

  if UninstallSilent then
    Exit;

  LocalDataDirectory := ExpandConstant('{app}\data');
  Answer := MsgBox(
    '是否保留 Juju 在安装目录中的数据？' + #13#10 + #13#10 +
    '是：保留 ' + LocalDataDirectory + #13#10 +
    '否：删除该目录。WebView2 数据无论如何都会清理。' + #13#10 + #13#10 +
    '设置中单独指定的应用或工具数据目录不会被删除。' + #13#10 +
    '外部数据目录的路径指针会移除；重装后如需继续使用该目录，请重新选择。',
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
  EditorDirectory: String;
  WebViewDataDirectory: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  AppDirectory := ExpandConstant('{app}');
  LocalDataDirectory := PathCombine(AppDirectory, 'data');
  EditorDirectory := PathCombine(AppDirectory, 'Editor');
  WebViewDataDirectory := PathCombine(AppDirectory, '{#AppExeName}.WebView2');

  if not KeepLocalAppDataOnUninstall then
    if not RemoveOwnedDirectory(LocalDataDirectory, True) then
      MsgBox('无法完整清理安装目录数据：' + LocalDataDirectory + #13#10 +
        '请关闭占用该目录的程序后手动删除。外部数据目录未被触碰。', mbError, MB_OK);

  if not RemoveOwnedDirectory(WebViewDataDirectory, True) then
    MsgBox('无法完整清理 WebView2 数据：' + WebViewDataDirectory + #13#10 +
      '请关闭占用该目录的 WebView2 进程后手动删除。', mbError, MB_OK);

  if not RemoveOwnedDirectory(EditorDirectory, True) then
    MsgBox('无法完整清理编辑器资源目录：' + EditorDirectory, mbError, MB_OK);

  { The pointer may identify an external data root. Remove the pointer only; never follow its target. }
  DeleteFile(PathCombine(AppDirectory, 'data-location.json'));
  if not DirExists(LocalDataDirectory) then
  begin
    if not RemoveDir(AppDirectory) and RestartAfterUninstall then
      MoveFileExW(AppDirectory, 0, MoveFileDelayUntilReboot);
  end;
end;

function UninstallNeedRestart(): Boolean;
begin
  Result := RestartAfterUninstall;
end;
