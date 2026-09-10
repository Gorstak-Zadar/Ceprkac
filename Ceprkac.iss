; Ceprkac Inno Setup Script
#define MyAppName "Ceprkac"
#define MyAppVersion "0.8.8"
#define MyAppPublisher "Ceprkac"
#define MyAppExeName "Ceprkac.exe"
#define MyAppIcon "Ceprkac.ico"

[Setup]
AppId={{8a7b3c2d-1e4f-5a6b-9c8d-7e0f1a2b3c4d}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppIcon}
SetupIconFile={#MyAppIcon}
Compression=lzma2
SolidCompression=yes
OutputDir=releases\{#MyAppVersion}
OutputBaseFilename=Ceprkac-{#MyAppVersion}-Setup
ArchitecturesInstallIn64BitMode=x64
WizardStyle=modern
ChangesAssociations=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "defaultbrowser"; Description: "Set Ceprkac as the default browser"; GroupDescription: "Additional options:"; Flags: checkedonce

[Files]
Source: "bin\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\publish\{#MyAppExeName}.config"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "bin\publish\{#MyAppIcon}"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\publish\*.dll"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs
Source: "bin\publish\*.json"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "bin\publish\blocklist.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppIcon}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppIcon}"; Tasks: desktopicon

[Registry]
; Appear in Windows Settings > Default apps
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\Ceprkac"; ValueType: string; ValueName: ""; ValueData: "Ceprkac"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\Ceprkac"; ValueType: string; ValueName: "LocalizedString"; ValueData: "Ceprkac"
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\Ceprkac\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\Ceprkac\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"""
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\Ceprkac\InstallInfo"; ValueType: string; ValueName: "ReinstallCommand"; ValueData: """{app}\{#MyAppExeName}"" --register-browser"
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\Ceprkac\InstallInfo"; ValueType: string; ValueName: "HideIconsCommand"; ValueData: """{app}\{#MyAppExeName}"" --register-browser"
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\Ceprkac\InstallInfo"; ValueType: string; ValueName: "ShowIconsCommand"; ValueData: """{app}\{#MyAppExeName}"" --register-browser"
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\Ceprkac\InstallInfo"; ValueType: dword; ValueName: "IconsVisible"; ValueData: 1
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\Ceprkac\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "Ceprkac"
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\Ceprkac\Capabilities"; ValueType: string; ValueName: "ApplicationIcon"; ValueData: "{app}\{#MyAppExeName},0"
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\Ceprkac\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "Ceprkac web browser"
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\Ceprkac\Capabilities\StartMenu"; ValueType: string; ValueName: "StartMenuInternet"; ValueData: "Ceprkac"
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\Ceprkac\Capabilities\URLAssociations"; ValueType: string; ValueName: "http"; ValueData: "CeprkacURL"
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\Ceprkac\Capabilities\URLAssociations"; ValueType: string; ValueName: "https"; ValueData: "CeprkacURL"
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\Ceprkac\Capabilities\FileAssociations"; ValueType: string; ValueName: ".htm"; ValueData: "CeprkacHTML"
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\Ceprkac\Capabilities\FileAssociations"; ValueType: string; ValueName: ".html"; ValueData: "CeprkacHTML"
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\Ceprkac\Capabilities\FileAssociations"; ValueType: string; ValueName: ".shtml"; ValueData: "CeprkacHTML"
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\Ceprkac\Capabilities\FileAssociations"; ValueType: string; ValueName: ".xhtml"; ValueData: "CeprkacHTML"
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\Ceprkac\Capabilities\FileAssociations"; ValueType: string; ValueName: ".xht"; ValueData: "CeprkacHTML"
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\Ceprkac\Capabilities\FileAssociations"; ValueType: string; ValueName: ".svg"; ValueData: "CeprkacHTML"
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\Ceprkac\Capabilities\FileAssociations"; ValueType: string; ValueName: ".webp"; ValueData: "CeprkacHTML"
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\Ceprkac\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mht"; ValueData: "CeprkacHTML"
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\Ceprkac\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mhtml"; ValueData: "CeprkacHTML"
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\Ceprkac\Capabilities\MimeAssociations"; ValueType: string; ValueName: "text/html"; ValueData: "CeprkacHTML"
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\Ceprkac\Capabilities\MimeAssociations"; ValueType: string; ValueName: "application/xhtml+xml"; ValueData: "CeprkacHTML"
Root: HKLM; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "Ceprkac"; ValueData: "Software\Clients\StartMenuInternet\Ceprkac\Capabilities"; Flags: uninsdeletevalue

Root: HKLM; Subkey: "Software\Classes\CeprkacURL"; ValueType: string; ValueName: ""; ValueData: "Ceprkac URL"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\CeprkacURL"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKLM; Subkey: "Software\Classes\CeprkacURL\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"
Root: HKLM; Subkey: "Software\Classes\CeprkacURL\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""

Root: HKLM; Subkey: "Software\Classes\CeprkacHTML"; ValueType: string; ValueName: ""; ValueData: "Ceprkac HTML Document"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\CeprkacHTML\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"
Root: HKLM; Subkey: "Software\Classes\CeprkacHTML\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""

Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\{#MyAppExeName}"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\{#MyAppExeName}"; ValueType: string; ValueName: "Path"; ValueData: "{app}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--register-browser"; StatusMsg: "Registering as default browser..."; Flags: waituntilterminated; Tasks: defaultbrowser
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
