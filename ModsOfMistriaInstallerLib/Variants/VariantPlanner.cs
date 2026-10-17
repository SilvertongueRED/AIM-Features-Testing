using Garethp.ModsOfMistriaInstallerLib.ModTypes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Tomlyn.Model;
using Toml = Garethp.ModsOfMistriaInstallerLib.Utils.Toml;

namespace Garethp.ModsOfMistriaInstallerLib.Variants;

/// <summary>
/// Decides, before anything is written, how the selected mods' competing sprite replacements
/// become looks:
/// <list type="bullet">
/// <item>Runtime families (furniture, animals, pets, buildings, world objects, item icons): every
/// competing mod keeps its art under its own look sprite name, the game's art is kept as the
/// "Original" look, and the base sprite holds the default look (the player's choice, else the
/// last mod in load order, exactly as before the framework existed).</item>
/// <item>Install-only sprites (portraits, interface, scenery) that two or more mods replace:
/// one version is installed (the player's choice, else load order) and a thumbnail of each
/// alternative is packed so the in-game catalog can offer the choice for the next install.</item>
/// </list>
/// A replacement the planner cannot use safely (odd strip width, atlas-less art, a LUT palette)
/// is left exactly as the installer would have handled it without the framework.
/// </summary>
public static class VariantPlanner
{
    private const int ThumbnailSize = 48;

    public static VariantPlan Plan(
        IReadOnlyList<IMod> mods,
        IMod framework,
        PristineAssets pristine,
        FamilyIndex index,
        VariantPreferences preferences)
    {
        var plan = new VariantPlan
        {
            Framework = framework,
            Preferences = preferences,
            AnimalAnimations = index.AnimalAnimations,
        };

        var contentMods = mods.Where(m => !ReferenceEquals(m, framework) && !VariantFramework.IsFrameworkMod(m)).ToList();
        var lookByMod = AssignLooks(contentMods, plan);

        // Portrait Wardrobe owns villager portraits when it is installed: it keeps every pack's
        // portraits side by side itself and reads each mod's original files, so the framework
        // must not also hide or rename them.
        var portraitsOwnedElsewhere = PortraitWardrobe.PortraitWardrobeBuilder.IsFrameworkInstalled(mods);

        // sprite -> replacements in load order
        var replacers = new Dictionary<string, List<(IMod Mod, Replacement Replacement)>>(StringComparer.Ordinal);
        foreach (var mod in contentMods)
        {
            Dictionary<string, Replacement> found;
            try
            {
                found = ReplacementScanner.Scan(mod, pristine);
            }
            catch (Exception exception)
            {
                plan.Warnings.Add($"Could not read {mod.GetName()}'s images: {exception.Message}");
                continue;
            }

            foreach (var (sprite, replacement) in found)
            {
                if (portraitsOwnedElsewhere && VariantFramework.IsPortraitSprite(sprite)) continue;
                if (!replacers.TryGetValue(sprite, out var list)) replacers[sprite] = list = [];
                list.Add((mod, replacement));
            }
        }

        var lookable = new Dictionary<string, bool>(StringComparer.Ordinal);
        bool IsLookable(string sprite)
        {
            if (lookable.TryGetValue(sprite, out var known)) return known;
            var entry = pristine.GetSprite(sprite);
            var ok = entry is not null && ReadMeta(pristine, entry) is { } meta && HasAtlas(meta);
            lookable[sprite] = ok;
            return ok;
        }

        var handled = new HashSet<string>(StringComparer.Ordinal);
        var context = new PlanContext(plan, pristine, index, lookByMod);

        // ── runtime families ───────────────────────────────────────────────
        var touchedFamilies = replacers.Keys
            .Select(index.FamilyOf)
            .Where(f => f is not null && f.IsRuntime)
            .Distinct()
            .OrderBy(f => f!.Id, StringComparer.Ordinal)
            .Cast<Family>();

        foreach (var family in touchedFamilies)
        {
            var sprites = family.Sprites.Where(s => replacers.ContainsKey(s) && IsLookable(s)).ToList();
            if (sprites.Count == 0) continue;

            var touching = contentMods.Where(m => sprites.Any(s => replacers[s].Any(r => ReferenceEquals(r.Mod, m)))).ToList();
            if (touching.Count == 0) continue;
            if (touching.Count == 1 && !preferences.KeepOriginalLooks) continue;

            var planned = PlanRuntimeFamily(family, sprites, touching, replacers, context);
            if (planned is null) continue;
            plan.RuntimeFamilies.Add(planned);
            foreach (var sprite in sprites) handled.Add(sprite);
        }

        // ── install-only overlaps ──────────────────────────────────────────
        var groups = replacers
            .Where(kv => !handled.Contains(kv.Key) && kv.Value.Select(r => r.Mod).Distinct().Count() > 1)
            .GroupBy(kv => GroupKey(kv.Key, kv.Value, pristine), StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (var group in groups)
        {
            var planned = PlanInstallOnly(group.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
                contentMods, context);
            if (planned is not null) plan.InstallOnlyFamilies.Add(planned);
        }

        return plan;
    }

    private sealed record PlanContext(
        VariantPlan Plan,
        PristineAssets Pristine,
        FamilyIndex Index,
        Dictionary<IMod, LookInfo> LookByMod);

    private static Dictionary<IMod, LookInfo> AssignLooks(List<IMod> mods, VariantPlan plan)
    {
        var looks = new Dictionary<IMod, LookInfo>(ReferenceEqualityComparer.Instance);
        var vanilla = new LookInfo { Id = VariantFramework.VanillaLook, Name = "Original" };
        plan.Looks[vanilla.Id] = vanilla;

        foreach (var mod in mods)
        {
            var id = VariantFramework.LookIdForMod(mod.GetId());
            var suffix = 0;
            while (plan.Looks.TryGetValue(id, out var taken) && !string.Equals(taken.ModId, mod.GetId(), StringComparison.OrdinalIgnoreCase))
                id = VariantFramework.LookIdForMod(mod.GetId()) + "x" + (char)('a' + suffix++ % 26);

            var look = new LookInfo
            {
                Id = id,
                Name = string.IsNullOrWhiteSpace(mod.GetName()) ? mod.GetId() : mod.GetName(),
                ModId = mod.GetId(),
                Author = mod.GetAuthor(),
            };
            plan.Looks[id] = look;
            looks[mod] = look;
        }

        return looks;
    }

    // ── runtime family ───────────────────────────────────────────────────────

    private static PlannedFamily? PlanRuntimeFamily(
        Family family,
        List<string> sprites,
        List<IMod> touching,
        Dictionary<string, List<(IMod Mod, Replacement Replacement)>> replacers,
        PlanContext context)
    {
        var plan = context.Plan;
        var preference = plan.Preferences.GlobalLooks.GetValueOrDefault(family.Id);
        var touchingLooks = touching.Select(m => context.LookByMod[m]).ToList();
        var defaultLook = preference == VariantFramework.VanillaLook
            ? plan.Looks[VariantFramework.VanillaLook]
            : touchingLooks.FirstOrDefault(l => l.Id == preference) ?? touchingLooks[^1];
        var defaultMod = touching.FirstOrDefault(m => context.LookByMod[m] == defaultLook);

        var planned = new PlannedFamily { Family = family, InstallDefault = defaultLook.Id };
        var vanillaLook = new FamilyLook { Look = plan.Looks[VariantFramework.VanillaLook] };
        planned.Looks.Add(vanillaLook);
        var modLooks = new Dictionary<IMod, FamilyLook>(ReferenceEqualityComparer.Instance);
        foreach (var mod in touching)
        {
            modLooks[mod] = new FamilyLook { Look = context.LookByMod[mod] };
            planned.Looks.Add(modLooks[mod]);
        }

        // Which mods successfully produced a look sprite for which vanilla sprite.
        var produced = new Dictionary<string, HashSet<IMod>>(StringComparer.Ordinal);
        var defaultReplaces = new HashSet<string>(StringComparer.Ordinal);

        foreach (var sprite in sprites)
        {
            var entry = context.Pristine.GetSprite(sprite)!;
            produced[sprite] = new HashSet<IMod>(ReferenceEqualityComparer.Instance);
            foreach (var (mod, replacement) in replacers[sprite])
            {
                if (!touching.Contains(mod)) continue;
                if (ReferenceEquals(mod, defaultMod))
                {
                    defaultReplaces.Add(sprite);
                    continue;
                }

                var lookId = context.LookByMod[mod].Id;
                var lookSprite = LookSpriteName(family, sprite, lookId, plan);
                if (TryAddLookSprite(context, mod, replacement, entry, lookSprite))
                {
                    produced[sprite].Add(mod);
                    HideReplacement(plan.RewriteFor(mod.GetId()), replacement);
                }
                else
                {
                    // The art cannot be packed as its own sprite; leave the mod's file alone so
                    // the install behaves exactly as it would without the framework.
                    plan.Warnings.Add($"{mod.GetName()}: '{sprite}' kept as a plain replacement (its image does not fit the game's frame layout).");
                }
            }
        }

        // A mod's art that still replaces the base sprite (fallback above) means the base no
        // longer holds the default look's image for that sprite; treat it as the default's.
        foreach (var sprite in sprites)
        {
            foreach (var (mod, _) in replacers[sprite])
                if (touching.Contains(mod) && !ReferenceEquals(mod, defaultMod) && !produced[sprite].Contains(mod))
                    defaultReplaces.Add(sprite);
        }

        // Keep the game's art for every sprite whose base now holds a mod's image.
        foreach (var sprite in defaultReplaces)
        {
            var entry = context.Pristine.GetSprite(sprite)!;
            var vanillaSprite = LookSpriteName(family, sprite, VariantFramework.VanillaLook, plan);
            if (TryAddVanillaCopy(context, entry, vanillaSprite))
                vanillaLook.Map[sprite] = vanillaSprite;
        }

        // An "Original" look that lacks a copy of some sprite would show the default mod's art
        // under the game's name (the base sprite holds that art now), so it is not offered.
        var originalIsComplete = defaultLook.IsVanilla || defaultReplaces.All(vanillaLook.Map.ContainsKey);

        foreach (var mod in touching)
        {
            var look = modLooks[mod];
            foreach (var sprite in sprites)
            {
                if (ReferenceEquals(mod, defaultMod))
                {
                    if (defaultReplaces.Contains(sprite) && replacers[sprite].Any(r => ReferenceEquals(r.Mod, mod)))
                        look.Changed.Add(sprite);
                    continue;
                }

                if (produced[sprite].Contains(mod))
                {
                    look.Map[sprite] = LookSpriteName(family, sprite, look.Look.Id, plan);
                    look.Changed.Add(sprite);
                }
                else if (vanillaLook.Map.TryGetValue(sprite, out var original))
                {
                    look.Map[sprite] = original;
                }
            }
        }

        // The default look shows the base sprites; the Original look and other mods' looks
        // only differ where something was actually installed.
        if (defaultLook.IsVanilla)
            vanillaLook.Map.Clear();
        else
            modLooks[defaultMod!].Map.Clear();

        if (!originalIsComplete)
        {
            planned.Looks.Remove(vanillaLook);
            plan.Warnings.Add($"{family.DisplayName}: the game's original art could not be kept, so it is not offered as a look.");
        }

        // Drop mod looks that ended up identical to the Original look (nothing usable).
        planned.Looks.RemoveAll(l => !l.Look.IsVanilla && l.Changed.Count == 0 && l.Look.Id != planned.InstallDefault);
        if (planned.Looks.Count < 2) return null;

        AddDoorCompanions(context, family, planned, replacers, produced, defaultMod);

        foreach (var look in planned.Looks)
        {
            look.PreviewSprite = PreviewFor(family, look);
            LinkDependents(context, family, look);
        }

        return planned;
    }

    // ── doors ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The swing sprite the engine plays when a door opens: obj_door replaces the FIRST
    /// <c>_closed</c> in the closed sprite's name with <c>_swing</c> (GML string_replace replaces
    /// the first occurrence only). Null when the name has no <c>_closed</c>.
    /// </summary>
    public static string? DoorSwingName(string closedSprite)
    {
        var at = closedSprite.IndexOf("_closed", StringComparison.Ordinal);
        return at < 0 ? null : closedSprite[..at] + "_swing" + closedSprite[(at + "_closed".Length)..];
    }

    /// <summary>The closed sprite whose door would swing with <paramref name="swingSprite"/>.</summary>
    public static string? DoorClosedName(string swingSprite)
    {
        var at = swingSprite.IndexOf("_swing", StringComparison.Ordinal);
        return at < 0 ? null : swingSprite[..at] + "_closed" + swingSprite[(at + "_swing".Length)..];
    }

    /// <summary>
    /// A look renames its door sprites, but the engine still derives the swing animation from
    /// the closed sprite's name (see <see cref="DoorSwingName"/>); a name that does not exist
    /// stops the door from opening. For every look this makes sure the closed sprite it shows
    /// derives the swing sprite it shows: by adding the derived swing name (a copy of the
    /// look's swing art) or, when that name already holds other art, a closed sprite whose
    /// derived name is the look's swing (a copy of the look's closed art).
    /// </summary>
    private static void AddDoorCompanions(
        PlanContext context,
        Family family,
        PlannedFamily planned,
        Dictionary<string, List<(IMod Mod, Replacement Replacement)>> replacers,
        Dictionary<string, HashSet<IMod>> produced,
        IMod? defaultMod)
    {
        // Only real doors: a doorway floor is named "..._closed" too, but the engine sets it
        // directly (doorway_floor) and never derives anything from its name.
        var pairs = new List<(string Closed, string Swing)>();
        foreach (var sprite in family.DoorSprites.Order(StringComparer.Ordinal))
        {
            var swing = DoorSwingName(sprite);
            if (swing is not null && context.Pristine.HasSprite(swing)) pairs.Add((sprite, swing));
        }
        if (pairs.Count == 0) return;

        var modByLook = context.LookByMod.ToDictionary(kv => kv.Value.Id, kv => kv.Key, StringComparer.Ordinal);

        // Where the art of every sprite name this family can show comes from:
        //   "p:<sprite>"          the game's own art of <sprite>
        //   "m:<look>:<sprite>"   a mod's replacement of <sprite> (packed as that look's sprite)
        //   "b:<sprite>"          the base sprite while some mod replaces it (not copied)
        var art = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var look in planned.Looks)
        {
            foreach (var (sprite, target) in look.Map)
            {
                if (target == VariantFramework.LookSpriteName(sprite, VariantFramework.VanillaLook))
                    art[target] = "p:" + sprite;
                else if (target == VariantFramework.LookSpriteName(sprite, look.Look.Id))
                    art[target] = "m:" + look.Look.Id + ":" + sprite;
            }
        }

        // A base sprite shows the game's art unless a replacement still lands on it: the default
        // mod's, or one that could not be packed as a look of its own.
        string ArtOf(string name)
        {
            if (art.TryGetValue(name, out var known)) return known;
            if (!replacers.TryGetValue(name, out var list)) return "p:" + name;
            var packed = produced.GetValueOrDefault(name);
            var remaining = list
                .Where(r => ReferenceEquals(r.Mod, defaultMod) || packed is null || !packed.Contains(r.Mod))
                .Select(r => r.Mod)
                .Distinct(ReferenceEqualityComparer.Instance)
                .ToList();
            if (remaining.Count == 0) return "p:" + name;
            if (remaining.Count == 1 && ReferenceEquals(remaining[0], defaultMod) && defaultMod is not null)
                return "m:" + context.LookByMod[defaultMod].Id + ":" + name;
            return "b:" + name;
        }

        bool Exists(string name) => art.ContainsKey(name) || context.Pristine.HasSprite(name);

        bool Copy(string artKey, string newName)
        {
            if (artKey.StartsWith("p:", StringComparison.Ordinal))
            {
                var entry = context.Pristine.GetSprite(artKey[2..]);
                return entry is not null && TryAddVanillaCopy(context, entry, newName);
            }

            if (artKey.StartsWith("m:", StringComparison.Ordinal))
            {
                var parts = artKey.Split(':', 3);
                if (!modByLook.TryGetValue(parts[1], out var mod)) return false;
                var source = parts[2];
                var entry = context.Pristine.GetSprite(source);
                var replacement = replacers.GetValueOrDefault(source)?.FirstOrDefault(r => ReferenceEquals(r.Mod, mod)).Replacement;
                return entry is not null && replacement is not null && TryAddLookSprite(context, mod, replacement, entry, newName);
            }

            return false;
        }

        foreach (var look in planned.Looks)
        {
            // The install default shows the base sprites, whose names already pair up.
            if (look.Look.Id == planned.InstallDefault) continue;
            foreach (var (closed, swing) in pairs)
            {
                var shownClosed = look.Resolve(closed);
                var shownSwing = look.Resolve(swing);
                if (shownClosed == closed && shownSwing == swing) continue;

                var derived = DoorSwingName(shownClosed);
                if (derived is null || derived == shownSwing) continue;

                var swingArt = ArtOf(shownSwing);
                if (Exists(derived))
                {
                    // Already there with the same art: just say so in the map.
                    if (ArtOf(derived) == swingArt)
                    {
                        look.Map[swing] = derived;
                        continue;
                    }
                }
                else if (derived.EndsWith("_" + look.Look.Id, StringComparison.Ordinal) && Copy(swingArt, derived))
                {
                    art[derived] = swingArt;
                    look.Map[swing] = derived;
                    continue;
                }

                // The derived name holds other art (another look's): give this look a closed
                // sprite of its own whose derived name is the swing it shows.
                var ownClosed = DoorClosedName(shownSwing);
                var closedArt = ArtOf(shownClosed);
                if (ownClosed is not null && ownClosed != shownClosed)
                {
                    if (Exists(ownClosed) && ArtOf(ownClosed) == closedArt)
                    {
                        look.Map[closed] = ownClosed;
                        continue;
                    }

                    if (!Exists(ownClosed) && ownClosed.EndsWith("_" + look.Look.Id, StringComparison.Ordinal) &&
                        Copy(closedArt, ownClosed))
                    {
                        art[ownClosed] = closedArt;
                        look.Map[closed] = ownClosed;
                        continue;
                    }
                }

                context.Plan.Warnings.Add($"{family.DisplayName}: the {look.Look.Name} look's door could not be given its opening animation; that door keeps its usual art.");
            }
        }
    }

    private static string? PreviewFor(Family family, FamilyLook look)
    {
        if (family.PreviewSprite is not null && (look.Look.IsVanilla || look.Changed.Count == 0 ||
                                                 look.Changed.Contains(family.PreviewSprite)))
            return family.PreviewSprite;

        return look.Changed
                   .OrderByDescending(s => PreviewScore(s))
                   .ThenBy(s => s, StringComparer.Ordinal)
                   .FirstOrDefault()
               ?? family.PreviewSprite;
    }

    private static int PreviewScore(string sprite)
    {
        var score = 0;
        if (sprite.Contains("_idle", StringComparison.Ordinal)) score += 8;
        if (sprite.EndsWith("_south", StringComparison.Ordinal) || sprite.Contains("_south_", StringComparison.Ordinal)) score += 4;
        if (sprite.EndsWith("_east", StringComparison.Ordinal)) score += 3;
        if (sprite.Contains("spring", StringComparison.Ordinal)) score += 2;
        if (sprite.Contains("shadow", StringComparison.Ordinal) || sprite.Contains("outline", StringComparison.Ordinal) ||
            sprite.Contains("_mask", StringComparison.Ordinal) || sprite.Contains("baby", StringComparison.Ordinal) ||
            sprite.Contains("cosmetic", StringComparison.Ordinal) || sprite.Contains("_top", StringComparison.Ordinal))
            score -= 10;
        if (sprite.StartsWith("spr_ui_", StringComparison.Ordinal)) score -= 4;
        return score;
    }

    // New look sprites keep their shadow and outline: a look's lamp casts the look's own
    // shadow when the mod replaced it, otherwise the vanilla shadow.
    private static void LinkDependents(PlanContext context, Family family, FamilyLook look)
    {
        foreach (var (sprite, target) in look.Map)
        {
            if (target == sprite) continue;
            if (context.Index.Shadows.TryGetValue(sprite, out var shadow))
                context.Plan.ShadowLinks[target] = look.Resolve(shadow);
            if (context.Index.Outlines.TryGetValue(sprite, out var outline))
                context.Plan.OutlineLinks[target] = look.Resolve(outline);
        }
    }

    // ── install-only ─────────────────────────────────────────────────────────

    private static string GroupKey(string sprite, List<(IMod Mod, Replacement Replacement)> list, PristineAssets pristine)
    {
        var folder = pristine.GetSprite(sprite)?.Folder ?? "";
        var mods = string.Join("|", list.Select(r => r.Mod.GetId().ToLowerInvariant()).Distinct().Order(StringComparer.Ordinal));
        return folder + "#" + mods;
    }

    private static PlannedFamily? PlanInstallOnly(
        Dictionary<string, List<(IMod Mod, Replacement Replacement)>> group,
        List<IMod> contentMods,
        PlanContext context)
    {
        var plan = context.Plan;
        var sprites = group.Keys.Order(StringComparer.Ordinal).ToList();
        var mods = contentMods.Where(m => group.Values.Any(l => l.Any(r => ReferenceEquals(r.Mod, m)))).ToList();
        var folder = context.Pristine.GetSprite(sprites[0])?.Folder ?? "Modded art";
        var key = folder + "#" + string.Join("|", mods.Select(m => m.GetId().ToLowerInvariant()).Order(StringComparer.Ordinal));
        var familyId = "scene:" + VariantFramework.Hash4(key) + VariantFramework.Hash4(key + "#");

        var family = new Family
        {
            Id = familyId,
            Kind = FamilyKind.InstallOnly,
            Key = folder,
            DisplayName = $"{folder.Replace("/", " / ")} ({sprites.Count} image{(sprites.Count == 1 ? "" : "s")})",
        };
        foreach (var sprite in sprites) family.Add(sprite);
        family.PreviewSprite = sprites.OrderByDescending(PreviewScore).ThenBy(s => s, StringComparer.Ordinal).First();

        var preference = plan.Preferences.GlobalLooks.GetValueOrDefault(familyId);
        var looks = mods.Select(m => context.LookByMod[m]).ToList();
        var chosen = preference == VariantFramework.VanillaLook
            ? plan.Looks[VariantFramework.VanillaLook]
            : looks.FirstOrDefault(l => l.Id == preference) ?? looks[^1];

        var planned = new PlannedFamily { Family = family, InstallDefault = chosen.Id };
        planned.Looks.Add(new FamilyLook { Look = plan.Looks[VariantFramework.VanillaLook], PreviewSprite = family.PreviewSprite });
        foreach (var mod in mods)
        {
            var look = new FamilyLook { Look = context.LookByMod[mod], PreviewSprite = family.PreviewSprite };
            foreach (var sprite in sprites)
                if (group[sprite].Any(r => ReferenceEquals(r.Mod, mod))) look.Changed.Add(sprite);
            planned.Looks.Add(look);
        }

        // Install only the chosen version of each contested image.
        foreach (var sprite in sprites)
        foreach (var (mod, replacement) in group[sprite])
            if (context.LookByMod[mod] != chosen)
                HideReplacement(plan.RewriteFor(mod.GetId()), replacement);

        // A thumbnail per option so the in-game catalog can show what each choice looks like.
        foreach (var look in planned.Looks)
        {
            var preview = look.Changed.Count > 0
                ? look.Changed.OrderByDescending(PreviewScore).ThenBy(s => s, StringComparer.Ordinal).First()
                : family.PreviewSprite;
            if (preview is null) continue;
            look.PreviewSprite = preview;
            var thumbName = $"spr_mvf_thumb_{VariantFramework.Hash4(familyId + look.Look.Id)}{VariantFramework.Hash4(look.Look.Id + familyId)}";
            Func<byte[]?> source;
            if (look.Look.IsVanilla)
            {
                var entry = context.Pristine.GetSprite(preview);
                source = () => entry?.PngPath is null ? null : context.Pristine.ReadBytes(entry.PngPath);
            }
            else
            {
                var mod = mods.First(m => context.LookByMod[m] == look.Look);
                var replacement = group[preview].First(r => ReferenceEquals(r.Mod, mod)).Replacement;
                source = () => ReadModBytes(mod, replacement.PngPath);
            }

            var entryMeta = context.Pristine.GetSprite(preview);
            var frames = entryMeta is null ? 1 : FrameCount(ReadMeta(context.Pristine, entryMeta));
            if (TryAddThumbnail(context, thumbName, source, frames)) look.Thumbnail = thumbName;
        }

        return planned;
    }

    // ── sprite generation ────────────────────────────────────────────────────

    /// <summary>
    /// The sprite name a look uses for a vanilla sprite. Animal body sprites keep the engine's
    /// <c>spr_animal_{kind}_{key}_{anim}_{dir}</c> pattern with the look id appended to the key,
    /// so a variant animal can point its key at a complete look family.
    /// </summary>
    public static string LookSpriteName(Family family, string sprite, string lookId, VariantPlan plan)
    {
        if (family.Kind == FamilyKind.Animal &&
            plan.AnimalAnimations.TryGetValue(family.Key, out var animations) &&
            AnimalSpriteName.TryParse(sprite, family.Key, animations, out var parts))
            return parts.WithKey(parts.Key + "_" + lookId);

        if (family.Kind == FamilyKind.Animal &&
            sprite.StartsWith($"spr_ui_icon_animal_{family.Key}_", StringComparison.Ordinal))
            return sprite + "_" + lookId;

        return VariantFramework.LookSpriteName(sprite, lookId);
    }

    private static bool TryAddLookSprite(PlanContext context, IMod mod, Replacement replacement,
        PristineAssets.SpriteEntry entry, string lookSprite)
    {
        var meta = ReadMeta(context.Pristine, entry);
        if (meta is null) return false;
        meta = CloneTable(meta);

        if (replacement.MetaPath is not null)
        {
            var modMeta = FamilyIndex.ParseQuiet(mod.ReadFile(replacement.MetaPath));
            if (modMeta.TryGetValue("asset_properties", out var a) && a is TomlTable modAsset)
                MergeTables(EnsureTable(meta, "asset_properties"), modAsset);
        }

        byte[]? png;
        try
        {
            png = ReadModBytes(mod, replacement.PngPath);
        }
        catch
        {
            return false;
        }

        if (png is null || !FitMetaToImage(meta, png)) return false;

        string? shape = null;
        if (replacement.ShapePath is not null)
            shape = ShapeText(FamilyIndex.ParseQuiet(mod.ReadFile(replacement.ShapePath)));
        if (shape is null && entry.ShapePath is not null)
            shape = ShapeText(FamilyIndex.ParseQuiet(context.Pristine.ReadText(entry.ShapePath) ?? ""));

        var rewrite = context.Plan.RewriteFor(mod.GetId());
        AddSpriteFiles(rewrite, entry.Folder, lookSprite, meta, () => ReadModBytes(mod, replacement.PngPath) ?? [], shape);
        context.Plan.LookSpriteCount++;
        return true;
    }

    private static bool TryAddVanillaCopy(PlanContext context, PristineAssets.SpriteEntry entry, string vanillaSprite)
    {
        if (entry.PngPath is null) return false;
        var meta = ReadMeta(context.Pristine, entry);
        if (meta is null || !HasAtlas(meta)) return false;
        meta = CloneTable(meta);
        var png = context.Pristine.ReadBytes(entry.PngPath);
        if (png is null || !FitMetaToImage(meta, png)) return false;

        string? shape = null;
        if (entry.ShapePath is not null)
            shape = ShapeText(FamilyIndex.ParseQuiet(context.Pristine.ReadText(entry.ShapePath) ?? ""));

        var rewrite = context.Plan.RewriteFor(context.Plan.Framework!.GetId());
        var pngPath = entry.PngPath;
        AddSpriteFiles(rewrite, entry.Folder, vanillaSprite, meta, () => context.Pristine.ReadBytes(pngPath) ?? [], shape);
        context.Plan.VanillaCopyCount++;
        return true;
    }

    private static bool TryAddThumbnail(PlanContext context, string thumbName, Func<byte[]?> source, int frames)
    {
        byte[]? thumbnail;
        int width, height;
        try
        {
            var bytes = source();
            if (bytes is null) return false;
            using var image = Image.Load<Rgba32>(bytes);
            var frameWidth = Math.Max(1, image.Width / Math.Max(1, frames));
            using var frame = image.Clone(c => c.Crop(new Rectangle(0, 0, frameWidth, image.Height)));
            var scale = Math.Min(1.0, Math.Min((double)ThumbnailSize / frame.Width, (double)ThumbnailSize / frame.Height));
            width = Math.Max(1, (int)Math.Round(frame.Width * scale));
            height = Math.Max(1, (int)Math.Round(frame.Height * scale));
            if (width != frame.Width || height != frame.Height)
                frame.Mutate(c => c.Resize(width, height, KnownResamplers.NearestNeighbor));
            using var output = new MemoryStream();
            frame.Save(output, new PngEncoder());
            thumbnail = output.ToArray();
        }
        catch
        {
            return false;
        }

        var meta = new TomlTable
        {
            ["meta_properties"] = new TomlTable { ["asset_kind"] = "Animation" },
            ["asset_properties"] = new TomlTable
            {
                ["frame_size"] = new TomlArray { (long)width, (long)height },
                ["atlas"] = "UI",
                ["offset"] = new TomlTable { ["horizontal"] = "Middle", ["vertical"] = "Middle" },
            },
        };
        var rewrite = context.Plan.RewriteFor(context.Plan.Framework!.GetId());
        var data = thumbnail;
        rewrite.Add(new VirtualFile { Path = $"images/mvf_thumbs/{thumbName}.meta.toml", Text = Toml.SerializeGameToml(meta) });
        rewrite.Add(new VirtualFile { Path = $"images/mvf_thumbs/{thumbName}.png", Bytes = () => data });
        return true;
    }

    private static void AddSpriteFiles(ModRewrite rewrite, string folder, string spriteName, TomlTable meta,
        Func<byte[]> png, string? shapeText)
    {
        var animationFolder = string.IsNullOrEmpty(folder) ? "animations" : $"animations/{folder}";
        rewrite.Add(new VirtualFile { Path = $"{animationFolder}/{spriteName}.meta.toml", Text = Toml.SerializeGameToml(meta) });
        rewrite.Add(new VirtualFile { Path = $"{animationFolder}/{spriteName}.png", Bytes = png });
        if (shapeText is null) return;
        var shapeFolder = string.IsNullOrEmpty(folder) ? "shapes" : $"shapes/{folder}";
        rewrite.Add(new VirtualFile
        {
            Path = $"{shapeFolder}/poly_{spriteName["spr_".Length..]}.meta.toml",
            Text = shapeText,
        });
    }

    private static void HideReplacement(ModRewrite rewrite, Replacement replacement)
    {
        rewrite.Hidden.Add(replacement.PngPath);
        if (replacement.MetaPath is not null) rewrite.Hidden.Add(replacement.MetaPath);
        if (replacement.ShapePath is not null) rewrite.Hidden.Add(replacement.ShapePath);
    }

    // ── meta helpers ─────────────────────────────────────────────────────────

    // Cached on the archive itself: a process-wide cache keyed by the archive's hash code
    // outlived every install in the GUI process and could hand one archive's metadata to another
    // whose hash code happened to match (after a game update, say).
    internal static TomlTable? ReadMeta(PristineAssets pristine, PristineAssets.SpriteEntry entry)
    {
        var cache = pristine.MetaCache;
        lock (cache)
        {
            if (cache.TryGetValue(entry.MetaPath, out var cached)) return cached;
            var text = pristine.ReadText(entry.MetaPath);
            var table = text is null ? null : FamilyIndex.ParseQuiet(text);
            if (table is not null && table.Count == 0) table = null;
            cache[entry.MetaPath] = table;
            return table;
        }
    }

    private static bool HasAtlas(TomlTable meta)
    {
        if (!meta.TryGetValue("asset_properties", out var a) || a is not TomlTable asset) return false;
        if (!asset.TryGetValue("atlas", out var atlas) || atlas is not string atlasName || atlasName.Length == 0) return false;
        if (asset.TryGetValue("tags", out var t) && t is TomlArray tags &&
            tags.OfType<string>().Any(tag => tag.Equals("manual-load", StringComparison.OrdinalIgnoreCase)))
            return false;
        return true;
    }

    private static int FrameCount(TomlTable? meta)
    {
        if (meta is not null && meta.TryGetValue("asset_properties", out var a) && a is TomlTable asset &&
            asset.TryGetValue("frame_len", out var f) && f is long count && count > 0)
            return (int)count;
        return 1;
    }

    // Keeps the strip's frame count and fixes the frame size to the image, exactly as the
    // installer does for replacements; refuses a strip that cannot be split evenly.
    private static bool FitMetaToImage(TomlTable meta, byte[] png)
    {
        ImageInfo info;
        try
        {
            info = Image.Identify(png);
        }
        catch
        {
            return false;
        }

        var meta_ = EnsureTable(meta, "meta_properties");
        meta_.Remove("id");
        meta_.Remove("replace_id");
        meta_["asset_kind"] = "Animation";

        var asset = EnsureTable(meta, "asset_properties");
        if (!HasAtlas(meta)) return false;
        var frames = FrameCount(meta);
        long frameWidth = 0, frameHeight = 0;
        if (asset.TryGetValue("frame_size", out var fs) && fs is TomlArray size && size.Count >= 2)
        {
            frameWidth = Convert.ToInt64(size[0]);
            frameHeight = Convert.ToInt64(size[1]);
        }

        if (frameWidth * frames == info.Width && frameHeight == info.Height) return true;
        if (info.Width % frames != 0) return false;

        asset["frame_size"] = new TomlArray { (long)(info.Width / frames), (long)info.Height };
        asset["dimensions"] = new TomlArray { (long)info.Width, (long)info.Height };
        return true;
    }

    private static string? ShapeText(TomlTable shape)
    {
        if (shape.Count == 0) return null;
        var clone = CloneTable(shape);
        var meta = EnsureTable(clone, "meta_properties");
        meta.Remove("id");
        meta.Remove("required_assets");
        meta["asset_kind"] = "Shape";
        return Toml.SerializeGameToml(clone);
    }

    internal static byte[]? ReadModBytes(IMod mod, string path)
    {
        using var stream = mod.ReadFileAsStream(path);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    internal static TomlTable EnsureTable(TomlTable parent, string key)
    {
        if (parent.TryGetValue(key, out var value) && value is TomlTable table) return table;
        table = new TomlTable();
        parent[key] = table;
        return table;
    }

    internal static void MergeTables(TomlTable into, TomlTable from)
    {
        foreach (var (key, value) in from)
        {
            if (value is TomlTable child && into.TryGetValue(key, out var existing) && existing is TomlTable target)
                MergeTables(target, child);
            else
                into[key] = CloneValue(value);
        }
    }

    internal static TomlTable CloneTable(TomlTable table)
    {
        var clone = new TomlTable();
        foreach (var (key, value) in table) clone[key] = CloneValue(value);
        return clone;
    }

    internal static object CloneValue(object? value) => value switch
    {
        TomlTable t => CloneTable(t),
        TomlTableArray ta => CloneTableArray(ta),
        TomlArray a => CloneArray(a),
        null => throw new InvalidOperationException("TOML values cannot be null."),
        _ => value,
    };

    private static TomlTableArray CloneTableArray(TomlTableArray source)
    {
        var clone = new TomlTableArray();
        foreach (var t in source) clone.Add(CloneTable(t));
        return clone;
    }

    private static TomlArray CloneArray(TomlArray source)
    {
        var clone = new TomlArray();
        foreach (var item in source) clone.Add(CloneValue(item));
        return clone;
    }
}

/// <summary>Parses <c>spr_animal_{kind}_{key}_{anim}_{dir}</c> with the kind's known animations.</summary>
public readonly record struct AnimalSpriteName(string Kind, string Key, string Animation, string Direction)
{
    private static readonly string[] Directions = ["east", "north", "south"];

    public string WithKey(string key) => $"spr_animal_{Kind}_{key}_{Animation}_{Direction}";

    public static bool TryParse(string sprite, string kind, IReadOnlyList<string> animationsLongestFirst, out AnimalSpriteName parts)
    {
        parts = default;
        var prefix = $"spr_animal_{kind}_";
        if (!sprite.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var rest = sprite[prefix.Length..];
        var direction = Directions.FirstOrDefault(d => rest.EndsWith("_" + d, StringComparison.Ordinal));
        if (direction is null) return false;
        rest = rest[..^(direction.Length + 1)];
        foreach (var animation in animationsLongestFirst)
        {
            if (!rest.EndsWith("_" + animation, StringComparison.Ordinal)) continue;
            var key = rest[..^(animation.Length + 1)];
            if (key.Length == 0) continue;
            parts = new AnimalSpriteName(kind, key, animation, direction);
            return true;
        }

        return false;
    }
}
