; LX VPN Installer Script
#define MyAppName "LX VPN"
#define MyAppVersion "2.2.0"
#define MyAppExeName "LX VPN.exe"
#define SourceExeName "LX_VPN.exe"
#define SourceDir "c:\Users\iNDiK4\Desktop\vpn"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir={#SourceDir}\installer_output
OutputBaseFilename=LX_VPN_Setup_Minimal
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=admin

[Files]
Source: "{#SourceDir}\XrayLauncher\bin\Release\{#SourceExeName}"; DestDir: "{app}"; DestName: "{#MyAppExeName}"
Source: "{#SourceDir}\XrayLauncher\bin\Release\xray.exe"; DestDir: "{app}"
