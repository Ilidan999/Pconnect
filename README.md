# Pconnect

**Pconnect** — красивый универсальный помощник VPN для Windows. В один клик:

- отключает программы, которые мешают VPN (DPI-обходы вроде zapret / GoodbyeDPI);
- подключает VPN (по команде или ссылке);
- включает маршрутизацию всего трафика через туннель;
- показывает внешний IP и страну.

Написан на PowerShell + WPF, без сторонних зависимостей.

> ⚠️ Обход блокировок и DPI-обход могут нарушать условия сервисов или местное законодательство. Используйте только там, где это разрешено, на свой риск.

---

## Быстрый старт

1. Скачайте репозиторий (или готовый `Pconnect.exe` из раздела Releases).
2. Положите рядом с программой файл **`config.json`** (пример ниже).
3. Запустите `Pconnect.exe` (или `Pconnect.ps1`). Потребуется подтвердить запрос **UAC** — программе нужны права администратора для управления службами и сетью.

---

## Как работает

- **«Включить VPN»** — останавливает «мешающие» службы и процессы из профиля → выполняет команду подключения → ждёт появления туннельного адаптера → (при необходимости) включает маршрутизацию → проверяет IP.
- **«Выключить VPN»** — выполняет команду отключения → возвращает обратно отключённые службы.
- Окно можно перетаскивать за верхнюю часть; журнал растягивается за разделитель.

---

## config.json

Файл с профилями VPN. Поля:

| Поле | Описание |
|------|----------|
| `name` | Название профиля (показывается в выпадающем списке). |
| `connect` | Команда или ссылка подключения. Если есть `://` — запускается как ссылка (например `happ://connect`). Иначе — как команда через `cmd`. |
| `disconnect` | Команда или ссылка отключения. |
| `adapter` | Имя сетевого адаптера туннеля (для определения статуса). Можно узнать командой `Get-NetAdapter`. |
| `clientPath` | Путь к приложению VPN (для кнопки «Открыть VPN»). Необязательно. |
| `fixRouting` | `true`, если нужно включать маршрут через туннель (`IgnoreDefaultRoutes`). Обычно `false` для обычных VPN, `true` для Happ. |
| `interferingServices` | Службы, которые нужно остановить/отключить при подключении и вернуть при отключении. |
| `interferingProcesses` | Процессы, которые нужно завершить при подключении. |
| `interferingKeywords` | Ключевые слова для автоопределения «мешающих» служб/процессов. Необязательно — есть встроенный список. |

> **Автоопределение.** Даже если `interferingServices`/`interferingProcesses` пусты, программа сама сканирует службы и процессы по встроенным ключевым словам (`zapret`, `winws`, `goodbyedpi`, `spoofdpi`, `byedpi`, `nfqws`, `tpws`, `dpi_bypass` и др.) и отключает найденное. Поэтому название папки и точные имена роли не играют. При выключении VPN программа возвращает службам их исходный тип запуска и запускает их обратно. Список ключевых слов можно расширить полем `interferingKeywords`.

### Пример: Happ (РФ, с обходом)

```json
{
  "defaultProfile": "Happ",
  "profiles": [
    {
      "name": "Happ",
      "connect": "happ://connect",
      "disconnect": "happ://disconnect",
      "adapter": "happ-xray",
      "clientPath": "D:\\Happ\\Happ.exe",
      "fixRouting": true,
      "interferingServices": ["zapret"],
      "interferingProcesses": ["winws", "nfqws", "tpws"]
    }
  ]
}
```

### Пример: Windscribe (командная строка)

```json
{
  "defaultProfile": "Windscribe",
  "profiles": [
    {
      "name": "Windscribe",
      "connect": "windscribe connect",
      "disconnect": "windscribe disconnect",
      "adapter": "Windscribe VPN",
      "clientPath": "C:\\Program Files\\Windscribe\\Windscribe.exe",
      "fixRouting": false,
      "interferingServices": [],
      "interferingProcesses": []
    }
  ]
}
```

### Пример: Mullvad

```json
{
  "defaultProfile": "Mullvad",
  "profiles": [
    {
      "name": "Mullvad",
      "connect": "mullvad connect",
      "disconnect": "mullvad disconnect",
      "adapter": "Mullvad",
      "clientPath": "C:\\Program Files\\Mullvad VPN\\mullvad.exe",
      "fixRouting": false,
      "interferingServices": [],
      "interferingProcesses": []
    }
  ]
}
```

> Чтобы узнать имя адаптера, подключите VPN и выполните в PowerShell: `Get-NetAdapter | Format-Table Name, InterfaceDescription`.

---

## Сборка в .exe

```powershell
# однократно установить модуль
Install-Module -Name ps2exe -Scope CurrentUser -Force

# собрать exe (с иконкой и запросом прав администратора)
.\build.ps1
```

Получится `Pconnect.exe`. Его можно раздавать вместе с `config.json`.

---

## Структура репозитория

```
Pconnect/
├── Pconnect.ps1      # сама программа
├── config.json       # настройки профилей
├── build.ps1         # сборка в exe
├── assets/
│   ├── Pconnect.ico  # иконка
│   └── Pconnect.png  # логотип
├── LICENSE
└── README.md
```

---

## Лицензия

[MIT](LICENSE). Звуки генерируются самой программой, иконка и логотип — собственные. Вы можете свободно использовать и изменять проект.

---

## Дисклеймер

Программа предоставляется «как есть». Автор не несёт ответственности за нарушение условий обслуживания сторонних сервисов, местного законодательства или любые последствия использования.
