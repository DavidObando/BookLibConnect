#define MyAppSetupName 'Oahu'
#ifndef MyAppVersion
  #define MyAppVersion '1.0.0'
#endif
#define MyProgramExe = 'Oahu.exe'
#define MyCompany = 'DavidObando'
#define MyAppName = 'Oahu'
#ifndef MySourceDir
  #define MySourceDir '..\Oahu.App\bin\Release\net10.0\publish'
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
ChangesEnvironment=yes

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
Name: "addtopath"; Description: "Add Oahu install folder to PATH (so 'oahu-cli' works from any terminal)"; GroupDescription: "Command-line access:"

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


[Registry]
; Add the install dir to PATH so 'oahu-cli' is available from any terminal.
; Writes to HKLM when running as admin (system-wide), otherwise HKCU (per-user).
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"; \
    ValueType: expandsz; ValueName: "Path"; ValueData: "{olddata};{app}"; \
    Tasks: addtopath; Check: IsAdminInstallMode and NeedsAddPath(ExpandConstant('{app}'), True); \
    Flags: preservestringtype
Root: HKCU; Subkey: "Environment"; \
    ValueType: expandsz; ValueName: "Path"; ValueData: "{olddata};{app}"; \
    Tasks: addtopath; Check: (not IsAdminInstallMode) and NeedsAddPath(ExpandConstant('{app}'), False); \
    Flags: preservestringtype

[Run]
Filename: "{app}\{#MyProgramExe}"; Description: "{cm:LaunchProgram,{#MyAppSetupName}}"; Flags: nowait postinstall skipifsilent

[Code]
function NeedsAddPath(Param: string; SystemWide: Boolean): Boolean;
var
  OrigPath: string;
  RootKey: Integer;
  SubKey: string;
begin
  if SystemWide then
  begin
    RootKey := HKEY_LOCAL_MACHINE;
    SubKey := 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment';
  end
  else
  begin
    RootKey := HKEY_CURRENT_USER;
    SubKey := 'Environment';
  end;

  if not RegQueryStringValue(RootKey, SubKey, 'Path', OrigPath) then
  begin
    Result := True;
    exit;
  end;
  // Look for the path as a complete entry (between semicolons).
  Result := Pos(';' + Uppercase(Param) + ';', ';' + Uppercase(OrigPath) + ';') = 0;
end;

procedure RemoveFromPath(Param: string; SystemWide: Boolean);
var
  OrigPath: string;
  NewPath: string;
  RootKey: Integer;
  SubKey: string;
  P: Integer;
  Needle: string;
  Hay: string;
begin
  if SystemWide then
  begin
    RootKey := HKEY_LOCAL_MACHINE;
    SubKey := 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment';
  end
  else
  begin
    RootKey := HKEY_CURRENT_USER;
    SubKey := 'Environment';
  end;

  if not RegQueryStringValue(RootKey, SubKey, 'Path', OrigPath) then
    exit;

  Hay := ';' + Uppercase(OrigPath) + ';';
  Needle := ';' + Uppercase(Param) + ';';
  P := Pos(Needle, Hay);
  if P = 0 then
    exit;

  // Reconstruct the original-cased value minus the matched entry.
  NewPath := Copy(';' + OrigPath + ';', 1, P - 1) +
             Copy(';' + OrigPath + ';', P + Length(Needle), MaxInt);
  // Strip the leading and trailing sentinels we added.
  if (Length(NewPath) > 0) and (NewPath[1] = ';') then
    Delete(NewPath, 1, 1);
  if (Length(NewPath) > 0) and (NewPath[Length(NewPath)] = ';') then
    Delete(NewPath, Length(NewPath), 1);

  RegWriteExpandStringValue(RootKey, SubKey, 'Path', NewPath);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if IsAdminInstallMode() then
      RemoveFromPath(ExpandConstant('{app}'), True)
    else
      RemoveFromPath(ExpandConstant('{app}'), False);
  end;
end;
