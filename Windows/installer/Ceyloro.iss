; Script Inno Setup untuk Ceyloro Windows
#define MyAppName "Ceyloro"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "Ceyloro"
#define MyAppURL "https://github.com/ceyloro/ceyloro"
#define MyAppExeName "Ceyloro.exe"

#ifndef AppArch
  #define AppArch "x64"
#endif

[Setup]
AppId={{C17A1689-1304-4F54-9EE9-C59A440D908E}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Izinkan instalasi tanpa hak Administrator (non-admin per-user)
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputBaseFilename=Ceyloro-Windows-{#AppArch}-Setup
OutputDir=..\..\dist
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64 arm64
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupicon"; Description: "Jalankan Ceyloro saat Windows dimulai (system tray)"; GroupDescription: "Opsi Startup:"

[Files]
Source: "..\..\package\Ceyloro\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: startupicon; Flags: uninsdeletevalue
; Context Menu Explorer (File & Folder)
Root: HKCU; Subkey: "Software\Classes\*\shell\Ceyloro"; ValueType: string; ValueName: ""; ValueData: "Buka dengan Roda Ceyloro"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\*\shell\Ceyloro"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"
Root: HKCU; Subkey: "Software\Classes\*\shell\Ceyloro\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --files ""%1"""
Root: HKCU; Subkey: "Software\Classes\Directory\shell\Ceyloro"; ValueType: string; ValueName: ""; ValueData: "Buka dengan Roda Ceyloro"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\shell\Ceyloro"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"
Root: HKCU; Subkey: "Software\Classes\Directory\shell\Ceyloro\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --files ""%1"""

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

