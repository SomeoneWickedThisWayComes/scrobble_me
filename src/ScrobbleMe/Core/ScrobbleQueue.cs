// Черга скроблів на диску: скробл спершу записується сюди, і лише потім надсилається.
// Так скробли не губляться без інтернету, при збоях last.fm чи якщо ще не виконано вхід.
// Використовується App; файл — %APPDATA%\ScrobbleMe\queue.json. Див. docs/decisions.md, D-4.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ScrobbleMe.Core;

public sealed class ScrobbleQueue
{
    /// <summary>last.fm відкидає скробли, старші за 14 днів, — зберігати їх довше марно.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(14);

    private readonly string _path;
    private readonly List<QueuedScrobble> _items;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _flushLock = new(1, 1);

    public ScrobbleQueue(string path)
    {
        _path = path;
        _items = Load(path);
    }

    public int Count { get { lock (_gate) return _items.Count; } }

    public void Enqueue(QueuedScrobble s)
    {
        lock (_gate)
        {
            // Захист від подвійного запису того самого прослуховування.
            if (_items.Any(x => x.Timestamp == s.Timestamp && x.Title == s.Title && x.Artist == s.Artist)) return;
            _items.Add(s);
            Save();
        }
    }

    /// <summary>
    /// Надсилає все, що є в черзі, пачками по 50. Повертає кількість надісланих.
    /// Мережеві й тимчасові помилки пробрасуються нагору — черга лишається як була,
    /// App спробує пізніше. Пачки з некоректними даними (коди 6, 13) викидаються,
    /// інакше вони блокували б чергу назавжди.
    /// </summary>
    public async Task<int> FlushAsync(LastFmClient client, DateTimeOffset now, Action<string>? log = null, CancellationToken ct = default)
    {
        await _flushLock.WaitAsync(ct);
        try
        {
            PruneOld(now, log);
            var sent = 0;
            while (true)
            {
                List<QueuedScrobble> batch;
                lock (_gate) batch = _items.Take(LastFmClient.MaxBatch).ToList();
                if (batch.Count == 0) return sent;

                try
                {
                    await client.ScrobbleAsync(batch, ct);
                    sent += batch.Count;
                }
                catch (LastFmException ex) when (ex.Code is 6 or 13)
                {
                    log?.Invoke($"Відкинуто {batch.Count} скроблів, last.fm їх не приймає: {ex.Message}");
                }

                lock (_gate)
                {
                    foreach (var s in batch) _items.Remove(s);
                    Save();
                }
            }
        }
        finally
        {
            _flushLock.Release();
        }
    }

    private void PruneOld(DateTimeOffset now, Action<string>? log)
    {
        lock (_gate)
        {
            var limit = (now - MaxAge).ToUnixTimeSeconds();
            var removed = _items.RemoveAll(s => s.Timestamp < limit);
            if (removed > 0)
            {
                log?.Invoke($"Видалено з черги {removed} скроблів, старших за 14 днів");
                Save();
            }
        }
    }

    private static List<QueuedScrobble> Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new();
            return JsonSerializer.Deserialize<List<QueuedScrobble>>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception)
        {
            // Пошкоджений файл не має ламати запуск: відкладаємо його вбік і починаємо з нуля.
            try { File.Move(path, path + ".broken", overwrite: true); } catch (IOException) { }
            return new();
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        // Запис через тимчасовий файл: якщо вимкнуть світло посеред запису, стара черга вціліє.
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_items));
        File.Move(tmp, _path, overwrite: true);
    }
}
