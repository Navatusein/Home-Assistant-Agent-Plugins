# Задача: проверить macOS-плагин на Mac

Ты работаешь на Mac пользователя (Apple silicon). Нужно проверить новый плагин
`src/HomeAssistant.Agent.Plugin.MacOS` (код `macos-plugin`) в живом агенте,
исправить найденные ошибки в плагине и записать отчёт. Плагин написан на
Windows и ни разу не запускался: весь нативный код (P/Invoke в CoreAudio,
CoreGraphics, Objective-C runtime, ApplicationServices) проверяется впервые.

Отвечай пользователю по-русски. **Ничего не коммить и не пушь** — коммиты делает
пользователь. Отчёт — в `docs/MACOS-PLUGIN-CHECK.md` этого репозитория (по образцу
`docs/MACOS-CHECK.md` репозитория агента: что работает, что исправлено, что
открыто).

## Контекст

- Репозитории лежат рядом: `Home-Assistant-Agent` (агент) и
  `Home-Assistant-Agent-Plugins` (этот, плагины). Плагин собирается против
  исходников агента из соседней папки.
- Плагин повторяет Windows-плагин (`src/HomeAssistant.Agent.Plugin.Windows`) —
  сверяйся с ним, если непонятно, как должно себя вести.
- Сборка помечена `[assembly: SupportedOSPlatform("macos")]`, TFM `net10.0`.
- У агента две части: приложение в строке меню (от пользователя) и служба
  LaunchDaemon `com.navatusein.homeassistant-agent.service` (от root). Фичи
  `power`, `computer-running`, `session` работают в **службе**, остальные — в
  **приложении**.
- Данные установленного агента: `/Library/Application Support/HomeAssistantAgent/`
  — `Settings.json`, `Logs/`, `Plugins/` (`root:wheel 755`, писать через `sudo`).

| Фича (код) | Хост | Сущности (uniqueId) | Как сделано |
|---|---|---|---|
| `power` | служба | `macos_power_shutdown`, `macos_power_restart` | `/sbin/shutdown -h/-r now` |
| `computer-running` | служба | `macos_computer_running` | зеркало доступности службы |
| `session` | служба | `macos_session` (lock: ON = разблокирован) | опрос `ioreg -n Root -d1 -a` раз в 5 с, `IOConsoleUsers` |
| `lock` | приложение | `macos_lock` | `SACLockScreenImmediate` (login.framework), запасной `pmset displaysleepnow` |
| `agent-running` | приложение | `macos_agent_running` | зеркало доступности приложения |
| `volume` | приложение | `macos_volume_level`, `macos_volume_mute` | CoreAudio, опрос раз в 1 с |
| `audio-devices` | приложение | `macos_audio_devices_output/input` | CoreAudio, опрос раз в 2 с |
| `media` | приложение | `macos_media_play_pause/next/previous` | NSEvent systemDefined → CGEventPost |
| `launchers` | приложение | `macos_launchers_app_*`, `macos_launchers_folder_*` | `open -a` / `open` |
| `hotkeys` | приложение | `macos_hotkeys_*` | CGEventPost |
| `clipboard` | приложение | слоты `macos_clipboard_slot_N*`, `macos_clipboard_mode`, `macos_clipboard_text(_mode)` | NSPasteboard + Cmd+V / набор текста |

## Правила безопасности

- **Не нажимай Shutdown и Restart.** Проверь только, что кнопки появились в
  discovery. Реальное выключение — только если пользователь сам попросит.
- **Lock** блокирует экран пользователя — сначала спроси.
- Кнопки хоткеев, вставки, набора текста и медиа-клавиш шлют нажатия в
  **приложение на переднем плане**. Если это терминал с тобой — текст попадёт к
  тебе в ввод. Перед каждым таким тестом выводи на передний план тестовое
  приложение (например, `open -a TextEdit` с новым документом, затем `sleep 2`)
  и только потом жми кнопку. Хоткеи бери безобидные.
- Разрешения macOS (Accessibility, Automation) выдаёт только пользователь —
  попроси его и подожди.

## 1. Сборка и установка плагина

```sh
cd Home-Assistant-Agent-Plugins
dotnet build src/HomeAssistant.Agent.Plugin.MacOS -c Release
```

Ожидание: 0 ошибок, 0 предупреждений. Собирай **только этот проект**: решение
целиком на Mac не соберётся (Windows-плагин и тесты под `net10.0-windows` —
это ожидаемо, не чини). После сборки dll копируется в
`../Home-Assistant-Agent/src/HomeAssistant.Agent/bin/Release/net10.0-windows…/Plugins`
— это путь для Windows, не обращай внимания.

Агент должен быть установлен в `/Applications` (из `.dmg` или
`build/Publish-MacOS.ps1` агента), с выполненным setup, настроенным MQTT и
установленной службой. Если это не так — спроси пользователя, не ставь сам.

```sh
P="/Library/Application Support/HomeAssistantAgent/Plugins"
sudo cp src/HomeAssistant.Agent.Plugin.MacOS/bin/Release/net10.0/HomeAssistant.Agent.Plugin.MacOS.dll "$P/"
sudo chown root:wheel "$P/HomeAssistant.Agent.Plugin.MacOS.dll"
sudo chmod 644 "$P/HomeAssistant.Agent.Plugin.MacOS.dll"
sudo launchctl kickstart -k system/com.navatusein.homeassistant-agent.service
"/Applications/Home Assistant Agent.app/Contents/MacOS/HomeAssistant.Agent" --restart &
```

Проверь логи в `Logs/` (приложения и службы): плагин загружен в оба процесса,
нет ошибок загрузки, нет строки «is for … — skipped».

Для подробностей поставь плагину уровень логов Debug: в `Settings.json`
`plugins.macos-plugin.logLevel = "Debug"` (ошибки опросов пишутся на Debug как
«Poll failed»).

## 2. Как смотреть и жать сущности

Нужен `mosquitto_sub`/`mosquitto_pub` (`brew install mosquitto`, если нет).
Адрес брокера и учётные данные — в `Settings.json` (раздел `general`).

- Discovery: `mosquitto_sub -h … -u … -P … -v -t 'homeassistant/#' | grep macos_`.
  Из конфигов discovery бери **точные** `state_topic`, `command_topic`,
  `availability` — не угадывай топики.
- Нажать кнопку: `mosquitto_pub … -t '<command_topic>' -m PRESS`.
- Держи фоновый `mosquitto_sub -v` по топикам плагина с записью в файл, чтобы
  видеть изменения состояний во время тестов.

Настройки плагина правятся в `Settings.json`, формат:
`plugins.macos-plugin = { enabled, logLevel, features: { "<код>": bool }, config: { … } }`.
Ключи `config`: `ClipboardSlots` (число), `ClipboardText` (bool),
`Applications` `[{Name, Path, Arguments}]`, `Folders` `[{Name, Path}]`,
`Hotkeys` `[{Name, Keys: "Cmd+Shift+…"}]`, `OutputDevices`/`InputDevices`
`[{DeviceName, FriendlyName}]`. После правки — `--restart` приложения. Можно и
через UI (Settings → карточка плагина) — тогда попроси пользователя.

## 3. Что проверить

Для каждого пункта: работает / не работает, что в логах, что исправил.

1. **Discovery.** Все сущности из таблицы появились; после выключения фичи в
   `features` её сущности исчезают.
2. **Mac Running / Agent Running.** ON при работе; `--shutdown` приложения →
   Agent Running OFF.
3. **User Session (служба).** Сейчас ON. Посмотри сам вывод
   `ioreg -n Root -d1 -a`: есть ли в `IOConsoleUsers` ключи
   `kCGSSessionOnConsoleKey`, `kCGSessionLoginDoneKey` (или с `SS`),
   `CGSSessionScreenIsLocked` — парсер (`Session/SessionMonitor.cs`) рассчитан на
   них. Попроси пользователя заблокировать экран на ~20 с (заранее запусти
   фоном `sleep 10; ioreg -n Root -d1 -a > /tmp/ioreg-locked.plist`) —
   сенсор должен уйти в OFF и вернуться в ON.
4. **Volume.** Начальное значение = `osascript -e 'output volume of (get volume settings)'`.
   Меняй громкость через `osascript -e 'set volume output volume 30'` и mute —
   состояние в MQTT догоняет за ~1 с. Команды из MQTT (число 0–100, `ON`/`OFF`)
   меняют системную громкость. Проверь на встроенных динамиках и, если есть, на
   наушниках/внешнем устройстве (у некоторых нет общего регулятора — тогда
   используются каналы 1/2).
5. **Audio Devices.** Имена устройств: сравни список из инструмента плагина
   (выпадающий список в UI) с `system_profiler SPAudioDataType`. Пропиши 2
   выхода и 1–2 входа в `config`, переключай из MQTT — меняется системное
   устройство по умолчанию; смена в строке меню отражается в select.
6. **Accessibility.** Первое нажатие кнопки, шлющей клавиши, должно показать
   системный запрос, а в логе — ошибку «may not control the computer». Попроси
   пользователя разрешить «Home Assistant Agent» в Privacy & Security →
   Accessibility и повтори.
7. **Media.** Запусти воспроизведение (Music или видео в браузере), жми
   Play/Pause, Next, Previous. Это самое рискованное место: `objc_msgSend` с
   `NSPoint` и 11 аргументами (`Keyboard/MediaKeys.cs`). При падении процесса —
   смотри крэш-лог в `~/Library/Logs/DiagnosticReports`.
8. **Hotkeys.** В TextEdit: хоткей `Cmd+A`, затем что-то видимое (например,
   `Cmd+Shift+T` или стрелки). Проверь модификаторы, `Fn`, F-клавиши. Хоткей с
   неизвестной клавишей не должен попадать в сущности.
9. **Clipboard.**
   - Слот: `echo -n 'тест слот' | pbcopy` → кнопка Save/Paste → сенсор слота
     показывает текст; в TextEdit кнопка снова → текст вставлен. Режим
     `Clipboard (restore)` — после вставки `pbpaste` возвращает прежнее
     содержимое; `Clipboard` — остаётся вставленное; `Type` — текст набран
     (проверь кириллицу, эмодзи, перевод строки).
   - Clear очищает слот.
   - Clipboard Text (notify): режим `Copy`, отправь сообщение в его command
     topic → `pbpaste` его показывает. Маркеры скрытия проверь так:
     `osascript -l JavaScript -e 'ObjC.import("AppKit"); ObjC.deepUnwrap($.NSPasteboard.generalPasteboard.types)'`
     — должны быть `org.nspasteboard.ConcealedType` и `TransientType`.
10. **Launchers.** Приложение `/System/Applications/Calculator.app` (без
    аргументов) и что-нибудь с аргументами; исполняемый файл не-`.app`
    (например, скрипт с `chmod +x`); папка — открывается в Finder. Попроси
    пользователя проверить выбор `.app` через кнопку обзора в настройках (фильтр
    `*.app`).
11. **Lock** — только с согласия пользователя: экран блокируется сразу.
12. **Power** — не нажимать (см. правила).
13. **Стабильность.** Агент проработал 10+ минут: нет роста ошибок в логах,
    `ps -o rss` приложения и службы не растёт заметно, нет зависших `ioreg`.
    Выключение/включение плагина и фич в UI не оставляет висящих опросов.

## 4. Исправления

Чини в плагине всё, что найдёшь, и пересобирай/переустанавливай по шагу 1.
Если нужна правка в агенте (Plugin.Base, хост) — не делай её молча: опиши в
отчёте и спроси пользователя. После правок прогони на Mac сборку плагина;
тесты (`tests/HomeAssistant.Agent.Plugins.Tests`) на Mac не собираются —
пользователь прогонит их на Windows.

В отчёте для каждой правки: файл, что было не так, как проверено.
