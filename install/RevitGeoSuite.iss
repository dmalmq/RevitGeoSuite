; Kanizu Revit installer definition (Inno Setup 6)
;
; Build with:
;   ISCC.exe /DMyAppVersion=1.0.0.0 /DRevitYear=2024 /DDistDir="C:\path\to\install\dist" /DOutputDir="C:\path\to\install\output" install\RevitGeoSuite.iss

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0.0"
#endif

#ifndef DistDir
  #define DistDir "dist"
#endif
#ifndef OutputDir
  #define OutputDir "output"
#endif

#ifndef RevitYear
  #define RevitYear "2024"
#endif

#define AppName "Kanizu Revit"
#define AppPublisher "Kanizu"
; Keep the legacy AppId so upgrading from RevitGeoSuite replaces the Windows Installed Apps entry cleanly.
#define AppId "RevitGeoSuite.Revit" + RevitYear
#define AddinsRoot "{commonappdata}\Autodesk\Revit\Addins\" + RevitYear
#define InstallSubDir "KanizuRevit"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#MyAppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={#AddinsRoot}\{#InstallSubDir}
UsePreviousAppDir=no
DisableDirPage=yes
DisableProgramGroupPage=yes
UninstallDisplayName={#AppName} for Revit {#RevitYear}
UninstallDisplayIcon={app}\RevitGeoSuite.Shell.dll
OutputDir={#OutputDir}
OutputBaseFilename=KanizuRevit-Setup-{#RevitYear}-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[InstallDelete]
Type: filesandordirs; Name: "{app}"
; Cleanly remove any legacy RevitGeoSuite install so both add-ins can never load side-by-side.
Type: files; Name: "{#AddinsRoot}\RevitGeoSuite.addin"
Type: filesandordirs; Name: "{#AddinsRoot}\RevitGeoSuite"
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\{#RevitYear}\RevitGeoSuite.addin"

[Files]
Source: "{#DistDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "KanizuRevit.addin,RevitGeoSuite.addin"
Source: "{#DistDir}\KanizuRevit.addin"; DestDir: "{#AddinsRoot}"; Flags: ignoreversion

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
  if not FileExists(ExpandConstant('{pf}\Autodesk\Revit {#RevitYear}\Revit.exe')) then
  begin
    if MsgBox(
      'Autodesk Revit {#RevitYear} was not detected on this machine.'#13#10#13#10 +
      'Install anyway?',
      mbConfirmation,
      MB_YESNO) = IDNO then
    begin
      Result := False;
    end;
  end;
end;
