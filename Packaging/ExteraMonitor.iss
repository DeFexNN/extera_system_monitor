#ifndef AppVersion
  #define AppVersion "0.2.1"
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
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Extera Monitor\Extera Monitor"; Filename: "{app}\ExteraMonitor.exe"; WorkingDir: "{app}"
Name: "{autoprograms}\Extera Monitor\Uninstall Extera Monitor"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Extera Monitor"; Filename: "{app}\ExteraMonitor.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\ExteraMonitor.exe"; Description: "{cm:LaunchProgram,Extera Monitor}"; Flags: nowait postinstall skipifsilent

[Code]
function HasNet10Runtime: Boolean;
var
  SearchRec: TFindRec;
  RuntimePath: String;
begin
  Result := False;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.NETCore.App\10.*'), SearchRec) then
  begin
    try
      repeat
        if (SearchRec.Attributes and FILE_ATTRIBUTE_DIRECTORY <> 0) and
           (SearchRec.Name <> '.') and (SearchRec.Name <> '..') and
           (Pos('-', SearchRec.Name) = 0) then
        begin
          RuntimePath := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.NETCore.App\') + SearchRec.Name;
          if FileExists(RuntimePath + '\coreclr.dll') then
          begin
            Result := True;
            Exit;
          end;
        end;
      until not FindNext(SearchRec);
    finally
      FindClose(SearchRec);
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  RuntimeInstaller: String;
  ResultCode: Integer;
begin
  Result := '';
  if HasNet10Runtime then
    Exit;

  RuntimeInstaller := ExpandConstant('{tmp}\dotnet-runtime-10-x64.exe');
  try
    DownloadTemporaryFile('https://aka.ms/dotnet/10.0/dotnet-runtime-win-x64.exe', 'dotnet-runtime-10-x64.exe', '', nil);
  except
    Result := 'The .NET 10 runtime is missing and could not be downloaded. Check your internet connection, then run Setup again.';
    Exit;
  end;

  if not Exec(RuntimeInstaller, '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Result := 'The .NET 10 runtime installer could not be started. Run Setup again as an administrator.';
    Exit;
  end;

  if (ResultCode <> 0) and (ResultCode <> 3010) then
    Result := Format('.NET 10 runtime installation failed with exit code %d.', [ResultCode]);
end;
