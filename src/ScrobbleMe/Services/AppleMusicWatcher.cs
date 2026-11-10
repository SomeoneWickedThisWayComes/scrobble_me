// Читає, що зараз грає в Apple Music, через системний API Windows «Керування мультимедіа»
// (Global System Media Transport Controls — той самий, що показує плитку плеєра біля гучності).
// Опитується з App раз на секунду. Чому саме так, а не через iTunes COM — docs/decisions.md, D-1.
using System;
using System.Threading.Tasks;
using ScrobbleMe.Core;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace ScrobbleMe.Services;

public sealed record WatchResult(MediaSnapshot Snapshot, string? SourceApp, IRandomAccessStreamReference? Thumbnail)
{
    public static readonly WatchResult NoPlayer = new(MediaSnapshot.Empty, null, null);
}

public sealed class AppleMusicWatcher
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;

    public async Task<WatchResult> PollAsync()
    {
        _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();

        var session = FindAppleMusic(_manager);
        if (session is null) return WatchResult.NoPlayer;

        var props = await session.TryGetMediaPropertiesAsync();
        if (props is null) return WatchResult.NoPlayer;

        var playback = session.GetPlaybackInfo();
        var timeline = session.GetTimelineProperties();
        var isPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

        TimeSpan? duration = null;
        TimeSpan? position = null;
        if (timeline is not null)
        {
            var d = timeline.EndTime - timeline.StartTime;
            if (d > TimeSpan.Zero) duration = d;

            // Плеєр оновлює позицію не щосекунди, а лише при зміні стану, тому
            // «доводимо» її самі: позиція + скільки пройшло з моменту оновлення.
            var pos = timeline.Position;
            if (isPlaying && timeline.LastUpdatedTime.Year > 2000)
            {
                var elapsed = DateTimeOffset.Now - timeline.LastUpdatedTime;
                if (elapsed > TimeSpan.Zero) pos += elapsed;
            }
            if (duration is { } dur && pos > dur) pos = dur;
            position = pos;
        }

        var track = AppleMusicMetadata.Normalize(props.Artist, props.AlbumArtist, props.AlbumTitle, props.Title, duration);
        return new WatchResult(new MediaSnapshot(track, isPlaying && track is not null, position),
                               session.SourceAppUserModelId, props.Thumbnail);
    }

    /// <summary>
    /// Шукаємо саме Apple Music (AppUserModelId виду «AppleInc.AppleMusicWin_…!App»),
    /// щоб не скроблити YouTube у браузері чи Spotify, які теж сюди пишуть.
    /// </summary>
    private static GlobalSystemMediaTransportControlsSession? FindAppleMusic(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        foreach (var s in manager.GetSessions())
        {
            var id = s.SourceAppUserModelId ?? "";
            if (id.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("AppleInc.", StringComparison.OrdinalIgnoreCase))
                return s;
        }
        return null;
    }
}
