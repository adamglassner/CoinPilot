; WillowWallet Server for Windows: installs the server as a Windows service (starts with Windows),
; opens port 8484 on private networks and opens the web panel.
;   iscc /DMyAppVersion=0.1.0 /DSourceDir=..\..\dist-server\win-x64 server\installer\WillowWalletServer.iss

#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\dist-server\win-x64"
#endif
#define MyAppName "WillowWallet Server"
#define MyExe "WillowWalletServer.exe"
#define Service "WillowWalletServer"

[Setup]
AppId={{6C4E2B1A-7F3D-4E85-9A2C-1D8B5F0E7A44}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Bytehawk Studio
AppPublisherURL=https://github.com/adamglassner/CoinPilot
DefaultDirName={autopf}\WillowWallet Server
DefaultGroupName=WillowWallet Server
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\..\dist-server
OutputBaseFilename=WillowWalletServer-{#MyAppVersion}-Setup
SetupIconFile=..\..\src\CoinPilot.Desktop\Assets\coinpilot.ico
UninstallDisplayIcon={app}\{#MyExe}
UninstallDisplayName={#MyAppName}
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
CloseApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "firewall"; Description: "Let phones and other computers on my network connect (open port 8484 on private networks)"

[Dirs]
Name: "{commonappdata}\WillowWallet Server"

[Files]
Source: "{#SourceDir}\{#MyExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\wwwroot\*"; DestDir: "{app}\wwwroot"; Flags: ignoreversion recursesubdirs createallsubdirs

[INI]
Filename: "{group}\WillowWallet Server panel.url"; Section: "InternetShortcut"; Key: "URL"; String: "http://localhost:8484"
Filename: "{autodesktop}\WillowWallet Server panel.url"; Section: "InternetShortcut"; Key: "URL"; String: "http://localhost:8484"

[Run]
Filename: "{sys}\sc.exe"; Parameters: "create {#Service} binPath= ""\""{app}\{#MyExe}\"""" start= delayed-auto DisplayName= ""{#MyAppName}"""; Flags: runhidden; Check: not ServiceExists
Filename: "{sys}\sc.exe"; Parameters: "description {#Service} ""Sync server for the WillowWallet budgeting app. Web panel: http://localhost:8484"""; Flags: runhidden
; The service's environment: where to keep data (set after the service exists)
Filename: "{sys}\reg.exe"; Parameters: "add ""HKLM\SYSTEM\CurrentControlSet\Services\{#Service}"" /v Environment /t REG_MULTI_SZ /d ""WW_DATA={commonappdata}\WillowWallet Server"" /f"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "failure {#Service} reset= 86400 actions= restart/3000/restart/3000/restart/10000"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "failureflag {#Service} 1"; Flags: runhidden
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""WillowWallet Server"""; Flags: runhidden; Tasks: firewall
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""WillowWallet Server"" dir=in action=allow protocol=TCP localport=8484 profile=private,domain"; Flags: runhidden; Tasks: firewall
Filename: "{sys}\sc.exe"; Parameters: "start {#Service}"; Flags: runhidden
Filename: "http://localhost:8484"; Description: "Open the WillowWallet Server web panel"; Flags: postinstall shellexec nowait skipifsilent

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop {#Service}"; Flags: runhidden; RunOnceId: "StopService"
Filename: "{sys}\timeout.exe"; Parameters: "/t 3 /nobreak"; Flags: runhidden; RunOnceId: "Wait"
Filename: "{sys}\sc.exe"; Parameters: "delete {#Service}"; Flags: runhidden; RunOnceId: "DeleteService"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""WillowWallet Server"""; Flags: runhidden; RunOnceId: "Firewall"

[Messages]
FinishedLabel=WillowWallet Server is installed and running as a Windows service. It starts with Windows, even before anyone signs in.%n%nYour data is kept in C:\ProgramData\WillowWallet Server (uninstalling keeps it).

[Code]
function ServiceExists: Boolean;
var Code: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\sc.exe'), 'query {#Service}', '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var Code: Integer;
begin
  // Upgrading: stop the running service so its files can be replaced
  if ServiceExists then
  begin
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#Service}', '', SW_HIDE, ewWaitUntilTerminated, Code);
    Sleep(3000);
  end;
  Result := '';
end;
