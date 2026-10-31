; Light Downloader setup wizard (Inno Setup 6). Build with build-installer.ps1.

#define AppName "Light Downloader"
#define AppExe "LightDownloader.exe"
#define Root SourcePath + ".."
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
; Keep AppId stable: it is how an update finds the existing install.
AppId={{FBFF579A-7A2C-485E-80D7-D339E539ED60}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Light Downloader
AppPublisherURL=https://beliaja.id/light-downloader/
AppSupportURL=https://beliaja.id/light-downloader/
VersionInfoVersion={#AppVersion}
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DisableWelcomePage=no
DisableDirPage=no
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
WizardStyle=modern
WizardImageFile=wizard-side-202.bmp,wizard-side-253.bmp,wizard-side-303.bmp,wizard-side-404.bmp
WizardSmallImageFile=wizard-corner-58.bmp,wizard-corner-73.bmp,wizard-corner-87.bmp,wizard-corner-116.bmp
SetupIconFile={#Root}\src\LightDownloader.App\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir={#Root}\installer
OutputBaseFilename=LightDownloaderSetup
Compression=lzma2/ultra64
SolidCompression=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "indonesian"; MessagesFile: "Indonesian.isl"

[CustomMessages]
english.RuntimeDownloadTitle=Downloading .NET 8 Desktop Runtime
english.RuntimeDownloadDesc=Light Downloader needs this free Microsoft component (about 60 MB).
english.RuntimeReady=Also download and install the .NET 8 Desktop Runtime from Microsoft (about 60 MB).
english.RuntimeFailed=The .NET 8 Desktop Runtime could not be installed, so Light Downloader will not start yet.%n%nSetup will still install the app. Install the runtime (Desktop Runtime, x64) afterwards from:%nhttps://dotnet.microsoft.com/download/dotnet/8.0
english.AppRunning=Light Downloader is running and will be closed now.%n%nDownloads in progress will stop and can be resumed later.
english.AppStillRunning=Light Downloader is still running. Close it from its tray icon, then try again.
english.OpenExtensionPage=Open the browser extension page
english.RemoveUserData=Also delete your Light Downloader settings, download list and its downloaded tools (ffmpeg, yt-dlp)?%n%nFiles you downloaded are not affected.
indonesian.RuntimeDownloadTitle=Mengunduh .NET 8 Desktop Runtime
indonesian.RuntimeDownloadDesc=Light Downloader memerlukan komponen gratis dari Microsoft ini (sekitar 60 MB).
indonesian.RuntimeReady=Mengunduh dan memasang juga .NET 8 Desktop Runtime dari Microsoft (sekitar 60 MB).
indonesian.RuntimeFailed=.NET 8 Desktop Runtime tidak berhasil dipasang, jadi Light Downloader belum bisa dijalankan.%n%nAplikasi tetap akan dipasang. Pasang runtime-nya (Desktop Runtime, x64) nanti dari:%nhttps://dotnet.microsoft.com/download/dotnet/8.0
indonesian.AppRunning=Light Downloader sedang berjalan dan akan ditutup sekarang.%n%nUnduhan yang sedang berjalan akan berhenti dan bisa dilanjutkan nanti.
indonesian.AppStillRunning=Light Downloader masih berjalan. Tutup dari ikon di tray, lalu coba lagi.
indonesian.OpenExtensionPage=Buka halaman ekstensi browser
indonesian.RemoveUserData=Hapus juga pengaturan, daftar unduhan, dan alat yang diunduh Light Downloader (ffmpeg, yt-dlp)?%n%nBerkas hasil unduhan Anda tidak terpengaruh.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[InstallDelete]
; Leftovers from the old console installer (1.1.0 and earlier).
Type: files; Name: "{app}\LightDownloaderSetup.exe"
Type: files; Name: "{userdesktop}\{#AppName}.lnk"; Check: HasLegacyInstall

[Files]
Source: "{#Root}\publish\stage\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[UninstallDelete]
; Folders the old console installer created aren't owned by this uninstaller, so clear them if empty.
Type: dirifempty; Name: "{app}\extension\chrome\icons"
Type: dirifempty; Name: "{app}\extension\chrome"
Type: dirifempty; Name: "{app}\extension\firefox\icons"
Type: dirifempty; Name: "{app}\extension\firefox"
Type: dirifempty; Name: "{app}\extension"
Type: dirifempty; Name: "{app}"

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent; Check: RuntimeInstalled
Filename: "https://beliaja.id/light-downloader/extension/"; Description: "{cm:OpenExtensionPage}"; Flags: nowait postinstall shellexec skipifsilent unchecked

[Code]
const
  AppMutex = 'LightDownloader_SingleInstance_8F2A';
  ExitSignal = 'LightDownloader_Exit_8F2A';
  LegacyKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\LightDownloader';
  RuntimeUrl = 'https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe';
  RuntimeFile = 'windowsdesktop-runtime-8-win-x64.exe';
  EVENT_MODIFY_STATE = $0002;

var
  DownloadPage: TDownloadWizardPage;
  RuntimeDownloaded, RuntimeFailureShown: Boolean;

function OpenEvent(dwDesiredAccess: DWORD; bInheritHandle: BOOL; lpName: String): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(hEvent: THandle): BOOL;
  external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(hObject: THandle): BOOL;
  external 'CloseHandle@kernel32.dll stdcall';

function HasLegacyInstall: Boolean;
begin
  Result := RegKeyExists(HKCU, LegacyKey);
end;

function RuntimeInstalled: Boolean;
var
  Found: TFindRec;
begin
  Result := FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\8.*'), Found);
  if Result then
    FindClose(Found);
end;

function IsAppRunning: Boolean;
begin
  Result := CheckForMutexes(AppMutex);
end;

{ Asks a running copy to exit by itself (it saves its state first) and waits up to 10 s. }
procedure CloseRunningApp;
var
  Handle: THandle;
  I: Integer;
begin
  Handle := OpenEvent(EVENT_MODIFY_STATE, False, ExitSignal);
  if Handle = 0 then
    Exit;
  SetEvent(Handle);
  CloseHandle(Handle);
  for I := 1 to 50 do begin
    if not IsAppRunning then
      Break;
    Sleep(200);
  end;
end;

procedure ShowRuntimeFailure;
begin
  if not RuntimeFailureShown then
    SuppressibleMsgBox(CustomMessage('RuntimeFailed'), mbError, MB_OK, IDOK);
  RuntimeFailureShown := True;
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(CustomMessage('RuntimeDownloadTitle'), CustomMessage('RuntimeDownloadDesc'), nil);
  DownloadPage.ShowBaseNameInsteadOfUrl := True;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := MemoDirInfo;
  if MemoTasksInfo <> '' then
    Result := Result + NewLine + NewLine + MemoTasksInfo;
  if not RuntimeInstalled then
    Result := Result + NewLine + NewLine + CustomMessage('RuntimeReady');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID <> wpReady) or WizardSilent or RuntimeInstalled then
    Exit;
  DownloadPage.Clear;
  DownloadPage.Add(RuntimeUrl, RuntimeFile, '');
  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
      RuntimeDownloaded := True;
    except
      Log('Runtime download failed: ' + GetExceptionMessage);
      ShowRuntimeFailure;
    end;
  finally
    DownloadPage.Hide;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  Result := '';
  if IsAppRunning then begin
    if SuppressibleMsgBox(CustomMessage('AppRunning'), mbInformation, MB_OKCANCEL, IDOK) <> IDOK then begin
      Result := CustomMessage('AppStillRunning');
      Exit;
    end;
    { A copy that predates the exit signal stays open here; Restart Manager handles it next. }
    CloseRunningApp;
  end;
  if RuntimeDownloaded then begin
    { The runtime installs machine-wide, so this alone asks for elevation (UAC). }
    if not ShellExec('runas', ExpandConstant('{tmp}\' + RuntimeFile), '/install /passive /norestart', '',
                     SW_SHOWNORMAL, ewWaitUntilTerminated, Code) then
      Code := -1;
    Log('Runtime installer exit code: ' + IntToStr(Code));
    if (Code <> 0) and (Code <> 3010) then
      ShowRuntimeFailure;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and HasLegacyInstall then
    RegDeleteKeyIncludingSubkeys(HKCU, LegacyKey);
end;

function InitializeUninstall: Boolean;
begin
  Result := True;
  if not IsAppRunning then
    Exit;
  if SuppressibleMsgBox(CustomMessage('AppRunning'), mbInformation, MB_OKCANCEL, IDOK) <> IDOK then begin
    Result := False;
    Exit;
  end;
  CloseRunningApp;
  if IsAppRunning then begin
    SuppressibleMsgBox(CustomMessage('AppStillRunning'), mbError, MB_OK, IDOK);
    Result := False;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
    if MsgBox(CustomMessage('RemoveUserData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{userappdata}\LightDownloader'), True, True, True);
end;
