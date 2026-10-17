using System.Text;
using Garethp.ModsOfMistriaInstallerLib.Generator;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;

namespace Garethp.ModsOfMistriaInstallerLib.Variants;

/// <summary>
/// A read-only view of a mod with some files hidden and some generated files added. The mod
/// on disk is never changed: the installers simply read this view instead, so a competing
/// replacement is installed under its look's own sprite name rather than over the shared one.
/// </summary>
public sealed class VariantOverlayMod(IMod inner, ModRewrite rewrite) : IMod
{
    public IMod Inner { get; } = inner;

    private readonly Dictionary<string, VirtualFile> _added =
        new(rewrite.Added, StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _hidden = new(rewrite.Hidden, StringComparer.OrdinalIgnoreCase);

    // ── file access ─────────────────────────────────────────────────────────

    public List<string> GetAllFiles(string extension)
    {
        var basePath = NormalizedBase();
        var result = Inner.GetAllFiles(extension)
            .Where(path => !_hidden.Contains(ToRelative(path)))
            .Where(path => !_added.ContainsKey(ToRelative(path)))
            .ToList();
        foreach (var rel in _added.Keys.Order(StringComparer.Ordinal))
        {
            if (!rel.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) continue;
            result.Add(string.IsNullOrEmpty(basePath) ? rel : $"{basePath}/{rel}");
        }

        return result;
    }

    public bool FileExists(string path)
    {
        var rel = ToRelative(path);
        if (_added.ContainsKey(rel)) return true;
        if (_hidden.Contains(rel)) return false;
        return Inner.FileExists(path);
    }

    public bool FolderExists(string path) =>
        Inner.FolderExists(path) || _added.Keys.Any(k => k.StartsWith(Folder(path), StringComparison.OrdinalIgnoreCase));

    public string ReadFile(string path)
    {
        var rel = ToRelative(path);
        if (_added.TryGetValue(rel, out var file))
            return file.Text ?? Encoding.UTF8.GetString(file.Bytes?.Invoke() ?? []);
        if (_hidden.Contains(rel)) return "";
        return Inner.ReadFile(path);
    }

    public Stream ReadFileAsStream(string path)
    {
        var rel = ToRelative(path);
        if (_added.TryGetValue(rel, out var file))
            return new MemoryStream(file.Bytes?.Invoke() ?? Encoding.UTF8.GetBytes(file.Text ?? ""), writable: false);
        if (_hidden.Contains(rel)) throw new FileNotFoundException("Hidden by the Variants Framework.", path);
        return Inner.ReadFileAsStream(path);
    }

    public bool HasFilesInFolder(string folder, string extension) =>
        GetFilesInFolder(folder, extension).Count > 0;

    public bool HasFilesInFolder(string folder) => HasFilesInFolder(folder, "");

    public List<string> GetFilesInFolder(string folder, string extension)
    {
        var prefix = Folder(folder);
        var basePath = NormalizedBase();
        // An added file that shadows one of the mod's own (the framework's generated registry
        // replacing its shipped stub) is listed once, as the added file, like GetAllFiles does.
        var result = Inner.HasFilesInFolder(folder, extension)
            ? Inner.GetFilesInFolder(folder, extension)
                .Where(path => !_hidden.Contains(Relative(path)) && !_added.ContainsKey(Relative(path)))
                .ToList()
            : [];
        foreach (var rel in _added.Keys.Order(StringComparer.Ordinal))
        {
            if (!rel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (rel[prefix.Length..].Contains('/')) continue; // non-recursive, like the containers
            if (!string.IsNullOrEmpty(extension) && !rel.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) continue;
            result.Add(string.IsNullOrEmpty(basePath) ? rel : $"{basePath}/{rel}");
        }

        return result;
    }

    public List<string> GetFilesInFolder(string folder) => GetFilesInFolder(folder, "");

    // ── metadata: the overlay is the same mod ───────────────────────────────

    public string GetAuthor() => Inner.GetAuthor();
    public string GetName() => Inner.GetName();
    public string GetDisplayName(string? languageCode) => Inner.GetDisplayName(languageCode);
    public string GetDisplayDescription(string? languageCode) => Inner.GetDisplayDescription(languageCode);
    public string GetVersion() => Inner.GetVersion();
    public string GetLocation() => Inner.GetLocation();
    public string GetSourcePath() => Inner.GetSourcePath();
    public string GetMinimumInstallerVersion() => Inner.GetMinimumInstallerVersion();
    public string GetManifestVersion() => Inner.GetManifestVersion();
    public Validation GetValidation() => Inner.GetValidation();
    public string GetId() => Inner.GetId();
    public bool IsInstalled() => Inner.IsInstalled();
    public void SetInstalled(bool installed) => Inner.SetInstalled(installed);
    public Validation Validate() => Inner.Validate();
    public string GetBasePath() => Inner.GetBasePath();
    public List<ModRequirement> GetRequirements() => Inner.GetRequirements();
    public List<string> GetRequiredHooks() => Inner.GetRequiredHooks();
    public string? GetUpdateUrl() => Inner.GetUpdateUrl();
    public string? GetDownloadUrl() => Inner.GetDownloadUrl();

    // ── paths ───────────────────────────────────────────────────────────────

    // Inner listings may be absolute (folders) or prefixed (archives); both normalise to the
    // mod-relative form the rewrite is keyed by.
    private string Relative(string path) => OutfitGenerator.Relativize(Inner, path).Replace('\\', '/');

    private string ToRelative(string path)
    {
        var normalized = path.Replace('\\', '/');
        var prefix = NormalizedBase();
        if (!string.IsNullOrEmpty(prefix) &&
            normalized.StartsWith(prefix + '/', StringComparison.OrdinalIgnoreCase))
            return normalized[(prefix.Length + 1)..];
        return Relative(normalized);
    }

    private static string Folder(string folder) => folder.Replace('\\', '/').TrimEnd('/') + '/';

    private string NormalizedBase() => Inner.GetBasePath().Replace('\\', '/').TrimEnd('/');
}
