namespace IptvPlayer.Tests;

internal static class TestData
{
    private static string Path(string name)
        => System.IO.Path.Combine(AppContext.BaseDirectory, "Data", name);

    public static string Playlist => File.ReadAllText(Path("playlist-sample.m3u"));

    public static string Epg => File.ReadAllText(Path("epg-sample.xml"));

    public static byte[] EpgBytes => File.ReadAllBytes(Path("epg-sample.xml"));
}
