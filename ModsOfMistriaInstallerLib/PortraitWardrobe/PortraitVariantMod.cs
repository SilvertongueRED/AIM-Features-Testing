using Garethp.ModsOfMistriaInstallerLib.Generator;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;

namespace Garethp.ModsOfMistriaInstallerLib.PortraitWardrobe;

/// <summary>
/// The portrait library as a mod that exists only in memory: a <c>.meta.toml</c> and a PNG per kept
/// portrait under <c>animations/PortraitWardrobe/</c>. Handing it to the normal installers packs
/// the PNGs into the right atlases and writes the metas exactly as for any mod, so the library
/// sprites are ordinary game sprites with nothing special about how they load.
/// </summary>
public sealed class PortraitVariantMod : IMod
{
    public const string Id = "aim.portrait_wardrobe_library";

    private readonly Dictionary<string, string> _text = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<byte[]>> _binary = new(StringComparer.OrdinalIgnoreCase);
    private readonly Validation _validation = new();

    public int SpriteCount { get; private set; }

    public void AddSprite(string folder, string spriteName, string metaToml, Func<byte[]> png)
    {
        var dir = $"animations/PortraitWardrobe/{folder}";
        _text[$"{dir}/{spriteName}.meta.toml"] = metaToml;
        _binary[$"{dir}/{spriteName}.png"] = png;
        SpriteCount++;
    }

    private IEnumerable<string> AllPaths() => _text.Keys.Concat(_binary.Keys);

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');

    public string GetAuthor() => "AIM";
    public string GetName() => "Portrait Wardrobe library";
    public string GetVersion() => "1";
    public string GetLocation() => "";
    public string GetSourcePath() => "";
    public string GetMinimumInstallerVersion() => "0.0.0";
    public string GetManifestVersion() => "1";
    public Validation GetValidation() => _validation;
    public string GetId() => Id;
    public bool IsInstalled() => true;
    public void SetInstalled(bool installed) { }
    public Validation Validate() => _validation;
    public string GetBasePath() => "";
    public List<ModRequirement> GetRequirements() => [];
    public List<string> GetRequiredHooks() => [];
    public string? GetUpdateUrl() => null;
    public string? GetDownloadUrl() => null;

    public bool HasFilesInFolder(string folder, string extension) => GetFilesInFolder(folder, extension).Count > 0;

    public bool HasFilesInFolder(string folder) => HasFilesInFolder(folder, "");

    // Direct children only, like a folder mod.
    public List<string> GetFilesInFolder(string folder, string extension)
    {
        var prefix = Normalize(folder).TrimEnd('/') + "/";
        return AllPaths()
            .Where(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        && !p[prefix.Length..].Contains('/')
                        && (string.IsNullOrEmpty(extension) || p.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    public List<string> GetFilesInFolder(string folder) => GetFilesInFolder(folder, "");

    public List<string> GetAllFiles(string extension) =>
        AllPaths().Where(p => p.EndsWith(extension, StringComparison.OrdinalIgnoreCase)).ToList();

    public bool FileExists(string path)
    {
        var p = Normalize(path);
        return _text.ContainsKey(p) || _binary.ContainsKey(p);
    }

    public bool FolderExists(string path)
    {
        var prefix = Normalize(path).TrimEnd('/') + "/";
        return AllPaths().Any(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    public string ReadFile(string path)
    {
        var p = Normalize(path);
        if (_text.TryGetValue(p, out var text)) return text;
        if (_binary.TryGetValue(p, out var bytes)) return System.Text.Encoding.UTF8.GetString(bytes());
        throw new FileNotFoundException(path);
    }

    public Stream ReadFileAsStream(string path)
    {
        var p = Normalize(path);
        if (_binary.TryGetValue(p, out var bytes)) return new MemoryStream(bytes(), writable: false);
        if (_text.TryGetValue(p, out var text)) return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text), writable: false);
        throw new FileNotFoundException(path);
    }
}
