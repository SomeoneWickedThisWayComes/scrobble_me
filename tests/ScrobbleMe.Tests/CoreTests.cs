// Тести платформонезалежної логіки: коли скроблити, розбір метаданих Apple Music, підпис last.fm, черга.
// Запуск: `dotnet test` з кореня репозиторію (так само робить CI).
using System.Net;
using System.Text;
using ScrobbleMe.Core;
using Xunit;

namespace ScrobbleMe.Tests;

public class ScrobbleTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private static TrackInfo Song(int seconds = 200, string title = "Song") =>
        new("Artist", title, "Album", TimeSpan.FromSeconds(seconds));

    /// <summary>Імітує N секунд відтворення з опитуванням щосекунди. Повертає всі події.</summary>
    private static List<TrackerEvent> Play(ScrobbleTracker t, TrackInfo track, ref DateTimeOffset now, int seconds, int startPos = 0, bool playing = true)
    {
        var events = new List<TrackerEvent>();
        for (var i = 0; i <= seconds; i++)
        {
            events.AddRange(t.Update(new MediaSnapshot(track, playing, TimeSpan.FromSeconds(startPos + i)), now));
            now += TimeSpan.FromSeconds(1);
        }
        return events;
    }

    [Theory]
    [InlineData(200, 100)]   // половина
    [InlineData(600, 240)]   // але не більше 4 хв
    [InlineData(30, 15)]     // рівно 30 с — ще приймається
    public void Threshold(int duration, int expected) =>
        Assert.Equal(TimeSpan.FromSeconds(expected), ScrobbleTracker.ComputeThreshold(TimeSpan.FromSeconds(duration)));

    [Fact]
    public void ShortTrackIsNeverScrobbled() => Assert.Null(ScrobbleTracker.ComputeThreshold(TimeSpan.FromSeconds(29)));

    [Fact]
    public void UnknownDurationWaitsFourMinutes() => Assert.Equal(TimeSpan.FromMinutes(4), ScrobbleTracker.ComputeThreshold(null));

    [Fact]
    public void NowPlayingThenScrobbleAtHalf()
    {
        var t = new ScrobbleTracker();
        var now = T0;
        var events = Play(t, Song(200), ref now, 99);
        Assert.Single(events.OfType<NowPlayingEvent>());
        Assert.Empty(events.OfType<ScrobbleEvent>());

        events = Play(t, Song(200), ref now, 2, startPos: 100);
        var sc = Assert.Single(events.OfType<ScrobbleEvent>());
        Assert.Equal(T0, sc.StartedAt);
        Assert.True(t.Scrobbled);

        // Дограли до кінця — повторного скроблу немає.
        Assert.Empty(Play(t, Song(200), ref now, 90, startPos: 102).OfType<ScrobbleEvent>());
    }

    [Fact]
    public void PauseDoesNotCount()
    {
        var t = new ScrobbleTracker();
        var now = T0;
        Play(t, Song(200), ref now, 50);
        // 10 хвилин на паузі
        Assert.Empty(Play(t, Song(200), ref now, 600, startPos: 50, playing: false));
        Assert.True(t.Played < TimeSpan.FromSeconds(52));
    }

    [Fact]
    public void SkippedTrackIsNotScrobbled()
    {
        var t = new ScrobbleTracker();
        var now = T0;
        var events = Play(t, Song(200, "A"), ref now, 20);
        events.AddRange(Play(t, Song(200, "B"), ref now, 20));
        Assert.Equal(2, events.OfType<NowPlayingEvent>().Count());
        Assert.Empty(events.OfType<ScrobbleEvent>());
    }

    [Fact]
    public void SleepGapIsNotCountedAsListening()
    {
        var t = new ScrobbleTracker();
        var now = T0;
        Play(t, Song(200), ref now, 10);
        now += TimeSpan.FromHours(1); // комп'ютер спав
        var events = t.Update(new MediaSnapshot(Song(200), true, TimeSpan.FromSeconds(12)), now);
        Assert.Empty(events.OfType<ScrobbleEvent>());
    }

    [Fact]
    public void RepeatOneScrobblesEachPlay()
    {
        var t = new ScrobbleTracker();
        var now = T0;
        var events = Play(t, Song(60), ref now, 60);
        events.AddRange(Play(t, Song(60), ref now, 60)); // позиція знову з 0
        Assert.Equal(2, events.OfType<ScrobbleEvent>().Count());
    }

    [Fact]
    public void TrackSelectedOnPauseStartsWhenPlayed()
    {
        var t = new ScrobbleTracker();
        var now = T0;
        Assert.Empty(Play(t, Song(200), ref now, 30, playing: false));
        var start = now;
        var events = Play(t, Song(200), ref now, 101);
        Assert.Equal(start, events.OfType<ScrobbleEvent>().Single().StartedAt);
    }

    [Fact]
    public void PlayerGoneResets()
    {
        var t = new ScrobbleTracker();
        var now = T0;
        Play(t, Song(200), ref now, 10);
        t.Update(MediaSnapshot.Empty, now);
        Assert.Null(t.Current);
    }
}

public class AppleMusicMetadataTests
{
    [Fact]
    public void SplitsArtistAndAlbum()
    {
        var t = AppleMusicMetadata.Normalize("Океан Ельзи — Земля", null, "", "Обійми", null)!;
        Assert.Equal("Океан Ельзи", t.Artist);
        Assert.Equal("Земля", t.Album);
        Assert.Equal("Обійми", t.Title);
    }

    [Fact]
    public void KeepsArtistWhenAlbumPresent()
    {
        var t = AppleMusicMetadata.Normalize("A — B", null, "Real Album", "T", null)!;
        Assert.Equal("A — B", t.Artist);
        Assert.Equal("Real Album", t.Album);
    }

    [Fact]
    public void FallsBackToAlbumArtist() =>
        Assert.Equal("AA", AppleMusicMetadata.Normalize("", "AA", "Alb", "T", null)!.Artist);

    [Fact]
    public void RejectsEmpty()
    {
        Assert.Null(AppleMusicMetadata.Normalize("A", null, "", "", null));
        Assert.Null(AppleMusicMetadata.Normalize("", null, "", "T", null));
    }
}

public class LastFmSignatureTests
{
    [Fact]
    public void MatchesSpec()
    {
        // md5("api_keyKEYmethodauth.getMobileSessionpasswordPWusernameUSERsecret")
        var p = new Dictionary<string, string>
        {
            ["username"] = "USER",
            ["password"] = "PW",
            ["method"] = "auth.getMobileSession",
            ["api_key"] = "KEY",
            ["format"] = "json",
        };
        var expected = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(
            Encoding.UTF8.GetBytes("api_keyKEYmethodauth.getMobileSessionpasswordPWusernameUSERsecret"))).ToLowerInvariant();
        Assert.Equal(expected, LastFmSignature.Sign(p, "secret"));
    }

    [Fact]
    public void SortsOrdinally()
    {
        // Квадратні дужки й цифри: artist[0] < artist[1] < track[0] у порядку байтів.
        var p = new Dictionary<string, string> { ["track[0]"] = "t", ["artist[1]"] = "b", ["artist[0]"] = "a" };
        var expected = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(
            Encoding.UTF8.GetBytes("artist[0]aartist[1]btrack[0]ts"))).ToLowerInvariant();
        Assert.Equal(expected, LastFmSignature.Sign(p, "s"));
    }
}

public class ScrobbleQueueTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "scrobbleme-tests-" + Guid.NewGuid());
    private string QueuePath => Path.Combine(_dir, "queue.json");
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeHandler(Func<string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            Bodies.Add(body);
            return respond(body);
        }
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static QueuedScrobble Item(int i, DateTimeOffset? at = null) =>
        new("Artist", $"Song {i}", "Album", 200, (at ?? Now).ToUnixTimeSeconds() - i);

    [Fact]
    public async Task SendsInBatchesOf50AndPersists()
    {
        var q = new ScrobbleQueue(QueuePath);
        for (var i = 0; i < 120; i++) q.Enqueue(Item(i));
        Assert.Equal(120, new ScrobbleQueue(QueuePath).Count); // пережило «перезапуск»

        var handler = new FakeHandler(_ => Json("{\"scrobbles\":{}}"));
        using var client = new LastFmClient("k", "s", "sk", handler);
        Assert.Equal(120, await q.FlushAsync(client, Now));
        Assert.Equal(3, handler.Bodies.Count);
        Assert.Equal(0, new ScrobbleQueue(QueuePath).Count);
    }

    [Fact]
    public async Task KeepsItemsOnTemporaryError()
    {
        var q = new ScrobbleQueue(QueuePath);
        q.Enqueue(Item(1));
        using var client = new LastFmClient("k", "s", "sk", new FakeHandler(_ => Json("{\"error\":16,\"message\":\"try later\"}")));
        var ex = await Assert.ThrowsAsync<LastFmException>(() => q.FlushAsync(client, Now));
        Assert.True(ex.IsTemporary);
        Assert.Equal(1, q.Count);
    }

    [Fact]
    public async Task DropsInvalidBatch()
    {
        var q = new ScrobbleQueue(QueuePath);
        q.Enqueue(Item(1));
        using var client = new LastFmClient("k", "s", "sk", new FakeHandler(_ => Json("{\"error\":6,\"message\":\"bad\"}")));
        await q.FlushAsync(client, Now);
        Assert.Equal(0, q.Count);
    }

    [Fact]
    public async Task PrunesOlderThan14Days()
    {
        var q = new ScrobbleQueue(QueuePath);
        q.Enqueue(Item(1, Now - TimeSpan.FromDays(15)));
        var handler = new FakeHandler(_ => Json("{}"));
        using var client = new LastFmClient("k", "s", "sk", handler);
        Assert.Equal(0, await q.FlushAsync(client, Now));
        Assert.Empty(handler.Bodies);
    }

    [Fact]
    public void IgnoresDuplicate()
    {
        var q = new ScrobbleQueue(QueuePath);
        q.Enqueue(Item(1));
        q.Enqueue(Item(1));
        Assert.Equal(1, q.Count);
    }

    [Fact]
    public void SurvivesCorruptFile()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(QueuePath, "{not json");
        Assert.Equal(0, new ScrobbleQueue(QueuePath).Count);
        Assert.True(File.Exists(QueuePath + ".broken"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (DirectoryNotFoundException) { }
    }
}
