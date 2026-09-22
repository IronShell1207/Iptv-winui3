using IptvPlayer.Core.Models;

namespace IptvPlayer.Core.Services;

/// <summary>Избранные каналы, хранятся по <see cref="Channel.Key"/> для каждого плейлиста.</summary>
public sealed class FavoritesService
{
    private const string FileName = "favorites.json";

    private readonly JsonStore _store;
    private Dictionary<string, HashSet<string>> _byPlaylist = new(StringComparer.Ordinal);

    public FavoritesService(JsonStore store) => _store = store;

    public event EventHandler<string>? Changed;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var data = await _store.LoadAsync<Dictionary<string, List<string>>>(FileName, ct)
            .ConfigureAwait(false);

        _byPlaylist = data?.ToDictionary(
                          kv => kv.Key,
                          kv => new HashSet<string>(kv.Value, StringComparer.Ordinal),
                          StringComparer.Ordinal)
                      ?? new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
    }

    public bool IsFavorite(string playlistId, string channelKey)
        => _byPlaylist.TryGetValue(playlistId, out var set) && set.Contains(channelKey);

    public IReadOnlyCollection<string> ForPlaylist(string playlistId)
        => _byPlaylist.TryGetValue(playlistId, out var set)
            ? set
            : Array.Empty<string>();

    public async Task<bool> ToggleAsync(string playlistId, string channelKey, CancellationToken ct = default)
    {
        if (!_byPlaylist.TryGetValue(playlistId, out var set))
        {
            set = new HashSet<string>(StringComparer.Ordinal);
            _byPlaylist[playlistId] = set;
        }

        var added = set.Add(channelKey);
        if (!added) set.Remove(channelKey);

        await SaveAsync(ct).ConfigureAwait(false);
        Changed?.Invoke(this, playlistId);
        return added;
    }

    public async Task RemovePlaylistAsync(string playlistId, CancellationToken ct = default)
    {
        if (_byPlaylist.Remove(playlistId))
            await SaveAsync(ct).ConfigureAwait(false);
    }

    private Task SaveAsync(CancellationToken ct)
    {
        var data = _byPlaylist.ToDictionary(kv => kv.Key, kv => kv.Value.ToList(), StringComparer.Ordinal);
        return _store.SaveAsync(FileName, data, ct);
    }
}
