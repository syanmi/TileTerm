; Inno Setup 6.3+ script for the TileTerm installer.
; Built by scripts\build_release.ps1, which passes the version and the publish folder:
;   ISCC /DAppVersion=0.2.0 /DSourceDir=<publish folder> /DOutputDir=<output folder> TileTerm.iss

#ifndef AppVersion
  #error AppVersion is required, e.g. ISCC /DAppVersion=0.2.0 ...
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

#define AppName "TileTerm"
#define AppExe "TileTerm.exe"
#define AppPublisher "syanmi"
#define AppURL "https://github.com/syanmi/TileTerm"

[Setup]
; AppId identifies the app to Windows (upgrades, uninstall entry). Never change it between releases.
AppId={{22CA0301-3E0E-48BB-A542-641975EC1032}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
VersionInfoVersion={#AppVersion}

; Per-user install by default (no administrator prompt, goes to %LOCALAPPDATA%\Programs\TileTerm);
; the wizard still offers "for all users" for those who want it.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes

; 64-bit Windows 10 version 1809 (build 17763) or newer: the pseudo console (ConPTY) the app relies on.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763

OutputDir={#OutputDir}
OutputBaseFilename=TileTerm-v{#AppVersion}-win-x64-setup
SetupIconFile=..\src\TileTerm\Assets\TileTerm.ico
UninstallDisplayIcon={app}\{#AppExe}
LicenseFile=..\LICENSE
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

; Uninstalling removes only what the installer put in {app}. The user's profiles and settings live in
; %AppData%\TileTerm and are deliberately kept, so a reinstall picks up where the user left off.
