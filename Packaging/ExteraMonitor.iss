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

[CustomMessages]
english.NetRuntimePageCaption=Downloading .NET 10 runtime
english.NetRuntimePageDescription=Extera Monitor needs the .NET 10 runtime. Setup downloads it from Microsoft only if it is missing.
english.NetRuntimeDownloadCancelled=The .NET runtime download was cancelled.
english.NetRuntimeDownloadFailed=Could not download the .NET runtime: %s
english.NetRuntimeStartFailed=The .NET runtime installer could not be started.
english.NetRuntimeInstallFailed=.NET 10 runtime installation failed with exit code %d.
english.NetRuntimeBusyRetry=Windows is already installing or updating another program. Let it finish, then choose Retry to reopen the .NET installer.
english.NetRuntimeStillMissing=The .NET 10 runtime is still missing. Retry the download and install step.
ukrainian.NetRuntimePageCaption=Завантаження .NET 10 Runtime
ukrainian.NetRuntimePageDescription=Extera Monitor потребує .NET 10 Runtime. Інсталятор завантажить його з Microsoft, якщо середовища ще немає.
ukrainian.NetRuntimeDownloadCancelled=Завантаження .NET Runtime скасовано.
ukrainian.NetRuntimeDownloadFailed=Не вдалося завантажити .NET Runtime: %s
ukrainian.NetRuntimeStartFailed=Не вдалося запустити інсталятор .NET Runtime.
ukrainian.NetRuntimeInstallFailed=Не вдалося встановити .NET 10 Runtime. Код завершення: %d.
ukrainian.NetRuntimeBusyRetry=Windows уже встановлює або оновлює іншу програму. Дочекайся завершення, потім натисни «Повторити», щоб знову відкрити інсталятор .NET.
ukrainian.NetRuntimeStillMissing=Середовище .NET 10 досі не встановлено. Повтори завантаження та встановлення.

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
var
  RuntimeDownloadPage: TDownloadWizardPage;

procedure InitializeWizard;
begin
  RuntimeDownloadPage := CreateDownloadPage(CustomMessage('NetRuntimePageCaption'), CustomMessage('NetRuntimePageDescription'), nil);
  RuntimeDownloadPage.ShowBaseNameInsteadOfUrl := True;
end;

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

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ErrorMessage: String;
  ResultCode: Integer;
begin
  Result := True;
  if (CurPageID <> wpReady) or HasNet10Runtime then
    Exit;

  RuntimeDownloadPage.Clear;
  RuntimeDownloadPage.Add('https://aka.ms/dotnet/10.0/dotnet-runtime-win-x64.exe', 'dotnet-runtime-10-x64.exe', '');
  RuntimeDownloadPage.Show;
  try
    try
      RuntimeDownloadPage.Download;
    except
      if RuntimeDownloadPage.AbortedByUser then
        ErrorMessage := CustomMessage('NetRuntimeDownloadCancelled')
      else
        ErrorMessage := Format(CustomMessage('NetRuntimeDownloadFailed'), [GetExceptionMessage]);
      SuppressibleMsgBox(AddPeriod(ErrorMessage), mbCriticalError, MB_OK, IDOK);
      Result := False;
      Exit;
    end;
  finally
    RuntimeDownloadPage.Hide;
  end;

  if not Result then
    Exit;

  if not Exec(ExpandConstant('{tmp}\dotnet-runtime-10-x64.exe'), '', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
  begin
    SuppressibleMsgBox(CustomMessage('NetRuntimeStartFailed'), mbCriticalError, MB_OK, IDOK);
    Result := False;
    Exit;
  end;

  while (ResultCode = 1618) and not HasNet10Runtime do
  begin
    if SuppressibleMsgBox(CustomMessage('NetRuntimeBusyRetry'), mbError, MB_RETRYCANCEL, IDRETRY) <> IDRETRY then
    begin
      Result := False;
      Exit;
    end;

    if not Exec(ExpandConstant('{tmp}\dotnet-runtime-10-x64.exe'), '', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
    begin
      SuppressibleMsgBox(CustomMessage('NetRuntimeStartFailed'), mbCriticalError, MB_OK, IDOK);
      Result := False;
      Exit;
    end;
  end;

  if (ResultCode <> 0) and (ResultCode <> 3010) and not HasNet10Runtime then
  begin
    SuppressibleMsgBox(Format(CustomMessage('NetRuntimeInstallFailed'), [ResultCode]), mbCriticalError, MB_OK, IDOK);
    Result := False;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not HasNet10Runtime then
    Result := CustomMessage('NetRuntimeStillMissing');
end;
