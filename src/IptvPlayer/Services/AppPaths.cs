namespace IptvPlayer.Services;

/// <summary>
/// Папки приложения. В MSIX-пакете это LocalFolder пакета,
/// для сборки без пакета — %LOCALAPPDATA%\IptvPlayer.
/// </summary>
public static class AppPaths
{
    public const string AppName = "IptvPlayer";

    private static string? _root;

    public static string Root => _root ??= Resolve();

    public static string Logs
    {
        get
        {
            var path = Path.Combine(Root, "logs");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    /// <summary>Папка данных версии без пакета — источник для переноса настроек.</summary>
    public static string LegacyRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppName);

    private static string Resolve()
    {
        string root;
        var packaged = true;

        try
        {
            // Для приложения в пакете даёт папку пакета, иначе бросает исключение.
            root = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
        }
        catch (Exception)
        {
            root = LegacyRoot;
            packaged = false;
        }

        Directory.CreateDirectory(root);

        if (packaged) MigrateFromLegacy(root);

        return root;
    }

    /// <summary>
    /// Переносит настройки, плейлисты и избранное из папки версии без пакета.
    /// Выполняется один раз: если в папке пакета уже есть settings.json, ничего не делаем.
    /// </summary>
    private static void MigrateFromLegacy(string root)
    {
        try
        {
            var legacy = LegacyRoot;
            if (string.Equals(legacy, root, StringComparison.OrdinalIgnoreCase)) return;
            if (!Directory.Exists(legacy)) return;
            if (File.Exists(Path.Combine(root, "settings.json"))) return;

            foreach (var name in new[] { "settings.json", "session.json", "playlists.json", "favorites.json" })
            {
                var source = Path.Combine(legacy, name);
                if (File.Exists(source)) File.Copy(source, Path.Combine(root, name), overwrite: false);
            }

            // кэш плейлистов переносим, телепрограмму и логотипы приложение скачает само
            CopyFolder(Path.Combine(legacy, "playlists"), Path.Combine(root, "playlists"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // перенос не критичен: приложение просто начнёт с чистой папки
        }
    }

    private static void CopyFolder(string source, string destination)
    {
        if (!Directory.Exists(source)) return;

        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
    }
}
