# Проверка macOS-плагина на Mac — 2026-11-11

Проверен `src/HomeAssistant.Agent.Plugin.MacOS` (`macos-plugin` 1.0.0) по
рабочей копии в живом агенте 1.0.1. Окружение: Mac на Apple silicon,
macOS 27.2, .NET 10.0.14, агент установлен в `/Applications`, служба
LaunchDaemon запущена, брокер MQTT 10.12.1.23. Состояния и команды проверялись
через `mosquitto_sub`/`mosquitto_pub` по топикам из discovery, системные
значения — через `osascript`, `system_profiler`, `ioreg`, `pbpaste` и
Accessibility API (выделение и шрифт в TextEdit).

## Итог

Весь нативный код работает с первого запуска: CoreAudio, CoreGraphics
(CGEventPost), Objective-C runtime (NSPasteboard, NSEvent с `NSPoint` и 11
аргументами), ApplicationServices (AX), login.framework. Падений процессов
не было. Найдена и исправлена одна ошибка (Audio Devices). Два вопроса
открыты: ложные начальные состояния при старте (хост) и устаревший путь к
настройке Accessibility в тексте ошибки.

## Что работает

| # | Пункт | Результат |
|---|---|---|
| 1 | Сборка | `dotnet build src/HomeAssistant.Agent.Plugin.MacOS -c Release`: 0 ошибок, 0 предупреждений (до и после правок). |
| 1 | Загрузка | Плагин загружен в оба процесса (`Plugin macos-plugin 1.0.0`), строки «skipped» для него нет. |
| 1 | Discovery | По умолчанию: 4 сущности в службе, 7 в приложении. Со всеми фичами и тестовым config — 29 в приложении. Хоткей `Cmd+Foo` с неизвестной клавишей в сущности не попал. |
| 1 | Выключение фич | `clipboard: false` → «Discovery: 26 published, 9 removed»; `session: false` → служба сама перечитала `Settings.json`, «3 published, 1 removed», зависших `ioreg` нет. Обратное включение возвращает сущности. |
| 2 | Mac Running / Agent Running | Оба `online`. `--shutdown` приложения → `navatusein-macbook/availability` = `offline`, запуск → `online`. |
| 3 | User Session | См. отдельный раздел ниже: ON/OFF отслеживается за ≤ 5 с. |
| 4 | Volume | Начальное значение совпадает с `osascript` (25, muted). Изменения в macOS (`set volume output volume 30`, unmute) доходят до MQTT за 1–2 с. Команды из MQTT: `15`, `0`, `37.6` (→ 38), mute `ON`/`OFF` меняют систему. Мусор (`abc`) игнорируется. Проверено на встроенных динамиках — другого выхода с регулятором нет. |
| 5 | Audio Devices | Текущие устройства публикуются верно (`Speakers` / `Mic`). Ошибка при переключении — исправлена, см. «Что исправлено». |
| 6 | Accessibility | Первое нажатие хоткея без разрешения: `InvalidOperationException: Home Assistant Agent may not control the computer…`, системный запрос показан. После выдачи разрешения нажатия проходят без перезапуска приложения. |
| 7 | Media | Play/Pause, Next, Previous работают (проверял пользователь на реальном воспроизведении). Приложение не падало, крэш-логов в `~/Library/Logs/DiagnosticReports` нет, ошибок в логе нет. |
| 8 | Hotkeys | В TextEdit: `Cmd+A` выделяет весь текст, `Cmd+B` делает его жирным (`Helvetica-Bold`), `Option+Left` — курсор на слово назад, `Shift+Left` — выделение одного символа. `Fn+E` открывает палитру эмодзи (флаг Fn доходит), `Ctrl+F2`, `Esc` отправляются без ошибок. |
| 9 | Clipboard: слоты | Save: `тест слот` из буфера → сенсор слота показывает текст. Paste в режимах `Clipboard (restore)` (вставлено, `pbpaste` вернул прежнее), `Clipboard` (вставлено, в буфере осталось вставленное), `Type` (набрано). Clear → `Empty`. |
| 9 | Clipboard: Type | Кириллица, эмодзи с модификатором тона (👍🏽), флаг (🇺🇦), перевод строки, строка длиннее 20 символов (разбиение на куски) — набраны без потерь. Буфер не тронут. |
| 9 | Clipboard Text (notify) | `Copy`: `pbpaste` = сообщение, типы `public.utf8-plain-text`, `org.nspasteboard.ConcealedType`, `org.nspasteboard.TransientType`. `Type`, `Clipboard`, `Clipboard (restore)` — как у слотов. |
| 10 | Launchers | `Calculator.app` без аргументов запускается. `TextEdit.app` с аргументами — `open -a … --args` передаёт их. Скрипт не-`.app` (`chmod +x`) с `one "two three" four` получил ровно 3 аргумента: `one`, `two three`, `four`. Папка открывается в Finder. |
| 11 | Lock | `SACLockScreenImmediate` блокирует экран сразу; Session ушёл в OFF через 1 с. |
| 12 | Power | Кнопки Shutdown/Restart есть в discovery (служба), не нажимались. |
| 13 | Стабильность | Служба за 1 час: RSS 82 → 76 МБ, `Poll failed` — 0, зависших `ioreg` и зомби нет. Приложение — см. замеры ниже. В логах за всё время только две ожидаемые ошибки (отказ Accessibility до разрешения и тест несуществующего устройства). |

### User Session и `ioreg`

Вывод `ioreg -n Root -d1 -a`, `IOConsoleUsers` (запись каждые 2 с во время
ручной блокировки `Ctrl+Cmd+Q`):

```
разблокирован: kCGSSessionOnConsoleKey = true, kCGSessionLoginDoneKey = true
заблокирован:  + CGSSessionScreenIsLocked = true, CGSSessionScreenLockedTime = …
```

Ключи совпадают с теми, на которые рассчитан `Session/SessionMonitor.cs`
(`kCGSSessionLoginDoneKey` с `SS` в этой версии нет — сработал вариант без
`SS`). Сенсор: 18:32:48 OFF → 18:33:03 ON, совпадает с блокировкой.

Служба стартует только после ввода пароля на экране входа — это не ошибка:
включён FileVault, и до разблокировки диска система (и LaunchDaemon'ы) не
загружается. Значит, «экран входа до логина» на этом Mac сенсор не увидит.

## Что исправлено

### Audio Devices: переключение на устройство, которое не может быть устройством по умолчанию

**Файлы:** `Audio/CoreAudio.cs`, `Audio/AudioDevices.cs`,
`Features/AudioDevicesFeature.cs`.

**Что было не так.** В списке устройств (инструмент для выпадающего списка
и поиск при переключении) учитывалось только наличие потоков. Виртуальное
«Microsoft Teams Audio» имеет потоки ввода и вывода, но
`kAudioDevicePropertyDeviceCanBeDefaultDevice` (`dflt`) = 0. Для него
`AudioObjectSetPropertyData(kAudioHardwarePropertyDefaultOutputDevice)`
возвращает 0 (успех), но устройство по умолчанию не меняется. Плагин при этом
публиковал в select выбранное значение — Home Assistant показывал «Microsoft
Teams Audio», а звук шёл в динамики. Результат `TrySetDefault` вообще
игнорировался, так что любая неудача выглядела как успех.

Попутно: у select был `DefaultState = первое устройство из списка`, и при
каждом старте сначала публиковалось оно, а через мгновение — реальное.

**Что сделано.**
- `CoreAudio.CanBeDefault(device, scope)` — чтение `dflt` в нужной области.
- `AudioDevices` берёт только устройства с потоками **и** `CanBeDefault` —
  так же, как их показывает «Звук» в настройках macOS; это и выпадающий список,
  и поиск при переключении.
- `TrySetDefault` → `SetDefault`: бросает исключение, если устройство не
  найдено или после записи устройство по умолчанию не то. Обработчик select
  его не глотает: хост пишет ошибку в лог и не публикует новое состояние, так
  что в Home Assistant остаётся реальное устройство.
- У select убран `DefaultState`: `OnEnableAsync` и так сразу публикует
  текущее устройство (`DefaultState` у `SelectEntity` необязателен).

**Как проверено.** Через ctypes: `dflt` = 0 у Teams Audio, установка
«успешна», `dOut` не меняется. После правки и переустановки: команда
`Microsoft Teams Audio` → в логе
`InvalidOperationException: Audio device 'Microsoft Teams Audio' is not connected or cannot be the default.`,
state в MQTT остался `Speakers`, системное устройство — MacBook Pro Speakers.
При старте публикуется только реальное устройство, без промежуточного.
Сборка: 0 ошибок, 0 предупреждений.

**Не проверено.** Успешное переключение между двумя реальными устройствами:
на этом Mac одно выходное и одно входное устройство (наушников/внешнего
устройства не было). Выпадающий список в UI после правки не смотрел —
должен остаться без «Microsoft Teams Audio». Тесты
(`tests/HomeAssistant.Agent.Plugins.Tests`) на Mac не собираются — прогнать
на Windows.

## Что открыто

### 1. Ложные начальные состояния при старте (хост, не плагин)

При каждом запуске приложения или перечитывании настроек службой сначала
публикуется `DefaultState` сущности, и только потом реальное значение:

```
17:38:14 macos/volume/level/state 0      ← DefaultState
17:38:14 macos/volume/mute/state  OFF    ← DefaultState
17:38:15 macos/volume/level/state 25
17:38:15 macos/volume/mute/state  ON
17:38:15 macos/session/state      OFF    ← DefaultState (служба)
17:38:15 macos/session/state      ON
```

Для Session это ложное «экран заблокирован» — автоматизации в Home Assistant
на него сработают. Причина в `HomeAssistant.Connector`: `PublishInitialStateAsync`
у `BinarySensorEntity`, `NumberEntity`, `SwitchEntity`, `SensorEntity`
публикует `DefaultState` до `OnEnableAsync` фичи. Windows-плагин устроен так же
(`windows_session`), значит это общее поведение. Нужна правка в агенте —
например, необязательный `DefaultState` (как у `SelectEntity`) или флаг «фича
сама опубликует начальное состояние». Не правил — решение за тобой.

### 2. Путь к Accessibility в тексте ошибки

`Native/Accessibility.cs` пишет «System Settings → Privacy & Security →
Accessibility». В macOS 27.2 ссылка
`x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility`
открывает окно «Device Control and Data Access», раздела «Privacy & Security»
пользователь не нашёл. Точный новый путь не уточнён — после уточнения
поправить текст (или сделать его нейтральным: «…allow it under Accessibility in
System Settings»).

### 3. Непонятное OFF → ON у Session в 18:31:33–18:31:38

Одно короткое (5 с) OFF без нажатия Lock — пользователь в это время, возможно,
что-то делал с экраном; повторно не воспроизвелось. Если повторится —
смотреть `ioreg` в этот момент.

### 4. Мелочи

- `AppleFnUsageType = 1` (Fn переключает раскладку) не мешает `Fn+E`.
- Набранный текст `line2` после перевода строки стал `Line2` — это
  автоматическая заглавная буква macOS (`NSAutomaticCapitalizationEnabled = 1`),
  не плагин; в режимах `Clipboard` текст вставляется как есть.
- Кнопку обзора `.app` в настройках (фильтр `*.app`) не проверял.
- Служба работает на dll до правки: правка её фич не касается, но для
  чистоты — `sudo launchctl kickstart -k system/com.navatusein.homeassistant-agent.service`.

## После проверки

- `Settings.json` возвращён из резервной копии, сделанной до проверки.
- dll плагина удалена из `Plugins` (будет поставлена из репозитория). Служба
  держит плагин в памяти до перезапуска:
  `sudo launchctl kickstart -k system/com.navatusein.homeassistant-agent.service`.
- Права `Plugins` на время проверки открыты на запись (`777`) — вернуть:
  `sudo chmod 755 "/Library/Application Support/HomeAssistantAgent/Plugins"`.
