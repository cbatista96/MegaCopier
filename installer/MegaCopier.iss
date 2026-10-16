; Instalador de MegaCopier (Inno Setup 6). Lo compila build.ps1:  ISCC /DAppVersion=1.0.0 installer\MegaCopier.iss
;
; - Instalación por usuario en %LOCALAPPDATA%\Programs\MegaCopier: no pide permisos de administrador.
; - La app ya lleva su propio runtime (.NET nativo + Windows App SDK): no hay que instalar nada más.
; - Las integraciones (menú del Explorador, inicio con Windows) las registra la propia app con --install
;   y las deshace con --uninstall, así el instalador y la app nunca discrepan.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{8B7E2C1D-4F6A-4E9B-9C3D-2A1F5E7B8C90}
AppName=MegaCopier
AppVersion={#AppVersion}
AppVerName=MegaCopier {#AppVersion}
AppPublisher=MegaCopier
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\MegaCopier
DefaultGroupName=MegaCopier
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
OutputDir=..\artifacts\installer
OutputBaseFilename=MegaCopier-Setup-{#AppVersion}
SetupIconFile=..\src\MegaCopier.App\Assets\MegaCopier.ico
UninstallDisplayIcon={app}\MegaCopier.exe
UninstallDisplayName=MegaCopier
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
WizardStyle=modern
CloseApplications=no
ShowLanguageDialog=auto

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
es.ShellMenu=Añadir «Pegar con MegaCopier» y «Copiar / Mover aquí» al Explorador
en.ShellMenu=Add "Paste with MegaCopier" and "Copy / Move here" to File Explorer
es.Startup=Iniciar con Windows (recomendado: las copias empiezan al instante)
en.Startup=Start with Windows (recommended: transfers start instantly)
es.Integration=Integración:
en.Integration=Integration:
es.DeleteData=¿Borrar también tus ajustes, temas, historial y copias pendientes de MegaCopier?%n%nSi vas a reinstalarlo, elige «No» para conservarlos.
en.DeleteData=Also delete your MegaCopier settings, themes, history and pending transfers?%n%nIf you plan to reinstall it, choose "No" to keep them.

[Tasks]
Name: "shellmenu"; Description: "{cm:ShellMenu}"; GroupDescription: "{cm:Integration}"
Name: "startup"; Description: "{cm:Startup}"; GroupDescription: "{cm:Integration}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; Extensiones de versiones anteriores (las que el Explorador tenga cargadas se quedan hasta la próxima vez).
Type: files; Name: "{app}\MegaCopierShell-*.dll"
Type: files; Name: "{app}\MegaCopierShell*.dll.old-*"

[Files]
Source: "..\artifacts\app\*"; DestDir: "{app}"; Excludes: "*.pdb,MegaCopierShell*.dll,MegaCopierShell*.dll.old-*"; Flags: ignoreversion recursesubdirs createallsubdirs
; La DLL lleva la versión en el nombre; si ya existe (reinstalar la misma versión) y está cargada, se conserva.
Source: "..\artifacts\app\MegaCopierShell-{#AppVersion}.dll"; DestDir: "{app}"; Flags: ignoreversion onlyifdoesntexist
Source: "..\artifacts\cli\mcopy.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\MegaCopier"; Filename: "{app}\MegaCopier.exe"
Name: "{autodesktop}\MegaCopier"; Filename: "{app}\MegaCopier.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\MegaCopier.exe"; Parameters: "--install{code:InstallArgs}"; Flags: runhidden waituntilterminated
Filename: "{app}\MegaCopier.exe"; Description: "{cm:LaunchProgram,MegaCopier}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\MegaCopier.exe"; Parameters: "--uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "MegaCopierUninstall"

[UninstallDelete]
Type: files; Name: "{app}\MegaCopierShell*.dll.old-*"

[Code]
function InstallArgs(Param: String): String;
begin
  Result := '';
  if WizardIsTaskSelected('shellmenu') then
    Result := Result + ' --shellmenu';
  if WizardIsTaskSelected('startup') then
    Result := Result + ' --startup';
end;

// Al actualizar: cerrar la versión que está en la bandeja (sus copias quedan reanudables).
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  Existing: String;
begin
  Result := '';
  Existing := ExpandConstant('{app}\MegaCopier.exe');
  if FileExists(Existing) then
    Exec(Existing, '--exit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

// Al desinstalar: los datos del usuario (%LOCALAPPDATA%\MegaCopier) se conservan salvo que pida borrarlos.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
    if MsgBox(CustomMessage('DeleteData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{localappdata}\MegaCopier'), True, True, True);
end;
