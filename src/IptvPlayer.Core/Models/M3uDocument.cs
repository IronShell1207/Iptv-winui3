namespace IptvPlayer.Core.Models;

/// <summary>Результат разбора M3U: атрибуты заголовка и каналы.</summary>
public sealed record M3uDocument
{
    public IReadOnlyDictionary<string, string> HeaderAttributes { get; init; }
        = new Dictionary<string, string>();

    public IReadOnlyList<Channel> Channels { get; init; } = Array.Empty<Channel>();

    /// <summary>URL EPG из атрибута url-tvg (или x-tvg-url).</summary>
    public string? EpgUrl =>
        HeaderAttributes.TryGetValue("url-tvg", out var u) ? u :
        HeaderAttributes.TryGetValue("x-tvg-url", out var x) ? x : null;
}
