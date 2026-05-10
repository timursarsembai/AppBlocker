; Основная конфигурация установщика
[Setup]
AppName=AppBlocker
AppVersion=1.1.0
AppPublisher=Ваша Компания
AppPublisherURL=https://yourwebsite.com
DefaultDirName={autopf}\AppBlocker
DefaultGroupName=AppBlocker
OutputDir=.\Output
OutputBaseFilename=AppBlockerSetup
Compression=lzma2/ultra64
SolidCompression=yes

; ВАЖНО: Запрашиваем права Администратора для установки службы и изменения Program Files
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

[Dirs]
; Создаем системную скрытую директорию для конфигурации (ProgramData\AppBlocker)
Name: "{commonappdata}\AppBlocker"; Permissions: authusers-modify

[Files]
; Копируем скомпилированные файлы пользовательского интерфейса (WPF)
Source: "src\AppBlocker.UI\bin\Release\net8.0-windows\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; Копируем скомпилированные файлы фоновой службы и Watchdog-а в отдельную подпапку Service
Source: "src\AppBlocker.Service\bin\Release\net8.0-windows\*"; DestDir: "{app}\Service"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "src\AppBlocker.Watchdog\bin\Release\net8.0\*"; DestDir: "{app}\Service"; Flags: ignoreversion recursesubdirs createallsubdirs

; Копируем упакованное Chrome-расширение (.crx) и updates.xml (создаются build_extension.ps1)
Source: "build\extension\appblocker.crx"; DestDir: "{app}\Extension"; Flags: ignoreversion
Source: "build\extension\updates.xml"; DestDir: "{app}\Extension"; Flags: ignoreversion
Source: "build\extension\extension_id.txt"; DestDir: "{app}\Extension"; Flags: ignoreversion

; Копируем Native Messaging Bridge
Source: "src\AppBlocker.Bridge\bin\Release\net8.0\*"; DestDir: "{app}\Bridge"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Ярлык в меню "Пуск"
Name: "{group}\AppBlocker"; Filename: "{app}\AppBlocker.UI.exe"
; Ярлык на рабочем столе
Name: "{autodesktop}\AppBlocker"; Filename: "{app}\AppBlocker.UI.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Registry]
; Добавляем WPF-приложение (UI) в автозагрузку Windows для ТЕКУЩЕГО пользователя (HKCU)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "AppBlocker"; ValueData: """{app}\AppBlocker.UI.exe"" -minimized"; Flags: uninsdeletevalue

; Регистрируем Native Messaging Host для Chrome
Root: HKLM; Subkey: "SOFTWARE\Google\Chrome\NativeMessagingHosts\com.appblocker.bridge"; ValueType: string; ValueData: "{app}\Bridge\com.appblocker.bridge.json"; Flags: uninsdeletekey

; Регистрируем Native Messaging Host для Edge (тоже Chromium)
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Edge\NativeMessagingHosts\com.appblocker.bridge"; ValueType: string; ValueData: "{app}\Bridge\com.appblocker.bridge.json"; Flags: uninsdeletekey

; Автоустановка расширения через Chrome Enterprise Policy
; Extension ID и update URL подставляются из [Code] секции (ssPostInstall)
; Расширение устанавливается автоматически при следующем запуске Chrome

[Run]
; 1. Регистрируем службу Windows
Filename: "{sys}\sc.exe"; Parameters: "create AppBlockerSvc binPath= ""{app}\Service\AppBlocker.Service.exe"" start= auto displayname= ""AppBlocker Background Service"""; Flags: runhidden

; 2. Запускаем службу
Filename: "{sys}\sc.exe"; Parameters: "start AppBlockerSvc"; Flags: runhidden

; 3. Защищаем службу от остановки (применяем наш SDDL из ServiceProtector)
Filename: "{sys}\sc.exe"; Parameters: "sdset AppBlockerSvc D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCLCSWLOCRRC;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)"; Flags: runhidden

; 4. Запускаем UI после завершения установки (shellexec нужен для UAC-манифеста)
Filename: "{app}\AppBlocker.UI.exe"; Description: "Запустить AppBlocker"; Flags: nowait postinstall skipifsilent shellexec

[UninstallRun]
; 1. При удалении убиваем Watchdog, иначе он будет мешать остановить службу
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM AppBlocker.Watchdog.exe"; Flags: runhidden skipifdoesntexist

; 2. Снимаем защиту со службы (возвращаем дефолтный SDDL), иначе sc stop выдаст "Отказано в доступе"
Filename: "{sys}\sc.exe"; Parameters: "sdset AppBlockerSvc D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)"; Flags: runhidden

; 3. Останавливаем службу
Filename: "{sys}\sc.exe"; Parameters: "stop AppBlockerSvc"; Flags: runhidden

; 4. Удаляем службу из реестра Windows
Filename: "{sys}\sc.exe"; Parameters: "delete AppBlockerSvc"; Flags: runhidden

[UninstallDelete]
; Очищаем кэш и конфиги при удалении программы
Type: filesandordirs; Name: "{commonappdata}\AppBlocker"
; Очищаем Chrome policy (ExtensionInstallForcelist) при удалении
Type: filesandordirs; Name: "{app}\Extension"

[Code]
// Читает Extension ID из файла, созданного build_extension.ps1
function GetExtensionId(): String;
var
  IdFile: String;
  ExtId: AnsiString;
begin
  IdFile := ExpandConstant('{app}\Extension\extension_id.txt');
  if FileExists(IdFile) then
  begin
    LoadStringFromFile(IdFile, ExtId);
    Result := Trim(String(ExtId));
  end
  else
    Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  JsonContent: String;
  ExtId: String;
  UpdatesXml: String;
  PolicyValue: String;
begin
  if CurStep = ssInstall then
  begin
    // Пытаемся убить Watchdog (чтобы он не перезапустил службу)
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM AppBlocker.Watchdog.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    
    // Снимаем SDDL защиту со службы
    Exec(ExpandConstant('{sys}\sc.exe'), 'sdset AppBlockerSvc D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    
    // Останавливаем службу
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop AppBlockerSvc', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    
    // Убиваем UI
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM AppBlocker.UI.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    
    // Даем Windows 2 секунды на освобождение файлов
    Sleep(2000);
  end;

  if CurStep = ssPostInstall then
  begin
    // 1. Генерируем JSON-манифест Native Messaging Host
    ExtId := GetExtensionId();
    
    if ExtId <> '' then
      JsonContent := '{' + #13#10
        + '  "name": "com.appblocker.bridge",' + #13#10
        + '  "description": "AppBlocker Native Messaging Bridge",' + #13#10
        + '  "path": "' + ExpandConstant('{app}') + '\Bridge\AppBlocker.Bridge.exe",' + #13#10
        + '  "type": "stdio",' + #13#10
        + '  "allowed_origins": ["chrome-extension://' + ExtId + '/"]' + #13#10
        + '}'
    else
      JsonContent := '{' + #13#10
        + '  "name": "com.appblocker.bridge",' + #13#10
        + '  "description": "AppBlocker Native Messaging Bridge",' + #13#10
        + '  "path": "' + ExpandConstant('{app}') + '\Bridge\AppBlocker.Bridge.exe",' + #13#10
        + '  "type": "stdio",' + #13#10
        + '  "allowed_origins": ["chrome-extension://*/"]' + #13#10
        + '}';
    SaveStringToFile(ExpandConstant('{app}\Bridge\com.appblocker.bridge.json'), JsonContent, False);

    // 2. Перезаписываем updates.xml с правильным путём к CRX
    if ExtId <> '' then
    begin
      UpdatesXml := '<?xml version=''1.0'' encoding=''UTF-8''?>' + #13#10
        + '<gupdate xmlns=''http://www.google.com/update2/response'' protocol=''2.0''>' + #13#10
        + '  <app appid=''' + ExtId + '''>' + #13#10
        + '    <updatecheck codebase=''file:///' + ExpandConstant('{app}') + '/Extension/appblocker.crx'' version=''1.0.0'' />' + #13#10
        + '  </app>' + #13#10
        + '</gupdate>';
      SaveStringToFile(ExpandConstant('{app}\Extension\updates.xml'), UpdatesXml, False);

      // 3. Прописываем Chrome Policy: ExtensionInstallForcelist
      //    Это заставляет Chrome автоматически установить расширение
      PolicyValue := ExtId + ';file:///' + ExpandConstant('{app}') + '/Extension/updates.xml';
      
      // Chrome
      RegWriteStringValue(HKEY_LOCAL_MACHINE, 'SOFTWARE\Policies\Google\Chrome\ExtensionInstallForcelist', '1', PolicyValue);
      
      // Edge
      RegWriteStringValue(HKEY_LOCAL_MACHINE, 'SOFTWARE\Policies\Microsoft\Edge\ExtensionInstallForcelist', '1', PolicyValue);
    end;
  end;
end;

// При удалении — очищаем Chrome Policy
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Удаляем Chrome policy
    RegDeleteValue(HKEY_LOCAL_MACHINE, 'SOFTWARE\Policies\Google\Chrome\ExtensionInstallForcelist', '1');
    RegDeleteValue(HKEY_LOCAL_MACHINE, 'SOFTWARE\Policies\Microsoft\Edge\ExtensionInstallForcelist', '1');
    
    // Удаляем пустые ключи если больше нет значений
    RegDeleteKeyIfEmpty(HKEY_LOCAL_MACHINE, 'SOFTWARE\Policies\Google\Chrome\ExtensionInstallForcelist');
    RegDeleteKeyIfEmpty(HKEY_LOCAL_MACHINE, 'SOFTWARE\Policies\Microsoft\Edge\ExtensionInstallForcelist');
  end;
end;
