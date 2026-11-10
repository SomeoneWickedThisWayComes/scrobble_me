// HTTP-клієнт last.fm API: вхід (auth.getMobileSession), «зараз грає» і скробли.
// Створюється в App і у вікні налаштувань (для перевірки логіна).
// Документація API: https://www.last.fm/api
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ScrobbleMe.Core;

/// <summary>Скробл, готовий до відправки (і до збереження в черзі на диску).</summary>
public sealed record QueuedScrobble(string Artist, string Title, string Album, int? DurationSeconds, long Timestamp)
{
    public static QueuedScrobble From(TrackInfo t, DateTimeOffset startedAt) =>
        new(t.Artist, t.Title, t.Album,
            t.Duration is { } d && d > TimeSpan.Zero ? (int)d.TotalSeconds : null,
            startedAt.ToUnixTimeSeconds());
}

/// <summary>Помилка, яку повернув сам last.fm (код з https://www.last.fm/api/errorcodes).</summary>
public sealed class LastFmException : Exception
{
    public int Code { get; }
    public LastFmException(int code, string message) : base($"last.fm: {message} (код {code})") => Code = code;

    /// <summary>Сесія недійсна або відкликана — потрібно увійти знову.</summary>
    public bool IsAuthError => Code is 4 or 9 or 14;

    /// <summary>Тимчасова проблема на боці last.fm — варто повторити пізніше.</summary>
    public bool IsTemporary => Code is 8 or 11 or 16 or 29;
}

public sealed class LastFmClient : IDisposable
{
    // Тільки HTTPS: getMobileSession передає пароль у тілі запиту.
    private static readonly Uri Endpoint = new("https://ws.audioscrobbler.com/2.0/");

    /// <summary>last.fm приймає до 50 скроблів в одному запиті.</summary>
    public const int MaxBatch = 50;

    private readonly HttpClient _http;

    public string ApiKey { get; set; }
    public string ApiSecret { get; set; }
    public string? SessionKey { get; set; }

    public bool HasCredentials => ApiKey.Length > 0 && ApiSecret.Length > 0;
    public bool IsLoggedIn => HasCredentials && !string.IsNullOrEmpty(SessionKey);

    public LastFmClient(string apiKey, string apiSecret, string? sessionKey = null, HttpMessageHandler? handler = null)
    {
        ApiKey = apiKey;
        ApiSecret = apiSecret;
        SessionKey = sessionKey;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        // 20 с вистачає з запасом; довше чекати немає сенсу — скробл однаково лишиться в черзі.
        _http.Timeout = TimeSpan.FromSeconds(20);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("ScrobbleMe/0.1");
    }

    /// <summary>Вхід за логіном і паролем. Пароль ніде не зберігається — лише отриманий ключ сесії.</summary>
    public async Task<(string UserName, string SessionKey)> GetMobileSessionAsync(string username, string password, CancellationToken ct = default)
    {
        using var doc = await CallAsync("auth.getMobileSession", new()
        {
            ["username"] = username,
            ["password"] = password,
        }, needSession: false, ct);

        var session = doc.RootElement.GetProperty("session");
        return (session.GetProperty("name").GetString() ?? username,
                session.GetProperty("key").GetString() ?? throw new LastFmException(0, "порожній ключ сесії"));
    }

    public async Task UpdateNowPlayingAsync(TrackInfo track, CancellationToken ct = default)
    {
        var p = new Dictionary<string, string>
        {
            ["artist"] = track.Artist,
            ["track"] = track.Title,
        };
        if (track.Album.Length > 0) p["album"] = track.Album;
        if (track.Duration is { } d && d > TimeSpan.Zero)
            p["duration"] = ((int)d.TotalSeconds).ToString(CultureInfo.InvariantCulture);

        using var _ = await CallAsync("track.updateNowPlaying", p, needSession: true, ct);
    }

    public async Task ScrobbleAsync(IReadOnlyList<QueuedScrobble> batch, CancellationToken ct = default)
    {
        if (batch.Count == 0) return;
        if (batch.Count > MaxBatch) throw new ArgumentException($"Не більше {MaxBatch} скроблів за раз", nameof(batch));

        var p = new Dictionary<string, string>();
        for (var i = 0; i < batch.Count; i++)
        {
            var s = batch[i];
            p[$"artist[{i}]"] = s.Artist;
            p[$"track[{i}]"] = s.Title;
            p[$"timestamp[{i}]"] = s.Timestamp.ToString(CultureInfo.InvariantCulture);
            if (s.Album.Length > 0) p[$"album[{i}]"] = s.Album;
            if (s.DurationSeconds is { } d) p[$"duration[{i}]"] = d.ToString(CultureInfo.InvariantCulture);
        }

        using var _ = await CallAsync("track.scrobble", p, needSession: true, ct);
    }

    private async Task<JsonDocument> CallAsync(string method, Dictionary<string, string> p, bool needSession, CancellationToken ct)
    {
        if (!HasCredentials) throw new InvalidOperationException("Не задано API key / API secret last.fm");

        p["method"] = method;
        p["api_key"] = ApiKey;
        if (needSession)
            p["sk"] = string.IsNullOrEmpty(SessionKey)
                ? throw new InvalidOperationException("Не виконано вхід у last.fm")
                : SessionKey;
        p["api_sig"] = LastFmSignature.Sign(p, ApiSecret);
        p["format"] = "json";

        using var content = new FormUrlEncodedContent(p);
        using var resp = await _http.PostAsync(Endpoint, content, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            // Буває при збоях last.fm: віддає HTML-сторінку з помилкою.
            throw new HttpRequestException($"last.fm повернув не JSON (HTTP {(int)resp.StatusCode})");
        }

        if (doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("error", out var err))
        {
            var code = err.ValueKind == JsonValueKind.Number ? err.GetInt32() : 0;
            var message = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
            doc.Dispose();
            throw new LastFmException(code, message);
        }

        return doc;
    }

    public void Dispose() => _http.Dispose();
}
