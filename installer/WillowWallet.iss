; Inno Setup script for WillowWallet (called CoinPilot before 1.0.2; same AppId so it upgrades in place)
; Build: run ..\build.ps1, or compile this file with Inno Setup 6 (ISCC.exe) after publishing.
;
; Installs per-user by default (no administrator prompt). The first page lets people choose
; "all users" instead, which installs under Program Files.
;
; The Bytehawk Studio Launcher runs this setup silently to install and update WillowWallet:
;   WillowWallet-<version>-Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /NOICONS /DIR="<folder>"

#define MyAppName "WillowWallet"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppPublisher "Bytehawk Studio"
#define MyAppExeName "WillowWallet.exe"
#define MyAppUrl "https://github.com/adamglassner/coinpilot"
#ifndef PublishDir
  #define PublishDir "..\dist\publish"
#endif

[Setup]
AppId={{B50AC4E8-8BC5-4292-B42D-0A408646C774}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}/issues
AppUpdatesURL={#MyAppUrl}/releases
AppCopyright=© 2026 Bytehawk Studio
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
DefaultDirName={autopf}\WillowWallet
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; The app was called CoinPilot before 1.0.2: put shortcuts under the new name instead of the old Start Menu folder
UsePreviousGroup=no
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\dist
OutputBaseFilename=WillowWallet-{#MyAppVersion}-Setup
SetupIconFile=..\src\CoinPilot.Desktop\Assets\coinpilot.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
LicenseFile=..\LICENSE
WizardStyle=modern
WizardImageFile=wizard-large-1.bmp,wizard-large-2.bmp
WizardSmallImageFile=wizard-small-55.bmp,wizard-small-110.bmp
Compression=lzma2/max
SolidCompression=yes
; Ask to close WillowWallet if it's running (Windows Restart Manager)
CloseApplications=yes

[InstallDelete]
; Leftovers from when the app was called CoinPilot (before 1.0.2)
Type: files; Name: "{app}\CoinPilot.exe"
Type: filesandordirs; Name: "{autoprograms}\CoinPilot"
Type: files; Name: "{autoprograms}\CoinPilot.lnk"
Type: files; Name: "{autodesktop}\CoinPilot.lnk"

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#PublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

; NOTE: There is deliberately no [UninstallDelete] section.
; Uninstalling removes the program and its shortcuts only. Settings in
; %APPDATA%\WillowWallet\ are left alone so a reinstall picks up where you left off.
