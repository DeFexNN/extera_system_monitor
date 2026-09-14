#ifndef AppVersion
  #define AppVersion "0.2.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\stage\ExteraMonitor"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

[Setup]
AppId={{6B775196-718A-4B1B-89C3-DC2A06EA37A7}
AppName=Extera Monitor
AppVersion={#AppVersion}
AppPublisher=DeFexNN
AppPublisherURL=https://github.com/DeFexNN/extera_system_monitor
AppSupportURL=https://github.com/DeFexNN/extera_system_monitor/issues
AppUpdatesURL=https://github.com/DeFexNN/extera_system_monitor/releases
DefaultDirName={autopf}\Extera Monitor
DefaultGroupName=Extera Monitor
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=ExteraMonitor-Setup-v{#AppVersion}-win-x64
SetupIconFile=..\Assets\extera-monitor.ico
UninstallDisplayIcon={app}\ExteraMonitor.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany=DeFexNN
VersionInfoDescription=Extera Monitor installer
VersionInfoProductName=Extera Monitor
VersionInfoProductVersion={#AppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "ukrainian"; MessagesFile: "compiler:Languages\Ukrainian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Extera Monitor"; Filename: "{app}\ExteraMonitor.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Extera Monitor"; Filename: "{app}\ExteraMonitor.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\ExteraMonitor.exe"; Description: "{cm:LaunchProgram,Extera Monitor}"; Flags: nowait postinstall skipifsilent
