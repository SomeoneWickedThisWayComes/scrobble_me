// Головний контролер застосунку: один екземпляр, трей, опитування Apple Music раз на секунду,
// передача подій у last.fm і черга скроблів. Тут з'єднуються всі частини:
//   AppleMusicWatcher → ScrobbleTracker → (LastFmClient + ScrobbleQueue) → MainWindow/TrayIcon.
// Архітектура описана в README.md і docs/decisions.md.
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ScrobbleMe.Core;
using ScrobbleMe.Services;
using ScrobbleMe.UI;
using Windows.Storage.Streams;

namespace ScrobbleMe;

public partial class App : System.Windows.Application
{
    // Імена об'єктів синхронізації в межах сесії користувача (Local\): другий запуск
    // не створює ще один значок у треї, а лише просить перший показати вікно.
    private const string MutexName = @"Local\ScrobbleMe.SingleInstance";
    private const string ShowEventName = @"Local\ScrobbleMe.Show";

    /// <summary>Раз на секунду — достатньо точно для правила «половина треку» і майже не навантажує систему.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    /// <summary>Пауза між спробами надіслати чергу після мережевої помилки.</summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private AppSettings _settings = null!;
    private LastFmClient _client = null!;
    private ScrobbleQueue _queue = null!;
    private readonly AppleMusicWatcher _watcher = new();
    private readonly ScrobbleTracker _tracker = new();
    private TrayIcon _tray = null!;
    private MainWindow _main = null!;
    private DispatcherTimer _timer = null!;

    private bool _polling;
    private bool _hintShown;
    private DateTimeOffset _nextFlush = DateTimeOffset.MinValue;
    private string? _lastError;
    private TrackInfo? _artworkFor;
    private bool _playerFound;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, MutexName, out var isFirst);
        if (!isFirst)
        {
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch (WaitHandleCannotBeOpenedException) { }
            Shutdown();
            return;
        }
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) => Dispatcher.BeginInvoke(ShowMain), null, Timeout.Infinite, false);

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("необроблений виняток", args.Exception);
            args.Handled = true; // застосунок у треї не повинен тихо зникати через одну помилку
        };

        Log.Write("Запуск");
        _settings = AppSettings.Load();
        _client = new LastFmClient(_settings.ApiKey, _settings.ApiSecret, _settings.SessionKey);
        _queue = new ScrobbleQueue(AppPaths.Queue);

        _tray = new TrayIcon();
        _tray.OpenRequested += ShowMain;
        _tray.SettingsRequested += ShowSettings;
        _tray.PauseToggled += TogglePause;
        _tray.ExitRequested += ExitApp;
        _tray.SetPaused(!_settings.ScrobblingEnabled);

        _main = new MainWindow
        {
            Icon = Imaging.CreateBitmapSourceFromHIcon(_tray.AppIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()),
        };
        _main.SettingsRequested += ShowSettings;
        _main.HiddenToTray += OnHiddenToTray;
        Render();

        var minimized = e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        if (!minimized) ShowMain();

        // Без ключів і входу скроблити нікуди — одразу ведемо в налаштування.
        if (!_client.IsLoggedIn && !minimized) ShowSettings();

        _timer = new DispatcherTimer { Interval = PollInterval };
        _timer.Tick += async (_, _) => await PollAsync();
        _timer.Start();
    }

    private async Task PollAsync()
    {
        // DispatcherTimer не чекає async-обробник: без прапорця опитування могли б накладатися.
        if (_polling) return;
        _polling = true;
        try
        {
            WatchResult result;
            try
            {
                result = await _watcher.PollAsync();
            }
            catch (Exception ex)
            {
                Log.Error("читання стану плеєра", ex);
                result = WatchResult.NoPlayer;
            }
            _playerFound = result.SourceApp is not null;

            var now = DateTimeOffset.UtcNow;
            foreach (var ev in _tracker.Update(result.Snapshot, now))
            {
                switch (ev)
                {
                    case NowPlayingEvent np:
                        Log.Write($"Грає: {np.Track}");
                        _ = SendNowPlayingAsync(np.Track);
                        break;
                    case ScrobbleEvent sc when _settings.ScrobblingEnabled:
                        Log.Write($"Скробл: {sc.Track}");
                        _queue.Enqueue(QueuedScrobble.From(sc.Track, sc.StartedAt));
                        _nextFlush = now; // надіслати одразу, не чекаючи паузи після попередньої помилки
                        break;
                }
            }

            await UpdateArtworkAsync(result);
            await FlushIfDueAsync(now);
            Render();
        }
        finally
        {
            _polling = false;
        }
    }

    private async Task SendNowPlayingAsync(TrackInfo track)
    {
        if (!_settings.ScrobblingEnabled || !_client.IsLoggedIn) return;
        try
        {
            await _client.UpdateNowPlayingAsync(track);
        }
        catch (Exception ex)
        {
            // «Зараз грає» — некритично: не повторюємо, просто записуємо в журнал.
            Log.Error("updateNowPlaying", ex);
        }
    }

    private async Task FlushIfDueAsync(DateTimeOffset now)
    {
        if (_queue.Count == 0 || !_client.IsLoggedIn || !_settings.ScrobblingEnabled || now < _nextFlush) return;
        try
        {
            var sent = await _queue.FlushAsync(_client, now, Log.Write);
            if (sent > 0) Log.Write($"Надіслано скроблів: {sent}");
            _lastError = null;
        }
        catch (LastFmException ex) when (ex.IsAuthError)
        {
            Log.Error("скробл", ex);
            _lastError = "last.fm відхилив сесію — увійдіть знову в налаштуваннях.";
            _client.SessionKey = null; // не смикаємо last.fm, доки користувач не увійде
        }
        catch (Exception ex)
        {
            Log.Error("скробл", ex);
            _lastError = ex is LastFmException ? ex.Message : "Немає зв'язку з last.fm — спробую ще раз за хвилину.";
            _nextFlush = now + RetryDelay;
        }
    }

    private async Task UpdateArtworkAsync(WatchResult result)
    {
        var track = result.Snapshot.Track;
        if (track is null) { _artworkFor = null; _main.SetArtwork(null); return; }
        if (track.SameSongAs(_artworkFor)) return;

        // Обкладинка часто з'являється на секунду-дві пізніше за назву треку,
        // тому трек позначаємо «готовим» лише коли картинку справді отримали.
        if (result.Thumbnail is null) { _main.SetArtwork(null); return; }
        _artworkFor = track;
        _main.SetArtwork(await LoadImageAsync(result.Thumbnail));
    }

    private static async Task<ImageSource?> LoadImageAsync(IRandomAccessStreamReference reference)
    {
        try
        {
            using var ras = await reference.OpenReadAsync();
            using var src = ras.AsStreamForRead();
            var ms = new MemoryStream();
            await src.CopyToAsync(ms);
            ms.Position = 0;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 192; // 96 px × 2 для екранів з масштабом 200%
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex)
        {
            Log.Error("обкладинка", ex);
            return null;
        }
    }

    private void Render()
    {
        var track = _tracker.Current;
        var threshold = _tracker.Threshold;
        var progress = _tracker.Scrobbled ? 1.0
            : threshold is { } t && t > TimeSpan.Zero ? _tracker.Played / t : 0;

        string status;
        if (!_playerFound) status = "Apple Music не запущено.";
        else if (track is null) status = "Apple Music нічого не відтворює.";
        else if (!_settings.ScrobblingEnabled) status = "Скроблінг призупинено.";
        else if (_tracker.Scrobbled) status = "✓ Заскроблено";
        else if (threshold is null) status = "Трек коротший за 30 с — last.fm такі не приймає.";
        else
        {
            var left = threshold.Value - _tracker.Played;
            status = $"Заскроблю через {(int)left.TotalMinutes}:{left.Seconds:00}" + (_tracker.IsPlaying ? "" : " (пауза)");
        }

        if (_queue.Count > 0) status += $"  ·  у черзі: {_queue.Count}";

        string account;
        if (!_client.HasCredentials) account = "last.fm: не налаштовано";
        else if (!_client.IsLoggedIn) account = "last.fm: вхід не виконано";
        else account = $"last.fm: {_settings.UserName}";

        var error = _lastError;
        if (error is null && !_client.IsLoggedIn && track is not null)
            error = "Скробли зберігаються в черзі, доки ви не увійдете в last.fm.";

        _main.Render(new NowPlayingView(track, _tracker.IsPlaying, progress, status, error, account));
        _tray.SetTooltip(track is null ? "ScrobbleMe" : $"{track.Artist} — {track.Title}");
    }

    private void ShowMain() => _main.ShowAndActivate();

    private void OnHiddenToTray()
    {
        // Підказка лише раз за запуск, щоб не дратувати.
        if (_hintShown) return;
        _hintShown = true;
        _tray.ShowBalloon("ScrobbleMe працює у треї", "Скроблінг триває. Щоб вийти — правий клік по значку → «Вийти».");
    }

    private void ShowSettings()
    {
        var existing = this.Windows.OfType<SettingsWindow>().FirstOrDefault();
        if (existing is not null) { existing.Activate(); return; }

        var w = new SettingsWindow(_settings) { Icon = _main.Icon };
        w.Applied += () =>
        {
            _client.ApiKey = _settings.ApiKey;
            _client.ApiSecret = _settings.ApiSecret;
            _client.SessionKey = _settings.SessionKey;
            _lastError = null;
            _nextFlush = DateTimeOffset.MinValue; // після входу одразу відправити накопичене
            Render();
        };
        if (_main.IsVisible) w.Owner = _main;
        w.Show();
        w.Activate();
    }

    private void TogglePause()
    {
        _settings.ScrobblingEnabled = !_settings.ScrobblingEnabled;
        _settings.Save();
        _tray.SetPaused(!_settings.ScrobblingEnabled);
        Log.Write(_settings.ScrobblingEnabled ? "Скроблінг відновлено" : "Скроблінг призупинено");
        Render();
    }

    private void ExitApp()
    {
        _timer.Stop();
        _main.AllowClose = true;
        _main.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _client?.Dispose();
        _mutex?.Dispose();
        _showEvent?.Dispose();
        Log.Write("Вихід");
        base.OnExit(e);
    }
}
