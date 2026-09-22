namespace IptvPlayer.Core.Models;

/// <summary>Элемент &lt;channel&gt; из XMLTV.</summary>
public sealed record EpgChannel
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string? IconUrl { get; init; }
}
