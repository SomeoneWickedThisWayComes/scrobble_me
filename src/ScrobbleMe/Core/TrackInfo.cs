// Моделі даних про трек і стан плеєра.
// Використовуються ScrobbleTracker (логіка скроблінгу) і AppleMusicWatcher (читання з Windows).
// Файл без залежностей від Windows — його компілюють і тести на будь-якій ОС.
using System;

namespace ScrobbleMe.Core;

/// <summary>Трек, як його бачить last.fm. Duration може бути невідомою (плеєр не віддав таймлайн).</summary>
public sealed record TrackInfo(string Artist, string Title, string Album, TimeSpan? Duration)
{
    /// <summary>
    /// Чи це та сама пісня. Тривалість навмисно не порівнюємо: Apple Music часто спершу
    /// віддає трек без таймлайну, а за секунду — з ним, і це не має рахуватися новим треком.
    /// </summary>
    public bool SameSongAs(TrackInfo? other) =>
        other is not null
        && string.Equals(Artist, other.Artist, StringComparison.Ordinal)
        && string.Equals(Title, other.Title, StringComparison.Ordinal)
        && string.Equals(Album, other.Album, StringComparison.Ordinal);

    public override string ToString() => $"{Artist} — {Title}";
}

/// <summary>Один «знімок» плеєра на момент опитування.</summary>
/// <param name="Track">null, якщо плеєр не запущено або нічого не вибрано.</param>
/// <param name="IsPlaying">true лише в стані «відтворюється» (не пауза, не стоп).</param>
/// <param name="Position">Позиція в треку, якщо відома.</param>
public sealed record MediaSnapshot(TrackInfo? Track, bool IsPlaying, TimeSpan? Position)
{
    public static readonly MediaSnapshot Empty = new(null, false, null);
}
