using Garethp.ModsOfMistriaInstallerLib.ModTypes;

namespace Garethp.ModsOfMistriaInstallerLib.Variants;

/// <summary>One selectable appearance of a family: the game's art or one mod's version.</summary>
public sealed class LookInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string ModId { get; init; } = "";
    public string Author { get; init; } = "";
    public bool IsVanilla => Id == VariantFramework.VanillaLook;
}

/// <summary>How one family appears under one look.</summary>
public sealed class FamilyLook
{
    public required LookInfo Look { get; init; }

    /// <summary>Vanilla sprite name to the sprite that shows it under this look. Sprites that
    /// are absent show themselves.</summary>
    public SortedDictionary<string, string> Map { get; } = new(StringComparer.Ordinal);

    /// <summary>Vanilla sprites this look really changes (for previews and variant items).</summary>
    public HashSet<string> Changed { get; } = new(StringComparer.Ordinal);

    /// <summary>The vanilla sprite to preview this look with.</summary>
    public string? PreviewSprite { get; set; }

    /// <summary>Install-only families: the generated thumbnail sprite.</summary>
    public string? Thumbnail { get; set; }

    public string Resolve(string sprite) => Map.GetValueOrDefault(sprite, sprite);
}

public sealed class PlannedFamily
{
    public required Family Family { get; init; }
    public List<FamilyLook> Looks { get; } = [];
    public required string InstallDefault { get; set; }

    public FamilyLook? LookById(string id) => Looks.FirstOrDefault(l => l.Look.Id == id);
}

/// <summary>A sprite file the overlay adds to a mod.</summary>
public sealed class VirtualFile
{
    public required string Path { get; init; }
    public string? Text { get; init; }
    public Func<byte[]>? Bytes { get; init; }
}

/// <summary>The rewrite applied to one mod's files.</summary>
public sealed class ModRewrite
{
    public HashSet<string> Hidden { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, VirtualFile> Added { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsEmpty => Hidden.Count == 0 && Added.Count == 0;

    public void Add(VirtualFile file) => Added[file.Path] = file;
}

public sealed class VariantPlan
{
    /// <summary>The framework mod; null for a retire-only session, which plans nothing.</summary>
    public IMod? Framework { get; init; }
    public List<PlannedFamily> RuntimeFamilies { get; } = [];
    public List<PlannedFamily> InstallOnlyFamilies { get; } = [];
    public Dictionary<string, LookInfo> Looks { get; } = new(StringComparer.Ordinal);

    /// <summary>Rewrites keyed by mod id; the framework's own generated files sit under its id.</summary>
    public Dictionary<string, ModRewrite> Rewrites { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>New look sprite to the shadow it should cast.</summary>
    public SortedDictionary<string, string> ShadowLinks { get; } = new(StringComparer.Ordinal);

    /// <summary>New look icon to its hover outline.</summary>
    public SortedDictionary<string, string> OutlineLinks { get; } = new(StringComparer.Ordinal);

    public List<string> Warnings { get; } = [];

    public VariantPreferences Preferences { get; init; } = new();

    public IReadOnlyDictionary<string, List<string>> AnimalAnimations { get; init; } =
        new Dictionary<string, List<string>>();

    public int LookSpriteCount { get; set; }
    public int VanillaCopyCount { get; set; }

    public FamilyLook? FindLook(string familyId, string lookId) =>
        RuntimeFamilies.FirstOrDefault(f => f.Family.Id == familyId)?.LookById(lookId);

    public ModRewrite RewriteFor(string modId)
    {
        if (!Rewrites.TryGetValue(modId, out var rewrite))
        {
            rewrite = new ModRewrite();
            Rewrites[modId] = rewrite;
        }

        return rewrite;
    }
}
