using System.Globalization;
using System.IO.Compression;
using System.Xml;
using IptvPlayer.Core.Models;

namespace IptvPlayer.Core.Parsing;

public sealed record XmltvDocument
{
    public IReadOnlyList<EpgChannel> Channels { get; init; } = Array.Empty<EpgChannel>();
    public IReadOnlyList<EpgProgramme> Programmes { get; init; } = Array.Empty<EpgProgramme>();
}

/// <summary>Потоковый разбор XMLTV через XmlReader — файл может быть в несколько мегабайт.</summary>
public static class XmltvParser
{
    public static bool LooksGzipped(ReadOnlySpan<byte> head)
        => head.Length >= 2 && head[0] == 0x1f && head[1] == 0x8b;

    /// <summary>
    /// Разбирает поток, самостоятельно определяя gzip по magic bytes
    /// (Content-Encoding у провайдеров врёт).
    /// </summary>
    public static async Task<XmltvDocument> ParseAsync(Stream stream, CancellationToken ct = default)
    {
        var buffered = stream.CanSeek ? stream : await BufferAsync(stream, ct).ConfigureAwait(false);

        var head = new byte[2];
        var read = await buffered.ReadAsync(head.AsMemory(), ct).ConfigureAwait(false);
        buffered.Seek(0, SeekOrigin.Begin);

        Stream content = read >= 2 && LooksGzipped(head)
            ? new GZipStream(buffered, CompressionMode.Decompress, leaveOpen: true)
            : buffered;

        try
        {
            return Parse(content);
        }
        finally
        {
            if (!ReferenceEquals(content, buffered))
                await content.DisposeAsync().ConfigureAwait(false);
            if (!ReferenceEquals(buffered, stream))
                await buffered.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task<Stream> BufferAsync(Stream source, CancellationToken ct)
    {
        var ms = new MemoryStream();
        await source.CopyToAsync(ms, ct).ConfigureAwait(false);
        ms.Seek(0, SeekOrigin.Begin);
        return ms;
    }

    public static XmltvDocument Parse(Stream stream)
    {
        using var reader = XmlReader.Create(stream, CreateSettings());
        return Read(reader);
    }

    public static XmltvDocument ParseText(string xml)
    {
        using var sr = new StringReader(xml);
        using var reader = XmlReader.Create(sr, CreateSettings());
        return Read(reader);
    }

    private static XmlReaderSettings CreateSettings() => new()
    {
        DtdProcessing = DtdProcessing.Ignore,
        IgnoreComments = true,
        IgnoreWhitespace = true,
        XmlResolver = null,
    };

    private static XmltvDocument Read(XmlReader reader)
    {
        var channels = new List<EpgChannel>();
        var programmes = new List<EpgProgramme>();

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element) continue;

            switch (reader.Name)
            {
                case "channel":
                    var channel = ReadChannel(reader);
                    if (channel is not null) channels.Add(channel);
                    break;

                case "programme":
                    var programme = ReadProgramme(reader);
                    if (programme is not null) programmes.Add(programme);
                    break;
            }
        }

        return new XmltvDocument { Channels = channels, Programmes = programmes };
    }

    /// <summary>
    /// Обходит непосредственных детей текущего элемента, не сдвигая внешний reader.
    /// Обработчик возвращает true, если он сам продвинул reader за элемент
    /// (как это делает ReadElementContentAsString).
    /// </summary>
    private static void ReadChildren(XmlReader reader, Func<XmlReader, bool> onElement)
    {
        if (reader.IsEmptyElement) return;

        using var sub = reader.ReadSubtree();
        sub.Read();             // корневой элемент субдерева
        if (!sub.Read()) return; // первый дочерний узел

        while (!sub.EOF && sub.NodeType != XmlNodeType.EndElement)
        {
            if (sub.NodeType != XmlNodeType.Element)
            {
                sub.Read();
                continue;
            }

            if (!onElement(sub))
                sub.Skip();
        }
    }

    private static EpgChannel? ReadChannel(XmlReader reader)
    {
        var id = reader.GetAttribute("id");
        if (string.IsNullOrWhiteSpace(id)) return null;

        string? displayName = null;
        string? icon = null;

        ReadChildren(reader, child =>
        {
            switch (child.Name)
            {
                case "display-name":
                    var name = child.ReadElementContentAsString().Trim();
                    displayName ??= name;
                    return true;
                case "icon":
                    icon ??= child.GetAttribute("src");
                    return false;
                default:
                    return false;
            }
        });

        return new EpgChannel
        {
            Id = id.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id.Trim() : displayName,
            IconUrl = icon,
        };
    }

    private static EpgProgramme? ReadProgramme(XmlReader reader)
    {
        var channelId = reader.GetAttribute("channel");
        var startRaw = reader.GetAttribute("start");
        var stopRaw = reader.GetAttribute("stop");

        if (string.IsNullOrWhiteSpace(channelId) || !TryParseXmltvDate(startRaw, out var start))
            return null;

        if (!TryParseXmltvDate(stopRaw, out var stop))
            stop = start;

        string? title = null, desc = null, category = null;

        ReadChildren(reader, child =>
        {
            switch (child.Name)
            {
                case "title":
                    var t = child.ReadElementContentAsString().Trim();
                    title ??= t;
                    return true;
                case "desc":
                    var d = child.ReadElementContentAsString().Trim();
                    desc ??= d;
                    return true;
                case "category":
                    var c = child.ReadElementContentAsString().Trim();
                    category ??= c;
                    return true;
                default:
                    return false;
            }
        });

        return new EpgProgramme
        {
            ChannelId = channelId.Trim(),
            Start = start,
            Stop = stop,
            Title = string.IsNullOrWhiteSpace(title) ? "Без названия" : title,
            Description = string.IsNullOrWhiteSpace(desc) ? null : desc,
            Category = string.IsNullOrWhiteSpace(category) ? null : category,
        };
    }

    /// <summary>
    /// Формат XMLTV: YYYYMMDDHHMMSS [±HHMM], секунды и смещение необязательны.
    /// Без смещения дата трактуется как местное время.
    /// </summary>
    public static bool TryParseXmltvDate(string? value, out DateTimeOffset result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var s = value.Trim();
        var space = s.IndexOf(' ');
        var datePart = space >= 0 ? s[..space] : s;
        var offsetPart = space >= 0 ? s[(space + 1)..].Trim() : string.Empty;

        // допускаем форму без пробела: 20260922183000+0300
        if (offsetPart.Length == 0 && datePart.Length > 8)
        {
            var sign = datePart.IndexOfAny(SignChars, 8);
            if (sign > 0)
            {
                offsetPart = datePart[sign..];
                datePart = datePart[..sign];
            }
        }

        if (datePart.Length is < 8 or > 14 || !datePart.All(char.IsAsciiDigit))
            return false;

        datePart = datePart.PadRight(14, '0');
        if (!DateTime.TryParseExact(datePart, "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return false;

        var offset = TryParseOffset(offsetPart, out var off)
            ? off
            : TimeZoneInfo.Local.GetUtcOffset(dt);

        result = new DateTimeOffset(dt, offset);
        return true;
    }

    private static readonly char[] SignChars = { '+', '-' };

    private static bool TryParseOffset(string s, out TimeSpan offset)
    {
        offset = TimeSpan.Zero;
        if (s.Length == 0) return false;

        if (s.Equals("Z", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("UTC", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("GMT", StringComparison.OrdinalIgnoreCase))
            return true;

        var sign = s[0] switch { '+' => 1, '-' => -1, _ => 0 };
        if (sign == 0) return false;

        var digits = s[1..].Replace(":", string.Empty);
        if (digits.Length is not (2 or 4) || !digits.All(char.IsAsciiDigit)) return false;

        var hours = int.Parse(digits[..2], CultureInfo.InvariantCulture);
        var minutes = digits.Length == 4 ? int.Parse(digits[2..], CultureInfo.InvariantCulture) : 0;

        if (hours > 14 || minutes > 59) return false;

        offset = sign * new TimeSpan(hours, minutes, 0);
        return true;
    }
}
