using System.IO.Compression;

namespace Garethp.ModsOfMistriaInstallerLib.PortraitWardrobe;

/// <summary>The game's original portraits, as shipped (before any mod touched them).</summary>
public interface IPortraitVanillaSource
{
    /// <summary>The original .meta.toml text, or null when the game has no such portrait.</summary>
    string? Meta(string spriteName);

    /// <summary>The original PNG strip, or null.</summary>
    byte[]? Png(string spriteName);
}

/// <summary>
/// Reads originals from AIM's pristine backup (<c>assets.bak.zip</c>), which is always the game as
/// installed by Steam - never a modded archive - so "original art" really is the original.
/// Only portrait metas are indexed, so opening it is cheap.
/// </summary>
public sealed class ZipPortraitVanillaSource : IPortraitVanillaSource, IDisposable
{
    private readonly ZipArchive _archive;
    private readonly Dictionary<string, string> _metaEntries = new(StringComparer.OrdinalIgnoreCase);

    public ZipPortraitVanillaSource(string zipPath)
    {
        _archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in _archive.Entries)
        {
            var name = entry.FullName;
            if (!name.StartsWith("assets/animations/", StringComparison.OrdinalIgnoreCase)) continue;
            if (!name.EndsWith(".meta.toml", StringComparison.OrdinalIgnoreCase)) continue;
            var sprite = Path.GetFileName(name)[..^".meta.toml".Length];
            if (!PortraitSpriteNames.IsPortrait(sprite)) continue;
            _metaEntries.TryAdd(sprite, name);
        }
    }

    public string? Meta(string spriteName)
    {
        if (!_metaEntries.TryGetValue(spriteName, out var entry)) return null;
        var bytes = Read(entry);
        return bytes is null ? null : System.Text.Encoding.UTF8.GetString(bytes);
    }

    public byte[]? Png(string spriteName)
    {
        if (!_metaEntries.TryGetValue(spriteName, out var entry)) return null;
        return Read(entry[..^".meta.toml".Length] + ".png");
    }

    private byte[]? Read(string entryName)
    {
        var entry = _archive.GetEntry(entryName);
        if (entry is null) return null;
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    public void Dispose() => _archive.Dispose();
}

/// <summary>An in-memory stand-in, for tests.</summary>
public sealed class DictionaryPortraitVanillaSource(Dictionary<string, (string Meta, byte[] Png)> portraits) : IPortraitVanillaSource
{
    public string? Meta(string spriteName) => portraits.TryGetValue(spriteName, out var p) ? p.Meta : null;

    public byte[]? Png(string spriteName) => portraits.TryGetValue(spriteName, out var p) ? p.Png : null;
}
