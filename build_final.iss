#define MyAppName "LX VPN"
#define MyAppVersion "2.2.0"
#define MyAppExeName "LX_VPN.exe"
#define AppExeName "LX VPN.exe"
#define SourceDir "c:\Users\iNDiK4\Desktop\vpn"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir={#SourceDir}\installer_output
OutputBaseFilename=LXVPN_Setup_v2.2.0
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=admin
DisableWelcomePage=no
DisableDirPage=no
DisableProgramGroupPage=yes

[Files]
; Source is LX_VPN.exe, Dest is LX VPN.exe (renaming during install)
Source: "{#SourceDir}\XrayLauncher\bin\Release\{#MyAppExeName}"; DestDir: "{app}"; DestName: "{#AppExeName}"; Flags: ignoreversion
Source: "{#SourceDir}\XrayLauncher\bin\Release\xray.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\XrayLauncher\bin\Release\blocked-domains.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#AppExeName}"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch LX VPN"; Flags: nowait postinstall skipifsilent
