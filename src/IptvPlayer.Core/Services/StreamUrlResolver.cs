using System.Text.RegularExpressions;
using IptvPlayer.Core.Models;

namespace IptvPlayer.Core.Services;

/// <summary>
/// Преобразует URL канала между прокси udpxy и прямым мультикастом:
/// http://192.168.3.1:4022/udp/239.195.65.33:1234 ↔ udp://@239.195.65.33:1234.
/// </summary>
public static partial class StreamUrlResolver
{
    [GeneratedRegex(@"^https?://(?<host>[^/]+)/(?<proto>udp|rtp)/(?<group>[^/?#]+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex UdpxyRegex();

    [GeneratedRegex(@"^(?<proto>udp|rtp)://@?(?<group>[^/?#]+)", RegexOptions.IgnoreCase)]
    private static partial Regex MulticastRegex();

    public static bool IsUdpxyUrl(string url) => UdpxyRegex().IsMatch(url);

    public static bool IsMulticastUrl(string url) => MulticastRegex().IsMatch(url);

    /// <summary>udpxy → udp://@group:port. Если URL не udpxy — возвращается как есть.</summary>
    public static string ToMulticast(string url)
    {
        var m = UdpxyRegex().Match(url);
        if (!m.Success) return url;

        var proto = m.Groups["proto"].Value.ToLowerInvariant();
        return $"{proto}://@{m.Groups["group"].Value}";
    }

    /// <summary>
    /// udp://@group:port → http://{udpxyBase}/udp/group:port.
    /// Если URL уже http или база не задана — возвращается как есть.
    /// </summary>
    public static string ToUdpxy(string url, string udpxyBaseUrl)
    {
        var m = MulticastRegex().Match(url);
        if (!m.Success || string.IsNullOrWhiteSpace(udpxyBaseUrl)) return url;

        var baseUrl = udpxyBaseUrl.Trim().TrimEnd('/');
        if (!baseUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            baseUrl = "http://" + baseUrl;

        var proto = m.Groups["proto"].Value.ToLowerInvariant();
        return $"{baseUrl}/{proto}/{m.Groups["group"].Value}";
    }

    /// <summary>Приводит URL канала к режиму, выбранному в настройках.</summary>
    public static string Resolve(string url, StreamSourceMode mode, string udpxyBaseUrl)
        => mode switch
        {
            StreamSourceMode.DirectMulticast => ToMulticast(url),
            StreamSourceMode.Udpxy => ToUdpxy(url, udpxyBaseUrl),
            _ => url,
        };

    /// <summary>Базовый адрес udpxy, выведенный из URL канала (для автонастройки).</summary>
    public static string? ExtractUdpxyBase(string url)
    {
        var m = UdpxyRegex().Match(url);
        return m.Success ? $"http://{m.Groups["host"].Value}" : null;
    }
}
