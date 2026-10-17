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
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
Name: "it"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "uk"; MessagesFile: "compiler:Languages\Ukrainian.isl"
Name: "pl"; MessagesFile: "compiler:Languages\Polish.isl"
Name: "nl"; MessagesFile: "compiler:Languages\Dutch.isl"
Name: "tr"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "ja"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "ko"; MessagesFile: "compiler:Languages\Korean.isl"
; Inno Setup 6 no trae el chino simplificado: ese instalador sale en inglés (la app sí va en chino).

[CustomMessages]
es.ShellMenu=Añadir «Pegar con MegaCopier» y «Copiar / Mover aquí» al Explorador
en.ShellMenu=Add "Paste with MegaCopier" and "Copy / Move here" to File Explorer
fr.ShellMenu=Ajouter « Coller avec MegaCopier » et « Copier / Déplacer ici » à l'Explorateur
de.ShellMenu=„Mit MegaCopier einfügen“ und „Hierher kopieren / verschieben“ zum Explorer hinzufügen
it.ShellMenu=Aggiungi «Incolla con MegaCopier» e «Copia / Sposta qui» a Esplora file
ptbr.ShellMenu=Adicionar "Colar com o MegaCopier" e "Copiar / Mover aqui" ao Explorador de Arquivos
ru.ShellMenu=Добавить «Вставить через MegaCopier» и «Копировать / Переместить сюда» в Проводник
uk.ShellMenu=Додати «Вставити через MegaCopier» і «Копіювати / Перемістити сюди» до Провідника
pl.ShellMenu=Dodaj „Wklej przez MegaCopier” i „Kopiuj / Przenieś tutaj” do Eksploratora plików
nl.ShellMenu='Plakken met MegaCopier' en 'Hierheen kopiëren / verplaatsen' aan Verkenner toevoegen
tr.ShellMenu=Dosya Gezgini'ne "MegaCopier ile yapıştır" ve "Buraya kopyala / taşı" ekle
ja.ShellMenu=エクスプローラーに「MegaCopier で貼り付け」と「ここにコピー / 移動」を追加
ko.ShellMenu=파일 탐색기에 "MegaCopier로 붙여넣기"와 "여기에 복사 / 이동" 추가
es.Startup=Iniciar con Windows (recomendado: las copias empiezan al instante)
en.Startup=Start with Windows (recommended: transfers start instantly)
fr.Startup=Démarrer avec Windows (recommandé : les copies commencent instantanément)
de.Startup=Mit Windows starten (empfohlen: Kopien beginnen sofort)
it.Startup=Avvia con Windows (consigliato: le copie iniziano subito)
ptbr.Startup=Iniciar com o Windows (recomendado: as cópias começam na hora)
ru.Startup=Запускать вместе с Windows (рекомендуется: копирование начинается мгновенно)
uk.Startup=Запускати разом з Windows (рекомендовано: копіювання починається миттєво)
pl.Startup=Uruchamiaj z systemem Windows (zalecane: kopiowanie startuje natychmiast)
nl.Startup=Met Windows starten (aanbevolen: kopieën beginnen meteen)
tr.Startup=Windows ile başlat (önerilir: kopyalamalar anında başlar)
ja.Startup=Windows と同時に起動（推奨: コピーがすぐに始まります）
ko.Startup=Windows와 함께 시작(권장: 복사가 즉시 시작됨)
es.Integration=Integración:
en.Integration=Integration:
fr.Integration=Intégration :
de.Integration=Integration:
it.Integration=Integrazione:
ptbr.Integration=Integração:
ru.Integration=Интеграция:
uk.Integration=Інтеграція:
pl.Integration=Integracja:
nl.Integration=Integratie:
tr.Integration=Tümleştirme:
ja.Integration=統合:
ko.Integration=통합:
es.DeleteData=¿Borrar también tus ajustes, temas, historial y copias pendientes de MegaCopier?%n%nSi vas a reinstalarlo, elige «No» para conservarlos.
en.DeleteData=Also delete your MegaCopier settings, themes, history and pending transfers?%n%nIf you plan to reinstall it, choose "No" to keep them.
fr.DeleteData=Supprimer aussi vos paramètres, thèmes, historique et copies en attente de MegaCopier ?%n%nSi vous comptez le réinstaller, choisissez « Non » pour les conserver.
de.DeleteData=Auch Ihre MegaCopier-Einstellungen, Designs, den Verlauf und ausstehende Kopien löschen?%n%nWenn Sie es neu installieren möchten, wählen Sie „Nein“, um sie zu behalten.
it.DeleteData=Eliminare anche impostazioni, temi, cronologia e copie in sospeso di MegaCopier?%n%nSe intendi reinstallarlo, scegli «No» per conservarli.
ptbr.DeleteData=Apagar também suas configurações, temas, histórico e cópias pendentes do MegaCopier?%n%nSe for reinstalá-lo, escolha "Não" para mantê-los.
ru.DeleteData=Удалить также настройки, темы, историю и незавершённые копирования MegaCopier?%n%nЕсли собираетесь переустановить программу, выберите «Нет», чтобы их сохранить.
uk.DeleteData=Видалити також налаштування, теми, історію та незавершені копіювання MegaCopier?%n%nЯкщо збираєтеся перевстановити програму, виберіть «Ні», щоб їх зберегти.
pl.DeleteData=Usunąć też ustawienia, motywy, historię i oczekujące kopiowania MegaCopier?%n%nJeśli zamierzasz zainstalować go ponownie, wybierz „Nie”, aby je zachować.
nl.DeleteData=Ook je MegaCopier-instellingen, thema's, geschiedenis en openstaande kopieën verwijderen?%n%nAls je het opnieuw wilt installeren, kies dan 'Nee' om ze te bewaren.
tr.DeleteData=MegaCopier ayarlarınız, temalarınız, geçmişiniz ve bekleyen kopyalamalarınız da silinsin mi?%n%nYeniden kuracaksanız bunları korumak için "Hayır"ı seçin.
ja.DeleteData=MegaCopier の設定、テーマ、履歴、保留中のコピーも削除しますか？%n%n再インストールする予定なら「いいえ」を選ぶと残せます。
ko.DeleteData=MegaCopier 설정, 테마, 기록, 대기 중인 복사도 삭제할까요?%n%n다시 설치할 예정이면 "아니요"를 선택해 보관하세요.

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
