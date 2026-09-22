using System.Text;
using IptvPlayer.Core.Models;

namespace IptvPlayer.Core.Parsing;

/// <summary>
/// Разбор M3U/M3U8. Терпим к реальным плейлистам: атрибуты с кавычками и без,
/// запятые внутри названия, \n и \r\n, неизвестные атрибуты сохраняются.
/// </summary>
public static class M3uParser
{
    public static M3uDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var header = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var channels = new List<Channel>();

        string? pendingName = null;
        Dictionary<string, string>? pendingAttrs = null;
        var number = 0;

        foreach (var raw in EnumerateLines(text))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var (k, v) in ParseAttributes(line.AsSpan("#EXTM3U".Length)))
                    header[k] = v;
                continue;
            }

            if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                (pendingName, pendingAttrs) = ParseExtInf(line.AsSpan("#EXTINF:".Length));
                continue;
            }

            // #EXTGRP задаёт группу для следующих записей, если её нет в group-title
            if (line.StartsWith("#EXTGRP:", StringComparison.OrdinalIgnoreCase))
            {
                var grp = line["#EXTGRP:".Length..].Trim();
                if (pendingAttrs is not null && grp.Length > 0 &&
                    !pendingAttrs.ContainsKey("group-title"))
                {
                    pendingAttrs["group-title"] = grp;
                }
                continue;
            }

            if (line[0] == '#') continue;   // прочие директивы игнорируем

            if (pendingName is null)
            {
                // URL без #EXTINF — берём имя из последнего сегмента
                pendingName = GuessNameFromUrl(line);
                pendingAttrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            var attrs = pendingAttrs ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            channels.Add(new Channel
            {
                Name = pendingName,
                Url = line,
                Number = ++number,
                TvgId = Get(attrs, "tvg-id"),
                TvgName = Get(attrs, "tvg-name"),
                Group = Get(attrs, "group-title"),
                LogoUrl = Get(attrs, "tvg-logo"),
                Attributes = attrs,
            });

            pendingName = null;
            pendingAttrs = null;
        }

        return new M3uDocument { HeaderAttributes = header, Channels = channels };
    }

    private static string? Get(IReadOnlyDictionary<string, string> attrs, string key)
        => attrs.TryGetValue(key, out var v) && v.Length > 0 ? v : null;

    /// <summary>
    /// Разбирает хвост строки #EXTINF: «-1 tvg-id=… group-title="…" ,Название».
    /// Разделитель — первая запятая вне кавычек: в названии запятые встречаются
    /// («Матч! Игра, повтор»), а в блоке атрибутов — нет.
    /// </summary>
    private static (string Name, Dictionary<string, string> Attrs) ParseExtInf(ReadOnlySpan<char> tail)
    {
        var commaIndex = FirstCommaOutsideQuotes(tail);
        ReadOnlySpan<char> attrPart;
        string name;

        if (commaIndex >= 0)
        {
            attrPart = tail[..commaIndex];
            name = tail[(commaIndex + 1)..].Trim().ToString();
        }
        else
        {
            attrPart = tail;
            name = string.Empty;
        }

        var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in ParseAttributes(attrPart))
            attrs[k] = v;

        if (name.Length == 0)
            name = Get(attrs, "tvg-name") ?? "Без названия";

        return (name, attrs);
    }

    /// <summary>Первая запятая вне кавычек — разделитель атрибутов и названия.</summary>
    private static int FirstCommaOutsideQuotes(ReadOnlySpan<char> s)
    {
        var inQuotes = false;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '"') inQuotes = !inQuotes;
            else if (c == ',' && !inQuotes) return i;
        }
        return -1;
    }

    /// <summary>Пары key=value и key="value"; значения без кавычек заканчиваются пробелом.</summary>
    private static IEnumerable<(string Key, string Value)> ParseAttributes(ReadOnlySpan<char> s)
    {
        var result = new List<(string, string)>();
        var i = 0;

        while (i < s.Length)
        {
            while (i < s.Length && (char.IsWhiteSpace(s[i]) || s[i] == ',')) i++;
            if (i >= s.Length) break;

            var keyStart = i;
            while (i < s.Length && s[i] != '=' && !char.IsWhiteSpace(s[i])) i++;
            if (i >= s.Length || s[i] != '=')
            {
                // одиночный токен (например длительность «-1») — пропускаем
                while (i < s.Length && !char.IsWhiteSpace(s[i])) i++;
                continue;
            }

            var key = s[keyStart..i].ToString();
            i++; // '='

            string value;
            if (i < s.Length && s[i] == '"')
            {
                i++;
                var vStart = i;
                while (i < s.Length && s[i] != '"') i++;
                value = s[vStart..Math.Min(i, s.Length)].ToString();
                if (i < s.Length) i++; // закрывающая кавычка
            }
            else
            {
                var vStart = i;
                while (i < s.Length && !char.IsWhiteSpace(s[i])) i++;
                value = s[vStart..i].ToString();
            }

            if (key.Length > 0)
                result.Add((key, value));
        }

        return result;
    }

    private static IEnumerable<string> EnumerateLines(string text)
    {
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\n' or '\r')) continue;

            yield return text[start..i];
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            start = i + 1;
        }
        if (start < text.Length)
            yield return text[start..];
    }

    private static string GuessNameFromUrl(string url)
    {
        var slash = url.LastIndexOf('/');
        var name = slash >= 0 && slash < url.Length - 1 ? url[(slash + 1)..] : url;
        return name.Length == 0 ? url : name;
    }

    /// <summary>Читает плейлист из байтов, снимая BOM и учитывая UTF-8.</summary>
    public static M3uDocument ParseBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            bytes = bytes[3..];
        return Parse(Encoding.UTF8.GetString(bytes));
    }
}
