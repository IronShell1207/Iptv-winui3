namespace IptvPlayer.ViewModels;

/// <summary>
/// Карточка «Добавить плейлист». Лежит в той же коллекции, что и плейлисты,
/// чтобы попадать в общую сетку последней — как в макете.
/// </summary>
public sealed class AddPlaylistPlaceholder
{
    public static AddPlaylistPlaceholder Instance { get; } = new();

    private AddPlaylistPlaceholder()
    {
    }
}
