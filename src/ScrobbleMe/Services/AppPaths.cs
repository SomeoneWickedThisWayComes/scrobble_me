// Шляхи до файлів застосунку і простий журнал подій.
// Усе лежить у %APPDATA%\ScrobbleMe: settings.json, queue.json, log.txt.
// Журнал потрібен, щоб розібратися, чому трек не заскробився, — у UI видно лише останню помилку.
using System;
using System.IO;

namespace ScrobbleMe.Services;

public static class AppPaths
{
    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScrobbleMe");

    public static string Settings => Path.Combine(DataDir, "settings.json");
    public static string Queue => Path.Combine(DataDir, "queue.json");
    public static string Log => Path.Combine(DataDir, "log.txt");
}

public static class Log
{
    /// <summary>Після 1 МБ журнал перейменовується в log.old.txt — цього вистачає на тижні роботи.</summary>
    private const long MaxSize = 1024 * 1024;
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.DataDir);
                var info = new FileInfo(AppPaths.Log);
                if (info.Exists && info.Length > MaxSize)
                    File.Move(AppPaths.Log, Path.Combine(AppPaths.DataDir, "log.old.txt"), overwrite: true);
                File.AppendAllText(AppPaths.Log, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
            // Журнал — допоміжна річ; якщо файл зайнятий, просто пропускаємо рядок.
        }
    }

    public static void Error(string context, Exception ex) => Write($"ПОМИЛКА {context}: {ex.GetType().Name}: {ex.Message}");
}
