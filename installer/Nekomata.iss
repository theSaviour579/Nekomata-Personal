#ifndef MyAppVersion
  #define MyAppVersion "0.3.1"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif

#define MyAppName "Nekomata Personal"
#define MyAppExeName "Nekomata.exe"

[Setup]
AppId={{5F882F30-5168-4F37-BBAA-2C67C03E7CF8}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=David Myers
DefaultDirName={localappdata}\Programs\Nekomata Personal
DefaultGroupName=Nekomata Personal
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=Nekomata-Personal-Setup-{#MyAppVersion}
SetupIconFile=..\Nekomata\Nekomata\Assets\Images\App.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
ChangesAssociations=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "startup"; Description: "Start Nekomata Personal when I sign in"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Nekomata Personal"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\Nekomata Personal"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Nekomata Personal"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Nekomata Personal"; Flags: nowait postinstall skipifsilent
