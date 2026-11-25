#define AppName "Empiro IP Scanner"
#define AppVersion "1.1.2"
#define AppPublisher "Empiro"
#define AppExeName "Empiro-IP-Scanner.exe"

[Setup]
AppId={{36AC4C8B-CB21-43D0-AE97-5AE80F17F51B}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\Empiro IP Scanner
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
OutputDir=dist
OutputBaseFilename=Empiro-IP-Scanner-Setup-{#AppVersion}-x64
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
WizardStyle=modern
Compression=lzma2
SolidCompression=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "dist\Empiro-IP-Scanner.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Empiro IP Scanner"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\Empiro IP Scanner"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch Empiro IP Scanner"; Flags: postinstall nowait skipifsilent
