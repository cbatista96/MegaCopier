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
AppPublisherURL=https://github.com/cbatista96/MegaCopier
AppSupportURL=https://github.com/cbatista96/MegaCopier/issues
AppUpdatesURL=https://github.com/cbatista96/MegaCopier/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\MegaCopier
DefaultGroupName=MegaCopier
DisableProgramGroupPage=yes
UsePreviousTasks=no
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
WizardImageFile=Assets\Wizard.bmp
WizardSmallImageFile=Assets\WizardSmall.bmp
InfoBeforeFile=Assets\Privacy.en.rtf
DisableWelcomePage=no
CloseApplications=no
ShowLanguageDialog=yes

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"; InfoBeforeFile: "Assets\Privacy.es.rtf"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
#include "DiagnosticsMessages.iss"
es.ShellMenu=Añadir «Pegar con MegaCopier» y «Copiar / Mover aquí» al Explorador
en.ShellMenu=Add "Paste with MegaCopier" and "Copy / Move here" to File Explorer
es.Startup=Iniciar con Windows (recomendado: las copias empiezan al instante)
en.Startup=Start with Windows (recommended: transfers start instantly)
es.Integration=Integración:
en.Integration=Integration:
es.DeleteData=¿Borrar también tus ajustes, temas, historial y copias pendientes de MegaCopier?%n%nSi vas a reinstalarlo, elige «No» para conservarlos.
en.DeleteData=Also delete your MegaCopier settings, themes, history and pending transfers?%n%nIf you plan to reinstall it, choose "No" to keep them.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startmenuicon"; Description: "{cm:Installer_StartMenu}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "taskbaricon"; Description: "{cm:Installer_Taskbar}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "shellmenu"; Description: "{cm:ShellMenu}"; GroupDescription: "{cm:Integration}"
Name: "startup"; Description: "{cm:Startup}"; GroupDescription: "{cm:Integration}"
Name: "autoupdates"; Description: "{cm:Updates_AutoCheck}"; GroupDescription: "{cm:Integration}"
Name: "crashreports"; Description: "{cm:Crash_Installer}"; GroupDescription: "{cm:Integration}"

[InstallDelete]
; Extensiones de versiones anteriores (las que el Explorador tenga cargadas se quedan hasta la próxima vez).
Type: files; Name: "{app}\MegaCopierShell-*.dll"
Type: files; Name: "{app}\MegaCopierShell*.dll.old-*"

[Files]
; Primero para extraerlo sin descomprimir la app completa al abrir el asistente.
Source: "..\artifacts\cli\mcopy.exe"; DestName: "MegaCopier-SetupHelper.exe"; Flags: dontcopy
Source: "..\artifacts\app\*"; DestDir: "{app}"; Excludes: "*.pdb,MegaCopierShell*.dll,MegaCopierShell*.dll.old-*"; Flags: ignoreversion recursesubdirs createallsubdirs
; La DLL lleva la versión en el nombre; si ya existe (reinstalar la misma versión) y está cargada, se conserva.
Source: "..\artifacts\app\MegaCopierShell-{#AppVersion}.dll"; DestDir: "{app}"; Flags: ignoreversion onlyifdoesntexist
Source: "..\artifacts\cli\mcopy.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\MegaCopier"; Filename: "{app}\MegaCopier.exe"; Tasks: startmenuicon
Name: "{autodesktop}\MegaCopier"; Filename: "{app}\MegaCopier.exe"; Tasks: desktopicon
; Windows exige una acción del usuario para anclar. Preparar el acceso nunca fuerza un anclaje.
Name: "{app}\MegaCopier"; Filename: "{app}\MegaCopier.exe"; Tasks: taskbaricon

[Run]
Filename: "{app}\MegaCopier.exe"; Parameters: "--install{code:InstallArgs}"; Flags: runhidden waituntilterminated
Filename: "{app}\MegaCopier.exe"; Description: "{cm:LaunchProgram,MegaCopier}"; Flags: nowait postinstall skipifsilent; Check: not IsUpdate
Filename: "{app}\MegaCopier.exe"; Parameters: "--updates"; Flags: nowait; Check: UpdateForeground
Filename: "{app}\MegaCopier.exe"; Parameters: "--background"; Flags: nowait runhidden; Check: UpdateBackground
Filename: "{win}\explorer.exe"; Parameters: "/select,""{app}\MegaCopier.lnk"""; Flags: nowait skipifsilent; Tasks: taskbaricon; Check: not IsUpdate

[UninstallRun]
Filename: "{app}\MegaCopier.exe"; Parameters: "--uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "MegaCopierUninstall"

[UninstallDelete]
Type: files; Name: "{app}\MegaCopierShell*.dll.old-*"

[Code]
var
  ReportsInitialized: Boolean;
  ReportHelp: TNewStaticText;

function HasUpdateOption(Option: String): Boolean;
var I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), Option) = 0 then Result := True;
end;

function IsUpdate: Boolean;
begin
  Result := HasUpdateOption('/UPDATE');
end;

function UpdateForeground: Boolean;
begin
  Result := IsUpdate and HasUpdateOption('/UPDATEFOREGROUND');
end;

function UpdateBackground: Boolean;
begin
  Result := IsUpdate and HasUpdateOption('/UPDATEBACKGROUND');
end;

function InstallArgs(Param: String): String;
begin
  Result := '';
  if WizardIsTaskSelected('shellmenu') then
    Result := Result + ' --shellmenu';
  if WizardIsTaskSelected('startup') then
    Result := Result + ' --startup';
  if not IsUpdate then
  begin
    if WizardIsTaskSelected('autoupdates') then
      Result := Result + ' --autoupdates=on'
    else
      Result := Result + ' --autoupdates=off';
    if WizardIsTaskSelected('crashreports') then
      Result := Result + ' --crashreports=on'
    else
      Result := Result + ' --crashreports=off';
  end;
end;

type
  TTaskBounds = record
    Left, Top, Right, Bottom: Integer;
  end;

function ReadTaskBounds(Window: LongWord; Message: LongWord; Index: LongInt; var Bounds: TTaskBounds): LongInt;
  external 'SendMessageW@user32.dll stdcall';

procedure CurPageChanged(CurPageID: Integer);
var
  Bounds: TTaskBounds;
  ListHeight: Integer;
begin
  if CurPageID <> wpSelectTasks then Exit;
  if ReportHelp = nil then
  begin
    ReportHelp := TNewStaticText.Create(WizardForm);
    ReportHelp.Parent := WizardForm.TasksList.Parent;
    ReportHelp.AutoSize := False;
    ReportHelp.WordWrap := True;
    ReportHelp.Caption := CustomMessage('Crash_Help');
    ReportHelp.Left := WizardForm.TasksList.Left + ScaleX(20);
    ReportHelp.Width := WizardForm.TasksList.Width - ScaleX(20);
    // Medir la última fila real, también con otra fuente o escala de Windows.
    ListHeight := WizardForm.TasksList.MinItemHeight * WizardForm.TasksList.Items.Count + ScaleY(8);
    if ReadTaskBounds(WizardForm.TasksList.Handle, $0198, WizardForm.TasksList.Items.Count - 1, Bounds) <> -1 then
      ListHeight := Bounds.Bottom + ScaleY(2);
    WizardForm.TasksList.Height := ListHeight;
    ReportHelp.Height := ScaleY(32);
    ReportHelp.AdjustHeight;
    ReportHelp.Top := WizardForm.TasksList.Top + ListHeight + ScaleY(4);
    ReportHelp.Anchors := [akLeft, akTop, akRight];
  end;
  // El asistente interactivo la ofrece marcada; no vuelve a marcarla al pulsar Atrás.
  if not ReportsInitialized then
  begin
    ReportsInitialized := True;
    if not WizardSilent then WizardSelectTasks('autoupdates,crashreports');
  end;
end;

// Al actualizar: cerrar la versión que está en la bandeja (sus copias quedan reanudables).
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  Existing: String;
  Arguments: String;
begin
  Result := '';
  Existing := ExpandConstant('{app}\MegaCopier.exe');
  if FileExists(Existing) then
  begin
    Arguments := '--exit';
    if IsUpdate then Arguments := '--exit-for-update';
    if not Exec(Existing, Arguments, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      Result := FmtMessage(SetupMessage(msgErrorExecutingProgram), [Existing]);
    if (Result = '') and (ResultCode <> 0) then
      Result := FmtMessage(SetupMessage(msgErrorCloseApplications), []);
  end;
end;

// Al desinstalar: los datos del usuario (%LOCALAPPDATA%\MegaCopier) se conservan salvo que pida borrarlos.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
    if MsgBox(CustomMessage('DeleteData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{localappdata}\MegaCopier'), True, True, True);
end;
