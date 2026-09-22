namespace IptvPlayer.Core.Models;

/// <summary>Канал из M3U-плейлиста.</summary>
public sealed record Channel
{
    public required string Name { get; init; }
    public required string Url { get; init; }
    public string? TvgId { get; init; }
    public string? TvgName { get; init; }
    public string? Group { get; init; }
    public string? LogoUrl { get; init; }

    /// <summary>Порядковый номер в плейлисте, с 1.</summary>
    public int Number { get; init; }

    /// <summary>Все атрибуты строки #EXTINF, включая неизвестные.</summary>
    public IReadOnlyDictionary<string, string> Attributes { get; init; }
        = new Dictionary<string, string>();

    /// <summary>
    /// Стабильный ключ канала для избранного и восстановления сессии:
    /// tvg-id, если есть, иначе URL.
    /// </summary>
    public string Key => string.IsNullOrWhiteSpace(TvgId) ? Url : TvgId;
}
