using System.Diagnostics;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;

namespace Garethp.ModsOfMistriaInstallerLib.Variants;

/// <summary>Everything one install needs from the Variants Framework.</summary>
public sealed class VariantSession : IDisposable
{
    public required VariantPlan Plan { get; init; }

    /// <summary>
    /// True when the framework is not part of this install (not selected, or not installed after
    /// all): no looks, no registry and no new variants, only retired stand-ins that keep every id
    /// an earlier install generated loadable in existing saves.
    /// </summary>
    public bool RetireOnly { get; private set; }

    /// <summary>Kept open for the install: original-art copies are read from it lazily, while
    /// the atlases are packed.</summary>
    public PristineAssets? Pristine { get; init; }
    public required VariantHistory History { get; init; }
    public string? ModsFolder { get; init; }
    public List<ItemVariant> ItemVariants { get; private set; } = [];
    public List<AnimalVariant> AnimalVariants { get; private set; } = [];
    public List<ItemVariant> RetiredItems { get; private set; } = [];
    public List<AnimalVariant> RetiredAnimals { get; private set; } = [];
    public List<ItemVariant> WrittenItemVariants { get; } = [];
    public List<AnimalVariant> WrittenAnimalVariants { get; } = [];

    private readonly Dictionary<IMod, IMod> _effective = new(ReferenceEqualityComparer.Instance);

    /// <summary>The view of a mod the installers should read (an overlay when the framework
    /// rewrote it, the mod itself otherwise).</summary>
    public IMod Effective(IMod mod) => _effective.GetValueOrDefault(mod, mod);

    /// <summary>The selected mod behind an overlay.</summary>
    public static IMod Original(IMod mod) => mod is VariantOverlayMod overlay ? overlay.Inner : mod;

    internal void SetEffective(IMod original, IMod effective) => _effective[original] = effective;

    public void Dispose() => Pristine?.Dispose();

    internal static VariantSession Create(VariantPlan plan, PristineAssets? pristine, VariantHistory history,
        string? modsFolder, List<ItemVariant> items, List<AnimalVariant> animals, bool retireOnly)
    {
        var (retiredItems, retiredAnimals) = history.Retired(items, animals);
        return new VariantSession
        {
            Plan = plan,
            Pristine = pristine,
            History = history,
            ModsFolder = modsFolder,
            RetireOnly = retireOnly,
            ItemVariants = items,
            AnimalVariants = animals,
            RetiredItems = retiredItems,
            RetiredAnimals = retiredAnimals,
        };
    }

    /// <summary>
    /// Drops this install's new variants and keeps only retired stand-ins for everything the
    /// history knows. For a planned session whose framework mod did not make it into the install
    /// (skipped for a GML problem): the variants it planned have no runtime to serve them, but
    /// saves still hold the ids earlier installs generated.
    /// </summary>
    public void RetireEverything()
    {
        RetireOnly = true;
        ItemVariants = [];
        AnimalVariants = [];
        (RetiredItems, RetiredAnimals) = History.Retired([], []);
    }

    /// <summary>Records what this install generated so later installs keep it loadable.</summary>
    public void Commit()
    {
        // A retire-only install generates nothing new, so the history is left exactly as it was:
        // rewriting it could only lose entries (an unreadable file would come back empty).
        if (RetireOnly) return;
        History.Remember(WrittenItemVariants.Where(v => !v.Retired), WrittenAnimalVariants.Where(v => !v.Retired));
        History.Save(ModsFolder);
    }
}

/// <summary>
/// Entry point used by <see cref="ModInstaller"/>. With the Variants Framework mod selected it
/// plans looks and variants. Without it, it returns a retire-only session when an earlier install
/// generated variant ids (so saves that hold them still load), and null otherwise, in which case
/// the install proceeds exactly as it always has.
/// </summary>
public static class VariantCompiler
{
    public static VariantSession? Prepare(
        IReadOnlyList<IMod> mods,
        string pristineZipPath,
        string? modsFolder,
        Action<string, string> reportStatus,
        VariantPreferences? preferences = null,
        string? installerVersion = null)
    {
        var framework = mods.FirstOrDefault(VariantFramework.IsFrameworkMod);
        if (framework is null) return PrepareRetireOnly(modsFolder, reportStatus);
        if (!File.Exists(pristineZipPath))
        {
            Logger.Log("[variants] The pristine backup is missing; looks are disabled for this install.");
            return PrepareRetireOnly(modsFolder, reportStatus);
        }

        var timer = Stopwatch.StartNew();
        reportStatus("Variants Framework: finding mods that change the same art", "");

        var pristine = PristineAssets.Open(pristineZipPath);
        VariantPlan plan;
        List<ItemVariant> items;
        List<AnimalVariant> animals;
        try
        {
            var index = FamilyIndex.Build(pristine);
            preferences ??= VariantPreferences.Load();
            plan = VariantPlanner.Plan(mods, framework, pristine, index, preferences);
            (items, animals) = VariantContentPlanner.Plan(plan, pristine);
        }
        catch
        {
            pristine.Dispose();
            throw;
        }

        var history = VariantHistory.Load(modsFolder);
        var session = VariantSession.Create(plan, pristine, history, modsFolder, items, animals, retireOnly: false);
        var (retiredItems, retiredAnimals) = (session.RetiredItems, session.RetiredAnimals);

        // The registry is plain GML data the framework's runtime reads; it replaces the stub the
        // framework ships, so it rides the normal GML staging and compile check.
        var registry = VariantRegistryWriter.Render(session,
            installerVersion ?? typeof(VariantCompiler).Assembly.GetName().Version?.ToString() ?? "AIM",
            DateTimeOffset.UtcNow);
        plan.RewriteFor(framework.GetId()).Add(new VirtualFile { Path = VariantFramework.RegistryGmlPath, Text = registry });

        foreach (var mod in mods)
        {
            if (!plan.Rewrites.TryGetValue(mod.GetId(), out var rewrite) || rewrite.IsEmpty) continue;
            session.SetEffective(mod, new VariantOverlayMod(mod, rewrite));
        }

        var looks = plan.RuntimeFamilies.Sum(f => f.Looks.Count);
        Logger.Log($"[variants] {plan.RuntimeFamilies.Count} switchable families ({looks} looks), " +
                   $"{plan.InstallOnlyFamilies.Count} install-time choices, {plan.LookSpriteCount} look sprites, " +
                   $"{plan.VanillaCopyCount} original copies, {items.Count} variant items, {animals.Count} animal variants, " +
                   $"{retiredItems.Count + retiredAnimals.Count} retired, planned in {timer.Elapsed.TotalSeconds:0.0}s");
        foreach (var warning in plan.Warnings.Take(200)) Logger.Log("[variants] " + warning);
        reportStatus($"Variants Framework: {plan.RuntimeFamilies.Count} switchable, {plan.InstallOnlyFamilies.Count} install-time choices", "");
        return session;
    }

    /// <summary>
    /// The session for an install without the framework: every variant id an earlier install
    /// generated becomes a retired stand-in (the item keeps its id and the original's art and is
    /// sold nowhere; the animal variant keeps its key and cannot be adopted). Placed variant
    /// furniture, its chest contents and barn animals of a variant therefore survive the
    /// framework's removal. No registry, no overlays, no look sprites. Null when nothing was ever
    /// generated, which leaves the install untouched.
    /// </summary>
    public static VariantSession? PrepareRetireOnly(string? modsFolder, Action<string, string> reportStatus)
    {
        var history = VariantHistory.Load(modsFolder);
        if (history.Items.Count == 0 && history.Animals.Count == 0) return null;

        var session = VariantSession.Create(new VariantPlan(), null, history, modsFolder, [], [], retireOnly: true);
        Logger.Log($"[variants] The Variants Framework is not part of this install; keeping {session.RetiredItems.Count} " +
                   $"variant item(s) and {session.RetiredAnimals.Count} animal variant(s) from earlier installs loadable.");
        reportStatus("Variants Framework: keeping earlier variant items loadable", "");
        return session;
    }
}
