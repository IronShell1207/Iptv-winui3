namespace IptvPlayer.Core.Models;

/// <summary>Передача из XMLTV.</summary>
public sealed record EpgProgramme
{
    public required string ChannelId { get; init; }
    public required DateTimeOffset Start { get; init; }
    public required DateTimeOffset Stop { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public string? Category { get; init; }

    public TimeSpan Duration => Stop - Start;

    /// <summary>Доля прошедшего времени передачи в диапазоне 0..1.</summary>
    public double ProgressAt(DateTimeOffset now)
    {
        if (Duration <= TimeSpan.Zero) return 0;
        var p = (now - Start) / Duration;
        return Math.Clamp(p, 0, 1);
    }
}
