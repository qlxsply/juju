; Build after `dotnet publish src\Juju.App\Juju.App.csproj -c Release -r win-x64 --self-contained true -o artifacts\win-x64`.
#define AppName "juju"
#define AppVersion "0.1.0"
#define AppPublisher "juju"
#define AppExeName "Juju.App.exe"

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
OutputDir=..\artifacts\installer
OutputBaseFilename=juju-setup-win-x64
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
