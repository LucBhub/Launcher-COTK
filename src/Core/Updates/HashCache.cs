using System.Security.Cryptography;
using System.Text.Json;

namespace COTK.Next.Core;

/// <summary>Cache d'empreintes (taille + date de modification → SHA-256). Une vérification de 15 Go
/// qui prenait plusieurs minutes devient quasi instantanée tant que rien n'a changé. « Réparer » l'ignore.</summary>
internal sealed class HashCache
{
    private const string FileName = ".cotk-hash-cache.json";
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _path;
    private bool _dirty;

    private HashCache(string clientDir) => _path = Path.Combine(clientDir, FileName);

    private sealed record Entry(long S, long M, string H);

    private sealed record Document(int V, Dictionary<string, Entry> E);

    public static HashCache Load(string clientDir)
    {
        var cache = new HashCache(clientDir);
        try
        {
            if (File.Exists(cache._path))
            {
                var doc = JsonSerializer.Deserialize<Document>(File.ReadAllText(cache._path));
                if (doc is { V: 1, E: not null })
                    foreach (var (key, value) in doc.E) cache._entries[key] = value;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("cache d'empreintes ignoré", ex);
            cache._entries.Clear();
        }
        return cache;
    }

    public bool TryGet(string relativePath, FileInfo file, out string sha)
    {
        if (_entries.TryGetValue(relativePath, out var e)
            && e.S == file.Length
            && e.M == file.LastWriteTimeUtc.Ticks)
        {
            sha = e.H;
            return true;
        }
        sha = "";
        return false;
    }

    public void Set(string relativePath, FileInfo file, string sha)
    {
        _entries[relativePath] = new Entry(file.Length, file.LastWriteTimeUtc.Ticks, sha.ToLowerInvariant());
        _dirty = true;
    }

    public void Forget(string relativePath)
    {
        if (_entries.TryRemove(relativePath, out _)) _dirty = true;
    }

    public void Save()
    {
        if (!_dirty) return;
        try
        {
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new Document(1, _entries.ToDictionary(kv => kv.Key, kv => kv.Value))));
            File.Move(temp, _path, overwrite: true);
            _dirty = false;
        }
        catch (Exception ex)
        {
            Log.Warn("cache d'empreintes non enregistré", ex);
        }
    }
}

internal static class Hashing
{
    /// <summary>SHA-256 en flux (jamais le fichier entier en mémoire), annulable, avec progression en octets.</summary>
    public static async Task<string> Sha256Async(string path, CancellationToken ct = default, Action<long>? onBytes = null)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1 << 20];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            onBytes?.Invoke(read);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
