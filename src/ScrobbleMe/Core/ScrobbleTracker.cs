// Логіка «коли скроблити»: рахує реально прослуханий час і видає події NowPlaying / Scrobble.
// Викликається з App раз на секунду зі свіжим MediaSnapshot.
// Правила взяті з документації last.fm: https://www.last.fm/api/scrobbling#when-is-a-scrobble-a-scrobble
// (див. також docs/decisions.md, D-3).
using System;
using System.Collections.Generic;

namespace ScrobbleMe.Core;

public abstract record TrackerEvent;

/// <summary>Почалося відтворення треку — сказати last.fm «зараз грає».</summary>
public sealed record NowPlayingEvent(TrackInfo Track) : TrackerEvent;

/// <summary>Трек прослухано достатньо — записати скробл з часом початку відтворення.</summary>
public sealed record ScrobbleEvent(TrackInfo Track, DateTimeOffset StartedAt) : TrackerEvent;

public sealed class ScrobbleTracker
{
    /// <summary>last.fm не приймає треки, коротші за 30 с.</summary>
    public static readonly TimeSpan MinTrackLength = TimeSpan.FromSeconds(30);

    /// <summary>Скроблимо після половини треку, але не пізніше ніж через 4 хв прослуховування.</summary>
    public static readonly TimeSpan MaxThreshold = TimeSpan.FromMinutes(4);

    /// <summary>
    /// Максимум, який зараховуємо за одне опитування. Якщо комп'ютер заснув або UI завис,
    /// між опитуваннями може пройти багато часу — не вважаємо його прослуханим.
    /// </summary>
    private static readonly TimeSpan MaxTick = TimeSpan.FromSeconds(5);

    /// <summary>Позиція «на початку треку» для розпізнавання повтору тієї ж пісні.</summary>
    private static readonly TimeSpan RepeatStartWindow = TimeSpan.FromSeconds(5);

    private TimeSpan? _lastPosition;
    private DateTimeOffset? _lastUpdate;
    private bool _nowPlayingSent;

    public TrackInfo? Current { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public TimeSpan Played { get; private set; }
    public bool Scrobbled { get; private set; }
    public bool IsPlaying { get; private set; }

    /// <summary>Скільки треба прослухати до скроблу; null — трек занадто короткий, не скроблиться.</summary>
    public TimeSpan? Threshold => Current is null ? null : ComputeThreshold(Current.Duration);

    public static TimeSpan? ComputeThreshold(TimeSpan? duration)
    {
        // Тривалість невідома — чекаємо максимальні 4 хв: так ми точно не заскроблимо
        // трек, який насправді лише промотали.
        if (duration is not { } d || d <= TimeSpan.Zero) return MaxThreshold;
        if (d < MinTrackLength) return null;
        var half = TimeSpan.FromTicks(d.Ticks / 2);
        return half < MaxThreshold ? half : MaxThreshold;
    }

    public IReadOnlyList<TrackerEvent> Update(MediaSnapshot snapshot, DateTimeOffset now)
    {
        var events = new List<TrackerEvent>();

        if (snapshot.Track is null)
        {
            Reset();
            return events;
        }

        if (!snapshot.Track.SameSongAs(Current) || IsRepeat(snapshot.Position))
        {
            StartNew(snapshot.Track);
        }
        else
        {
            // Тривалість могла з'явитися або уточнитися — беремо свіжу.
            if (snapshot.Track.Duration is not null && snapshot.Track.Duration != Current!.Duration)
                Current = Current with { Duration = snapshot.Track.Duration };

            // Зараховуємо час лише якщо грало і в попередньому, і в поточному опитуванні.
            if (IsPlaying && snapshot.IsPlaying && _lastUpdate is { } prev)
            {
                var dt = now - prev;
                if (dt > TimeSpan.Zero) Played += dt < MaxTick ? dt : MaxTick;
            }
        }

        IsPlaying = snapshot.IsPlaying;

        if (IsPlaying && !_nowPlayingSent)
        {
            _nowPlayingSent = true;
            // Час початку — момент, коли трек реально заграв, а не коли його вибрали на паузі.
            StartedAt = now;
            events.Add(new NowPlayingEvent(Current!));
        }

        if (!Scrobbled && _nowPlayingSent && Threshold is { } threshold && Played >= threshold)
        {
            Scrobbled = true;
            events.Add(new ScrobbleEvent(Current!, StartedAt));
        }

        _lastUpdate = now;
        _lastPosition = snapshot.Position;
        return events;
    }

    /// <summary>
    /// Та сама пісня пішла спочатку (режим «повтор одного» або ручний перезапуск):
    /// позиція стрибнула з далекої точки на самий початок. Це нове прослуховування.
    /// </summary>
    private bool IsRepeat(TimeSpan? position) =>
        Current is not null
        && position is { } pos && pos < RepeatStartWindow
        && _lastPosition is { } last && last > MinTrackLength;

    private void StartNew(TrackInfo track)
    {
        Current = track;
        Played = TimeSpan.Zero;
        Scrobbled = false;
        IsPlaying = false;
        _nowPlayingSent = false;
    }

    private void Reset()
    {
        Current = null;
        Played = TimeSpan.Zero;
        Scrobbled = false;
        IsPlaying = false;
        _nowPlayingSent = false;
        _lastPosition = null;
        _lastUpdate = null;
    }
}
