using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IptvPlayer.Core.Services;

/// <summary>
/// Атомарное чтение/запись JSON в папке данных приложения.
/// Запись идёт во временный файл и затем переименовывается, чтобы
/// аварийное завершение не оставило усечённый файл.
/// </summary>
public sealed class JsonStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonStore(string rootFolder)
    {
        RootFolder = rootFolder;
        Directory.CreateDirectory(RootFolder);
    }

    public string RootFolder { get; }

    public string PathFor(string fileName) => Path.Combine(RootFolder, fileName);

    public async Task<T?> LoadAsync<T>(string fileName, CancellationToken ct = default)
    {
        var path = PathFor(fileName);
        if (!File.Exists(path)) return default;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, Options, ct).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // повреждённый файл не должен ронять приложение
            return default;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync<T>(string fileName, T value, CancellationToken ct = default)
    {
        var path = PathFor(fileName);
        var temp = path + ".tmp";

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using (var stream = File.Create(temp))
            {
                await JsonSerializer.SerializeAsync(stream, value, Options, ct).ConfigureAwait(false);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch (IOException) { /* остатки временного файла не критичны */ }
            }
            _gate.Release();
        }
    }
}
