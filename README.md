<div align="center">

<img src="assets/Pconnect.png" width="110" alt="Pconnect logo"/>

# Pconnect

**Универсальный помощник VPN для Windows**

Один клик — VPN подключён, а мешающие программы (DPI-обходы) отключены.

[![Version](https://img.shields.io/badge/version-2.0.1-8B5CF6)](#)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](#)
[![Windows](https://img.shields.io/badge/platform-Windows-0078D6)](#)
[![License](https://img.shields.io/badge/license-MIT-22C55E)](#)
[![Downloads](https://img.shields.io/github/downloads/Ilidan999/Pconnect/total?color=6C5CE7)](https://github.com/Ilidan999/Pconnect/releases)

</div>

---

## ✨ Возможности

- ✅ Подключение / отключение VPN по команде или ссылке (`happ://connect`, CLI и т.д.)
- ✅ Авто-отключение мешающих DPI-программ (zapret, GoodbyeDPI и др.) при подключении — и возврат при отключении
- ✅ Маршрутизация всего трафика через туннель (`IgnoreDefaultRoutes`)
- ✅ Показ внешнего IP и страны с флагом (офлайн-флаги, автоподбор)
- ✅ Защита от обрыва (kill switch) с окном «Вы отключили VPN?»
- ✅ Перезапуск браузеров после подключения + сброс DNS-кэша
- ✅ Автоподбор пути к VPN-клиенту и имени адаптера
- ✅ Тёмный дизайн в стиле Windows 11, звуки, анимации
- ✅ Трей, горячая клавиша `Ctrl+Shift+V`, профили, экспорт/импорт настроек
- ✅ Интерфейс на русском и английском
- ✅ Автообновление через GitHub Releases

## 📥 Скачать

Последняя версия — в разделе [Releases](https://github.com/Ilidan999/Pconnect/releases):

| Файл | Назначение |
|------|-----------|
| `Pconnect-Setup-*.exe` | Установщик (рекомендуется) |
| `Pconnect.exe` | Портативная версия (без установки) |

> Требуются права администратора (запрос UAC) — программа управляет службами и сетью.

```

## 🚀 Быстрый старт

1. Скачайте установщик из [Releases](https://github.com/Ilidan999/Pconnect/releases) или соберите из исходников.
2. Запустите `Pconnect.exe` (потребуется подтвердить UAC).
3. В «Настройках» укажите профиль VPN (или он подхватится автоматически по `happ://`).

---

## ⚙️ Настройка — config.json

Файл с профилями VPN. Поля:

| Поле | Описание |
|------|----------|
| `name` | Название профиля. |
| `connect` | Команда или ссылка подключения. Если есть `://` — запускается как ссылка (например `happ://connect`), иначе — через `cmd`. |
| `disconnect` | Команда или ссылка отключения. |
| `adapter` | Имя туннельного адаптера (для статуса). |
| `clientPath` | Путь к приложению VPN (кнопка «Открыть VPN»). |
| `fixRouting` | `true` — включать маршрут через туннель (`IgnoreDefaultRoutes`). Обычно `true` для Happ. |
| `interferingServices` | Службы, которые нужно остановить/отключить при подключении и вернуть при отключении. |
| `interferingProcesses` | Процессы, которые нужно завершить при подключении. |
| `interferingKeywords` | Доп. ключевые слова для автопоиска «мешающих» служб/процессов. |

> **Автоопределение.** Даже если `interferingServices`/`interferingProcesses` пусты, программа сама найдёт и отключит мешающие программы по встроенным ключевым словам (`zapret`, `winws`, `goodbyedpi`, `spoofdpi`, `byedpi`, `nfqws`, `tpws`, `dpi_bypass` и др.). При выключении VPN службы возвращаются.

> **Автообновление.** Поле `"updateRepo": "владелец/репозиторий"` — программа проверяет новые версии на GitHub Releases и уведомляет.

### Пример: Happ

```json
{
  "defaultProfile": "Happ",
  "updateRepo": "Ilidan999/Pconnect",
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

> Имя адаптера можно узнать, подключив VPN: `Get-NetAdapter | Format-Table Name, InterfaceDescription`.

---

## 🔧 Сборка

```powershell
dotnet publish src/Pconnect.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist
```

Получится `dist/Pconnect.exe` (self-contained — .NET уже внутри).

Установщик собирается из `installer/Pconnect.iss` (Inno Setup).

### Автосборка на GitHub Actions

`.github/workflows/build.yml` автоматически собирает exe + установщик и прикрепляет их к релизу при создании тега `v*`.

### Подпись кода

Для подписи нужен сертификат (Code Signing) — после подписи исчезнет предупреждение SmartScreen «Windows защитил ваш компьютер».

---

## 📄 Лицензия

[MIT](LICENSE). Звуки генерируются программой, иконка и логотип — собственные.

## ⚠️ Дисклеймер

Обход блокировок и DPI-обход могут нарушать условия сервисов или местное законодательство. Используйте только там, где это разрешено, на свой риск. Программа предоставляется «как есть».
