// Приведення метаданих від Apple Music до виду, який розуміє last.fm.
// Викликається з AppleMusicWatcher для кожного знімка плеєра.
// Чому це потрібно — docs/decisions.md, D-2.
using System;

namespace ScrobbleMe.Core;

public static class AppleMusicMetadata
{
    /// <summary>
    /// Застосунок Apple Music для Windows кладе в поле «виконавець» рядок
    /// «Виконавець — Альбом» (з довгим тире), а поле «альбом» лишає порожнім.
    /// Розрізаємо по першому « — », але лише коли альбом справді порожній,
    /// щоб не зіпсувати виконавців, у чиїй назві є тире, з інших плеєрів.
    /// </summary>
    public const string Separator = " — ";

    public static TrackInfo? Normalize(string? artist, string? albumArtist, string? album, string? title, TimeSpan? duration)
    {
        artist = (artist ?? "").Trim();
        album = (album ?? "").Trim();
        title = (title ?? "").Trim();

        if (album.Length == 0)
        {
            var i = artist.IndexOf(Separator, StringComparison.Ordinal);
            if (i > 0)
            {
                album = artist[(i + Separator.Length)..].Trim();
                artist = artist[..i].Trim();
            }
        }

        if (artist.Length == 0) artist = (albumArtist ?? "").Trim();

        // Без виконавця або назви last.fm скробл не прийме — краще нічого не надсилати.
        if (artist.Length == 0 || title.Length == 0) return null;

        return new TrackInfo(artist, title, album, duration);
    }
}
