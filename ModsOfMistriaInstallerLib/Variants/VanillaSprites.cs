using System.IO.Compression;

namespace Garethp.ModsOfMistriaInstallerLib.Variants;

/// <summary>
/// The names of the game's own sprites, for checks that need to know whether a mod's image
/// replaces the game's art or adds new art (the conflict report, when the Variants Framework is
/// selected). Read from the archive's central directory only, never its contents, and cached per
/// archive version, so asking again on every selection change costs nothing.
/// </summary>
public static class VanillaSprites
{
    private static readonly object Gate = new();
    private static CacheEntry? _cache;

    private sealed record CacheEntry(string Path, DateTime Stamp, long Length, HashSet<string> Names);

    /// <summary>
    /// For a game folder: the verified pristine backup when AIM has made one, else the live
    /// archive, which is the game's own until AIM first installs. Null when neither can be read.
    /// </summary>
    public static IReadOnlySet<string>? ForGame(string? fomLocation)
    {
        if (string.IsNullOrWhiteSpace(fomLocation)) return null;
        foreach (var candidate in new[] { "assets.bak.zip", "assets.zip" })
        {
            var path = Path.Combine(fomLocation, candidate);
            if (File.Exists(path)) return FromArchive(path);
        }

        return null;
    }

    /// <summary>
    /// A predicate over <see cref="ForGame"/> that reads the archive only on its first call, so a
    /// caller can hand it to a check that may never need it. Null-safe: an unreadable archive
    /// answers <c>true</c> (treat as the game's) rather than hiding the framework's handling.
    /// </summary>
    public static Func<string, bool> LazyPredicate(string? fomLocation)
    {
        var names = new Lazy<IReadOnlySet<string>?>(() => ForGame(fomLocation));
        return sprite => names.Value?.Contains(sprite) ?? true;
    }

    public static IReadOnlySet<string>? FromArchive(string zipPath)
    {
        try
        {
            var info = new FileInfo(zipPath);
            lock (Gate)
            {
                if (_cache is { } cached && cached.Path == info.FullName &&
                    cached.Stamp == info.LastWriteTimeUtc && cached.Length == info.Length)
                    return cached.Names;
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in zip.Entries)
                {
                    // The same rule PristineAssets indexes sprites by: a spr_ metadata file under
                    // assets/animations/.
                    var name = entry.FullName.Replace('\\', '/');
                    if (!name.StartsWith("assets/animations/", StringComparison.Ordinal) ||
                        !name.EndsWith(".meta.toml", StringComparison.Ordinal)) continue;
                    var file = name[(name.LastIndexOf('/') + 1)..^".meta.toml".Length];
                    if (file.StartsWith("spr_", StringComparison.Ordinal)) names.Add(file);
                }
            }

            lock (Gate)
            {
                _cache = new CacheEntry(info.FullName, info.LastWriteTimeUtc, info.Length, names);
            }

            return names;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Logger.Log($"[variants] Could not read the game's sprite list from '{zipPath}': {exception.Message}");
            return null;
        }
    }
}
