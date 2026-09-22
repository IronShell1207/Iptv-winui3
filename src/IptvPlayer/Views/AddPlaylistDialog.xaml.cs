using IptvPlayer.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace IptvPlayer.Views;

public sealed partial class AddPlaylistDialog : ContentDialog
{
    public AddPlaylistDialog(PlaylistSourceKind kind)
    {
        InitializeComponent();

        KindBox.SelectedIndex = kind switch
        {
            PlaylistSourceKind.Xtream => 1,
            PlaylistSourceKind.LocalFile => 2,
            _ => 0,
        };

        PrimaryButtonClick += OnPrimaryButtonClick;
    }

    public string PlaylistName => string.IsNullOrWhiteSpace(NameBox.Text)
        ? DefaultName()
        : NameBox.Text.Trim();

    public PlaylistSourceKind Kind => KindBox.SelectedIndex switch
    {
        1 => PlaylistSourceKind.Xtream,
        2 => PlaylistSourceKind.LocalFile,
        _ => PlaylistSourceKind.M3uUrl,
    };

    public string Source => SourceBox.Text.Trim();

    public string? Username => Kind == PlaylistSourceKind.Xtream ? UserBox.Text.Trim() : null;

    public string? Password => Kind == PlaylistSourceKind.Xtream ? PasswordBox.Password : null;

    public string? EpgUrl => EpgBox.Text.Trim() is { Length: > 0 } url ? url : null;

    private void OnKindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CredentialsPanel is null) return;

        var xtream = KindBox.SelectedIndex == 1;
        CredentialsPanel.Visibility = xtream ? Visibility.Visible : Visibility.Collapsed;

        SourceBox.Header = KindBox.SelectedIndex switch
        {
            1 => "Адрес сервера",
            2 => "Путь к файлу",
            _ => "Адрес плейлиста",
        };

        SourceBox.PlaceholderText = KindBox.SelectedIndex switch
        {
            1 => "http://example.com:8080",
            2 => @"C:\iptv\playlist.m3u",
            _ => AppSettings.DefaultPlaylistUrl,
        };
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var error = Validate();
        if (error is null) return;

        ErrorText.Text = error;
        ErrorText.Visibility = Visibility.Visible;
        args.Cancel = true;
    }

    private string? Validate()
    {
        if (Source.Length == 0)
            return "Укажите адрес источника.";

        if (Kind == PlaylistSourceKind.LocalFile)
            return File.Exists(Source) ? null : "Файл не найден.";

        var url = Source.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? Source
            : "http://" + Source;

        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            return "Адрес выглядит неправильно.";

        if (Kind == PlaylistSourceKind.Xtream && string.IsNullOrWhiteSpace(UserBox.Text))
            return "Для Xtream Codes нужны логин и пароль.";

        return null;
    }

    private string DefaultName() => Kind switch
    {
        PlaylistSourceKind.Xtream => "Xtream",
        PlaylistSourceKind.LocalFile => Path.GetFileNameWithoutExtension(Source),
        _ => Uri.TryCreate(Source, UriKind.Absolute, out var uri) ? uri.Host : "Новый плейлист",
    };
}
