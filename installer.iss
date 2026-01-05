; LX VPN Installer Script
#define MyAppName "LX VPN"
#define MyAppVersion "2.2.0"
#define MyAppPublisher "LX VPN"
#define MyAppURL "https://github.com/iNDiK4/lx-vpn-releases"
#define MyAppExeName "LX VPN.exe"
#define SourceExeName "LX_VPN.exe"
#define SourceDir "c:\Users\iNDiK4\Desktop\vpn"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir={#SourceDir}\installer_output
OutputBaseFilename=LX_VPN_Setup_{#MyAppVersion}
SetupIconFile={#SourceDir}\XrayLauncher\icon.ico
WizardStyle=modern
WizardSizePercent=100
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checked
Name: "autostart"; Description: "Запускать при старте Windows"; GroupDescription: "Дополнительно:"; Flags: unchecked

[Files]
; Note: Source file is LX_VPN.exe but we install it as LX VPN.exe
Source: "{#SourceDir}\XrayLauncher\bin\Release\{#SourceExeName}"; DestDir: "{app}"; DestName: "{#MyAppExeName}"; Flags: ignoreversion
Source: "{#SourceDir}\XrayLauncher\bin\Release\xray.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\XrayLauncher\bin\Release\blocked-domains.txt"; DestDir: "{app}"; Flags: ignoreversion onlyifdoesntexist

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Удалить {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: files; Name: "{app}\config.json"
Type: files; Name: "{app}\settings.json"
Type: files; Name: "{app}\last_vless.txt"
Type: dirifempty; Name: "{app}"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  mRes: Integer;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Ask to remove settings in Russian? Or use custom msg.
    // For simplicity using standard MsgBox. 
    // In Russian: "Удалить все настройки и данные LX VPN?"
    mRes := MsgBox('Удалить все настройки и данные LX VPN?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2);
    if mRes = IDYES then
    begin
      DelTree(ExpandConstant('{app}'), True, True, True);
    end;
  end;
end;
