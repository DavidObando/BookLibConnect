#define MyAppSetupName 'Oahu'
#ifndef MyAppVersion
  #define MyAppVersion '1.0.0'
#endif
#define MyProgramExe = 'Oahu.exe'
#define MyCompany = 'DavidObando'
#define MyAppName = 'Oahu'
#ifndef MySourceDir
  #define MySourceDir '..\Connect.app.gui.core\bin\Release\net10.0-windows\publish'
#endif
#ifndef MyArchitecture
  #define MyArchitecture 'x64'
#endif

[Setup]
AppName={#MyAppSetupName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppSetupName} {#MyAppVersion}
AppCopyright=Copyright � 2026 {#MyCompany}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyCompany}
AppPublisher={#MyCompany}
AppPublisherURL=https://github.com/{#MyCompany}/{#MyAppName}

OutputBaseFilename={#MyAppName}-{#MyAppVersion}-win-{#MyArchitecture}-Setup
DefaultGroupName={#MyCompany}
DefaultDirName={autopf}\{#MyAppSetupName}
UninstallDisplayIcon={app}\{#MyProgramExe}
SourceDir={#MySourceDir}
OutputDir=..\..\..\Setup
SolidCompression=yes

DisableWelcomePage=no
WizardStyle=modern
AllowNoIcons=yes
DisableDirPage=yes
DisableProgramGroupPage=yes

PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
UsePreviousPrivileges=yes

ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: en; MessagesFile: "compiler:Default.isl"
;Name: de; MessagesFile: "compiler:languages\German.isl"

[CustomMessages]
en.MyDocName=Manual
en.MyDocFile={#MyAppName}.pdf

;de.MyDocName=Anleitung
;de.MyDocFile={#MyAppName}.de.pdf


[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "quicklaunchicon"; Description: "{cm:CreateQuickLaunchIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "*.exe"; DestDir: "{app}"
Source: "*.dll"; DestDir: "{app}"
;Source: "*.pdf"; DestDir: "{app}"
Source: "*.json"; DestDir: "{app}"

[Icons]
Name: "{group}\{#MyAppSetupName}"; Filename: "{app}\{#MyProgramExe}"
Name: "{group}\{#MyAppSetupName} {cm:MyDocName}"; Filename: "{app}\{cm:MyDocFile}"
Name: "{group}\{cm:UninstallProgram,{#MyAppSetupName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppSetupName}"; Filename: "{app}\{#MyProgramExe}"; Tasks: desktopicon


[Run]
Filename: "{app}\{#MyProgramExe}"; Description: "{cm:LaunchProgram,{#MyAppSetupName}}"; Flags: nowait postinstall skipifsilent
