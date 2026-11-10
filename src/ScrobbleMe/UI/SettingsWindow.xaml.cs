// Логіка вікна налаштувань: вхід у last.fm (auth.getMobileSession), вихід, збереження.
// Змінює AppSettings лише після успішного входу або натискання «Зберегти»;
// App підхоплює зміни через подію Applied.
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using ScrobbleMe.Core;
using ScrobbleMe.Services;

namespace ScrobbleMe.UI;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    // Сесія, отримана в цьому вікні, але ще не збережена (до «Зберегти» або входу).
    private string? _userName;
    private string _sessionKey;

    public event Action? Applied;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        _userName = settings.UserName;
        _sessionKey = settings.SessionKey;

        ApiKeyBox.Text = settings.ApiKey;
        ApiSecretBox.Password = settings.ApiSecret;
        UserBox.Text = settings.UserName ?? "";
        AutostartBox.IsChecked = settings.StartWithWindows;
        RefreshLoginState();
    }

    private void RefreshLoginState()
    {
        var loggedIn = !string.IsNullOrEmpty(_sessionKey);
        LoggedInText.Text = loggedIn ? $"✓ Увійшли як {_userName}" : "Вхід не виконано";
        LoggedInText.Foreground = loggedIn ? System.Windows.Media.Brushes.ForestGreen : System.Windows.Media.Brushes.Gray;
        LoginPanel.Visibility = loggedIn ? Visibility.Collapsed : Visibility.Visible;
        LoginButton.Visibility = loggedIn ? Visibility.Collapsed : Visibility.Visible;
        LogoutButton.Visibility = loggedIn ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnLoginClick(object sender, RoutedEventArgs e)
    {
        var apiKey = ApiKeyBox.Text.Trim();
        var apiSecret = ApiSecretBox.Password.Trim();
        var user = UserBox.Text.Trim();
        var password = PasswordBox.Password;

        if (apiKey.Length == 0 || apiSecret.Length == 0) { ShowError("Спершу вкажіть API key і Shared secret."); return; }
        if (user.Length == 0 || password.Length == 0) { ShowError("Вкажіть логін і пароль last.fm."); return; }

        LoginButton.IsEnabled = false;
        ShowError(null);
        try
        {
            using var client = new LastFmClient(apiKey, apiSecret);
            var (name, key) = await client.GetMobileSessionAsync(user, password);
            _userName = name;
            _sessionKey = key;
            PasswordBox.Clear();

            // Успішний вхід зберігаємо одразу — щоб не загубити сесію, якщо вікно закриють хрестиком.
            Commit();
            Log.Write($"Вхід у last.fm як {name}");
        }
        catch (LastFmException ex) when (ex.Code == 4)
        {
            ShowError("Неправильний логін або пароль.");
        }
        catch (LastFmException ex) when (ex.Code is 10 or 26)
        {
            ShowError("last.fm не приймає цей API key. Перевірте API key і Shared secret.");
        }
        catch (LastFmException ex) when (ex.Code == 13)
        {
            ShowError("Неправильний Shared secret.");
        }
        catch (Exception ex)
        {
            ShowError($"Не вдалося увійти: {ex.Message}");
        }
        finally
        {
            LoginButton.IsEnabled = true;
            RefreshLoginState();
        }
    }

    private void OnLogoutClick(object sender, RoutedEventArgs e)
    {
        _sessionKey = "";
        RefreshLoginState();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        Commit();
        Close();
    }

    private void Commit()
    {
        var newKey = ApiKeyBox.Text.Trim();
        var newSecret = ApiSecretBox.Password.Trim();
        // Сесія прив'язана до API key: якщо ключі змінили, стара сесія вже недійсна.
        if (newKey != _settings.ApiKey && _settings.ApiKey.Length > 0 && _sessionKey == _settings.SessionKey)
            _sessionKey = "";

        _settings.ApiKey = newKey;
        _settings.ApiSecret = newSecret;
        _settings.UserName = string.IsNullOrEmpty(_sessionKey) ? null : _userName;
        _settings.SessionKey = _sessionKey;
        _settings.StartWithWindows = AutostartBox.IsChecked == true;
        _settings.Save();
        Autostart.Apply(_settings.StartWithWindows);
        Applied?.Invoke();
    }

    private void ShowError(string? text)
    {
        LoginError.Text = text ?? "";
        LoginError.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnLink(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
