namespace IptvPlayer.Services;

/// <summary>
/// Папки приложения. Для unpackaged-сборки это %LOCALAPPDATA%\IptvPlayer,
/// для packaged — LocalFolder пакета.
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

    private static string Resolve()
    {
        string root;
        try
        {
            // Для packaged-приложения даёт папку пакета, для unpackaged бросает исключение.
            root = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
        }
        catch (Exception)
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppName);
        }

        Directory.CreateDirectory(root);
        return root;
    }
}
