namespace IptvPlayer.Core.Models;

/// <summary>Состояние между запусками (session.json).</summary>
public sealed record SessionState
{
    public string? LastPlaylistId { get; init; }
    public string? LastChannelKey { get; init; }
    public int? WindowX { get; init; }
    public int? WindowY { get; init; }
    public int? WindowWidth { get; init; }
    public int? WindowHeight { get; init; }
    public bool IsMaximized { get; init; }
}
