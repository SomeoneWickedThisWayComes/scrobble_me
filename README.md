<div align="center">

# ♪ ScrobbleMe

**Скроблінг з Apple Music для Windows у [last.fm](https://www.last.fm).**
Тихо живе в треї й записує в профіль усе, що ви слухаєте.

[![build](https://github.com/SomeoneWickedThisWayComes/scrobble_me/actions/workflows/build.yml/badge.svg)](https://github.com/SomeoneWickedThisWayComes/scrobble_me/actions/workflows/build.yml)
![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows&logoColor=white)
![.NET 8](https://img.shields.io/badge/.NET-8-512BD4?logo=dotnet&logoColor=white)
![last.fm](https://img.shields.io/badge/last.fm-scrobbler-D51007?logo=lastdotfm&logoColor=white)

<img src="docs/images/now-playing.png" width="460" alt="Віконце «Зараз грає» над треєм">

</div>

> [!NOTE]
> Зображення в README — макети, відтворені за розміткою вікон. На справжній Windows шрифт і рамки
> будуть системні, а розташування й тексти такі самі.

---

## ✨ Що вміє

| | |
|---|---|
| 🎵 **Бачить, що грає** | Назва, виконавець, альбом і обкладинка з Apple Music, оновлення щосекунди. |
| 📡 **Скроблить чесно** | Як вимагає last.fm: трек довший за 30 с, прослухано половину або 4 хв. Пауза й перемотування не рахуються. Окремо надсилає «Scrobbling now». |
| 🔁 **Повтор однієї пісні** | Кожне повторне прослуховування — окремий скробл. |
| 📴 **Не губить скробли** | Без інтернету вони чекають у черзі на диску й відправляються, щойно з'явиться зв'язок. |
| 🖥️ **Живе в треї** | Хрестик ховає вікно, а не закриває програму. Є автозапуск разом з Windows. |
| ⏸️ **Пауза** | Пункт меню «Призупинити скроблінг», коли слухаєте щось не для профілю. |
| 🔒 **Безпечно** | Пароль не зберігається. Ключі шифруються засобами Windows (DPAPI) і на іншому ПК не розшифруються. |

## 🖼️ Як це виглядає

<table>
<tr>
<td width="50%" valign="top">

**Трей.** Хрестик ховає вікно сюди. Лівий клік по значку — відкрити віконце, правий — меню.

<img src="docs/images/tray.png" alt="Меню в треї">

</td>
<td width="50%" valign="top">

**Різні стани.** Заскроблено ✓, пауза, черга, немає зв'язку з last.fm.

<img src="docs/images/states.png" alt="Стани віконця">

</td>
</tr>
</table>

## 🚀 Встановлення

1. **Завантажте `ScrobbleMe.exe`**:
   - з [Releases](https://github.com/SomeoneWickedThisWayComes/scrobble_me/releases), коли там з'явиться реліз, або
   - з [Actions](https://github.com/SomeoneWickedThisWayComes/scrobble_me/actions/workflows/build.yml) → останній зелений запуск → артефакт **ScrobbleMe-win-x64** (zip, усередині exe).
2. **Покладіть exe куди зручно** (наприклад, `%USERPROFILE%\Apps\ScrobbleMe\`) і запустіть.
   .NET встановлювати не треба, усе вже всередині.

> [!TIP]
> Windows SmartScreen може попередити про «невідомого видавця», бо exe не підписаний.
> Натисніть **Докладніше → Однаково запустити**.

**Вимоги:** Windows 10 1809+ або Windows 11 і застосунок **Apple Music** з Microsoft Store.

## 🔑 Перший запуск: вхід у last.fm

<img src="docs/images/settings.png" width="420" align="right" alt="Вікно налаштувань">

last.fm вимагає, щоб у кожного застосунку був свій API-ключ. Його створюють один раз, це займає хвилини дві:

1. Відкрийте **[last.fm/api/account/create](https://www.last.fm/api/account/create)**.
2. **Application name** — будь-яка (наприклад, `ScrobbleMe`).
   **Callback URL** — можна лишити порожнім. Натисніть **Submit**.
3. Скопіюйте **API key** і **Shared secret** у налаштування ScrobbleMe.
4. Введіть логін і пароль last.fm → **Увійти**.

Пароль використовується лише один раз: програма обмінює його на ключ сесії й далі працює тільки з ключем.

**Перевірка:** увімкніть пісню в Apple Music. У профілі last.fm з'явиться *Scrobbling now*, а після половини треку у віконці буде **✓ Заскроблено**.

<br clear="right">

## 🩺 Якщо щось не так

<details>
<summary><b>«Apple Music не запущено», хоча музика грає</b></summary>

Підтримується лише **новий** застосунок Apple Music з Microsoft Store. Старий iTunes для Windows
не передає системі, що зараз грає.
</details>

<details>
<summary><b>Трек показується, але не скроблиться</b></summary>

Подивіться статус під смужкою прогресу. Треки, коротші за 30 с, last.fm не приймає.
Подробиці — у журналі `%APPDATA%\ScrobbleMe\log.txt`.
</details>

<details>
<summary><b>«last.fm відхилив сесію — увійдіть знову»</b></summary>

Налаштування → **Вийти з облікового запису** → увійдіть знову. Скробли з черги не загубляться.
</details>

<details>
<summary><b>Виконавець або альбом у last.fm записались криво</b></summary>

Apple Music передає їх одним рядком «Виконавець — Альбом», і програма розрізає його по тире
([чому так](docs/decisions.md#d-2-розрізати-виконавець--альбом)). Якщо трапився збій, створіть issue з назвою треку.
</details>

### Де лежать дані

Усе зберігається в `%APPDATA%\ScrobbleMe\`:

| Файл | Що в ньому |
|---|---|
| `settings.json` | Налаштування. API secret і ключ сесії зашифровані. |
| `queue.json` | Скробли, які ще чекають відправки. |
| `log.txt` | Журнал: що грало, що заскроблено, помилки. |

## 🛠️ Для розробників

```
src/ScrobbleMe/
├── Core/                     логіка без залежностей від Windows (тестується на будь-якій ОС)
│   ├── ScrobbleTracker.cs      коли скроблити: облік прослуханого часу
│   ├── AppleMusicMetadata.cs   розбір «Виконавець — Альбом»
│   ├── LastFmClient.cs         запити до last.fm API
│   ├── LastFmSignature.cs      підпис запитів (api_sig)
│   └── ScrobbleQueue.cs        черга скроблів на диску
├── Services/                 Windows: читання плеєра, налаштування, автозапуск, журнал
├── UI/                       вікна WPF і значок у треї
└── App.xaml.cs               з'єднує все разом і опитує плеєр раз на секунду
tests/ScrobbleMe.Tests/       тести Core
docs/decisions.md             журнал рішень: що обрали і чому
.github/workflows/build.yml   CI: тести + exe на кожен push, реліз на тег v*
```

Потрібен **.NET 8 SDK**.

```bash
dotnet test tests/ScrobbleMe.Tests
```

```bash
dotnet publish src/ScrobbleMe/ScrobbleMe.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

Збирати можна і на Linux/macOS (`EnableWindowsTargeting`), а запускати — лише на Windows.

**Випуск релізу:** поставте тег `v0.1.0` і запуште його. CI сам створить реліз з exe.

Чому зроблено саме так — у [журналі рішень](docs/decisions.md).
