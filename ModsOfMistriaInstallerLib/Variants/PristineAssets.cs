using System.IO.Compression;
using System.Text;

namespace Garethp.ModsOfMistriaInstallerLib.Variants;

/// <summary>
/// Read-only index over the verified pristine game archive: every animation sprite by name,
/// with the paths of its metadata, its PNG strip and its collision shape, plus raw access to
/// fiddle/data files. Opened once per install; never writes.
/// </summary>
public sealed class PristineAssets : IDisposable
{
    public sealed record SpriteEntry(string Name, string MetaPath, string? PngPath, string? ShapePath)
    {
        /// <summary>The meta path relative to <c>assets/</c>, e.g.
        /// <c>animations/Placeables/Furniture/Cottage Garden/spr_x.meta.toml</c>.</summary>
        public string RelativeMetaPath => MetaPath.StartsWith("assets/", StringComparison.Ordinal)
            ? MetaPath["assets/".Length..]
            : MetaPath;

        /// <summary>The folder below <c>assets/animations/</c>, e.g.
        /// <c>Placeables/Furniture/Cottage Garden</c>.</summary>
        public string Folder
        {
            get
            {
                var rel = RelativeMetaPath;
                const string prefix = "animations/";
                if (rel.StartsWith(prefix, StringComparison.Ordinal)) rel = rel[prefix.Length..];
                var slash = rel.LastIndexOf('/');
                return slash < 0 ? "" : rel[..slash];
            }
        }
    }

    private readonly ZipArchive? _zip;
    private readonly Dictionary<string, ZipArchiveEntry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SpriteEntry> _sprites = new(StringComparer.Ordinal);

    private PristineAssets(ZipArchive? zip)
    {
        _zip = zip;
    }

    public static PristineAssets Open(string pristineZipPath)
    {
        var zip = ZipFile.OpenRead(pristineZipPath);
        var assets = new PristineAssets(zip);
        assets.Index(zip.Entries.Select(e => (e.FullName.Replace('\\', '/'), (ZipArchiveEntry?)e)));
        return assets;
    }

    /// <summary>Builds an index from in-memory files (tests).</summary>
    public static PristineAssets FromFiles(IReadOnlyDictionary<string, byte[]> files)
    {
        var assets = new PristineAssets(null);
        assets._memory = new Dictionary<string, byte[]>(files.Select(kv =>
            new KeyValuePair<string, byte[]>(kv.Key.Replace('\\', '/'), kv.Value)), StringComparer.Ordinal);
        assets.Index(assets._memory.Keys.Select(k => (k, (ZipArchiveEntry?)null)));
        return assets;
    }

    private Dictionary<string, byte[]>? _memory;

    private void Index(IEnumerable<(string Name, ZipArchiveEntry? Entry)> names)
    {
        var shapes = new Dictionary<string, string>(StringComparer.Ordinal);
        var pngs = new HashSet<string>(StringComparer.Ordinal);
        var metas = new List<string>();

        foreach (var (name, entry) in names)
        {
            if (entry is not null) _entries[name] = entry;
            if (name.EndsWith('/')) continue;

            if (name.StartsWith("assets/animations/", StringComparison.Ordinal))
            {
                if (name.EndsWith(".meta.toml", StringComparison.Ordinal)) metas.Add(name);
                else if (name.EndsWith(".png", StringComparison.Ordinal)) pngs.Add(name);
            }
            else if (name.StartsWith("assets/shapes/", StringComparison.Ordinal) &&
                     name.EndsWith(".meta.toml", StringComparison.Ordinal))
            {
                var file = name[(name.LastIndexOf('/') + 1)..^".meta.toml".Length];
                if (file.StartsWith("poly_", StringComparison.Ordinal))
                    shapes.TryAdd(file["poly_".Length..], name);
            }
        }

        foreach (var meta in metas)
        {
            var file = meta[(meta.LastIndexOf('/') + 1)..^".meta.toml".Length];
            if (!file.StartsWith("spr_", StringComparison.Ordinal)) continue;
            var png = meta[..^".meta.toml".Length] + ".png";
            shapes.TryGetValue(file["spr_".Length..], out var shape);
            _sprites.TryAdd(file, new SpriteEntry(file, meta, pngs.Contains(png) ? png : null, shape));
        }
    }

    /// <summary>Parsed sprite metadata, read once per install (see <c>VariantPlanner.ReadMeta</c>).</summary>
    internal Dictionary<string, Tomlyn.Model.TomlTable?> MetaCache { get; } = new(StringComparer.Ordinal);

    public bool HasSprite(string name) => _sprites.ContainsKey(name);

    public SpriteEntry? GetSprite(string name) => _sprites.GetValueOrDefault(name);

    public IEnumerable<string> SpriteNames => _sprites.Keys;

    public int SpriteCount => _sprites.Count;

    public bool Exists(string path) => _memory?.ContainsKey(path) ?? _entries.ContainsKey(path);

    public byte[]? ReadBytes(string path)
    {
        if (_memory is not null) return _memory.GetValueOrDefault(path);
        if (!_entries.TryGetValue(path, out var entry)) return null;
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    public string? ReadText(string path)
    {
        var bytes = ReadBytes(path);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n");
    }

    /// <summary>All archive paths below a prefix such as <c>assets/fiddle/items/</c>.</summary>
    public IEnumerable<string> PathsUnder(string prefix)
    {
        var keys = _memory is not null ? (IEnumerable<string>)_memory.Keys : _entries.Keys;
        return keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal) && !k.EndsWith('/'))
            .Order(StringComparer.Ordinal);
    }

    public void Dispose() => _zip?.Dispose();
}
