// Код головного віконця: малює стан, який йому передає App, і ховає вікно в трей замість закриття.
// Логіки скроблінгу тут немає — вікно лише показує NowPlayingView.
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using ScrobbleMe.Core;

namespace ScrobbleMe.UI;

/// <summary>Усе, що потрібно вікну для відображення. Збирається в App на кожному опитуванні.</summary>
public sealed record NowPlayingView(
    TrackInfo? Track,
    bool IsPlaying,
    double Progress,
    string Status,
    string? Error,
    string Account);

public partial class MainWindow : Window
{
    private bool _placed;

    /// <summary>Встановлює App перед виходом, щоб хрестик знову справді закривав вікно.</summary>
    public bool AllowClose { get; set; }

    public event Action? SettingsRequested;
    public event Action? HiddenToTray;

    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>Показати й підняти вікно. Перший раз ставимо його в правий нижній кут — поруч із треєм.</summary>
    public void ShowAndActivate()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        if (!_placed)
        {
            UpdateLayout();
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - 16;
            Top = area.Bottom - ActualHeight - 16;
            _placed = true;
        }
        Activate();
    }

    public void Render(NowPlayingView v)
    {
        if (v.Track is { } t)
        {
            TitleText.Text = t.Title;
            ArtistText.Text = t.Artist;
            AlbumText.Text = t.Album;
            AlbumText.Visibility = t.Album.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            TitleText.Text = "Нічого не грає";
            ArtistText.Text = "";
            AlbumText.Text = "";
            AlbumText.Visibility = Visibility.Collapsed;
        }

        ScrobbleProgress.Value = Math.Clamp(v.Progress, 0, 1);
        StatusText.Text = v.Status;
        ErrorText.Text = v.Error ?? "";
        ErrorText.Visibility = string.IsNullOrEmpty(v.Error) ? Visibility.Collapsed : Visibility.Visible;
        AccountText.Text = v.Account;
    }

    public void SetArtwork(ImageSource? image)
    {
        ArtImage.Source = image;
        ArtPlaceholder.Visibility = image is null ? Visibility.Visible : Visibility.Collapsed;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Хрестик = згорнути в трей. Справжній вихід — через меню значка в треї.
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
            HiddenToTray?.Invoke();
        }
        base.OnClosing(e);
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
}
