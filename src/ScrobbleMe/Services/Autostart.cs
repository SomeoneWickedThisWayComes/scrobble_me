// Автозапуск разом з Windows через ключ реєстру HKCU\...\Run.
// Вмикається галочкою у вікні налаштувань. HKCU, а не HKLM, — щоб не потрібні були права адміністратора.
// Запускаємо з --minimized, щоб при вході в систему застосунок одразу йшов у трей.
using System;
using Microsoft.Win32;

namespace ScrobbleMe.Services;

public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ScrobbleMe";

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                var exe = Environment.ProcessPath ?? throw new InvalidOperationException("невідомий шлях до exe");
                key.SetValue(ValueName, $"\"{exe}\" --minimized");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            Log.Error("налаштування автозапуску", ex);
        }
    }
}
