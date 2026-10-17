using Garethp.ModsOfMistriaInstallerLib;
using Garethp.ModsOfMistriaInstallerLib.Installer;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;
using Garethp.ModsOfMistriaInstallerLib.PortraitWardrobe;
using Garethp.ModsOfMistriaInstallerLib.Utils;
using ModsOfMistriaInstallerLibTests.Fixtures;
using ModsOfMistriaInstallerLibTests.TestUtils;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Tomlyn;
using Tomlyn.Model;

namespace ModsOfMistriaInstallerLibTests.PortraitWardrobe;

// The Portrait Wardrobe library: every installed pack's portraits kept side by side, plus a registry
// the in-game menu reads. The rules under test are the ones that decide what the player can pick:
// which versions get their own sprite, which atlas they live in, and what the registry says.
[TestFixture]
public class PortraitWardrobeBuilderTest
{
    private const string Beach = "spr_portrait_march_beach_neutral";
    private const string Bath = "spr_portrait_march_beach_bath_neutral";
    private const string NewOutfit = "spr_portrait_march_bath_happy";

    private const string GameMeta = """
        [meta_properties]
        id = "aa8f8a6bfb8a1796"
        asset_kind = "Animation"

        [asset_properties]
        frame_size = [296, 180]
        frame_len = 2
        duration = 0.2
        atlas = "PortraitsSummer"
        """;

    private const string ModNewMeta = """
        [meta_properties]
        asset_kind = "Animation"

        [asset_properties]
        frame_size = [296, 180]
        frame_len = 2
        dimensions = [592, 180]
        duration = 0.2
        atlas = "Npc"

        [asset_properties.offset]
        horizontal = "Left"
        vertical = "Top"
        """;

    private static byte[] Png(int width, int height, byte shade = 200)
    {
        using var image = new Image<Rgba32>(width, height);
        image[width / 2, height / 2] = new Rgba32(shade, 10, 10, 255);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private static DictionaryPortraitVanillaSource Vanilla(params string[] sprites) =>
        new(sprites.ToDictionary(s => s, _ => (GameMeta, Png(592, 180))));

    private static MockMod Framework() => new(new Dictionary<string, object>
    {
        ["fiddle/mods/portrait_wardrobe/registry.toml"] = "format = 1\ndata = '{}'",
        ["gml/PortraitWardrobe.gml"] = "// boot",
    })
    {
        Id = "silvertonguered.portrait_wardrobe", Name = "Portrait Wardrobe", DirName = "portrait_wardrobe",
    };

    private static MockMod ReplacePack(string id, string name, params string[] sprites)
    {
        var files = sprites.ToDictionary(s => $"images/replace/{s}.png", object (_) => Png(592, 180));
        return new MockMod(files) { Id = id, Name = name, Author = id.Split('.')[0], DirName = name };
    }

    private static PortraitLibraryResult Build(IPortraitVanillaSource vanilla, params IMod[] mods) =>
        new PortraitWardrobeBuilder(vanilla).Build(mods.ToList(), "AIM test", DateTimeOffset.UnixEpoch);

    private static JObject Registry(PortraitLibraryResult result) => JObject.Parse(result.RegistryJson);

    // ── detecting the framework ──

    [Test]
    public void ShouldRecogniseTheFrameworkByItsPlaceholderRegistry()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PortraitWardrobeBuilder.IsFramework(Framework()), Is.True);
            Assert.That(PortraitWardrobeBuilder.IsFramework(ReplacePack("a.b", "Pack", Beach)), Is.False);
            Assert.That(PortraitWardrobeBuilder.IsFrameworkInstalled([ReplacePack("a.b", "Pack", Beach), Framework()]), Is.True);
        });
    }

    // ── scanning ──

    [Test]
    public void ShouldFindPortraitsBothWaysTheInstallerDoes()
    {
        var mod = new MockMod(new Dictionary<string, object>
        {
            [$"images/replace/{Beach}.png"] = Png(592, 180),
            ["images/replace/spr_portrait_effects_sweat.png"] = Png(32, 32),
            ["images/replace/spr_npc_march_beach_idle_south.png"] = Png(32, 32),
            [$"images/bath portrait/{NewOutfit}.png"] = Png(592, 180),
            [$"images/bath portrait/{NewOutfit}.meta.toml"] = ModNewMeta,
            ["images/bath portrait/spr_portrait_march_bath_sad.png"] = Png(592, 180), // no meta: the installer skips it too
        });

        var found = PortraitScanner.Scan(mod);

        Assert.Multiple(() =>
        {
            // Sorted by name, so the order never depends on how the mod's files happen to be listed.
            Assert.That(found.Select(c => c.SpriteName), Is.EqualTo(new[] { NewOutfit, Beach }));
            Assert.That(found[0].IsReplacement, Is.False);
            Assert.That(found[0].MetaPath, Is.EqualTo($"images/bath portrait/{NewOutfit}.meta.toml"));
            Assert.That(found[1].IsReplacement, Is.True);
            Assert.That(found[1].MetaPath, Is.Null);
        });
    }

    [Test]
    public void ShouldNoticeWhatElseAPackChanges()
    {
        var mod = new MockMod(new Dictionary<string, object>
        {
            [$"images/replace/{Beach}.png"] = Png(592, 180),
            ["t2/Conversations/Bank/March/Banked Lines/line.c.toml"] = "x = 1",
            ["mist_scripts/heart_events/march_three_hearts.mist"] = "x",
            ["fiddle/npcs/march.toml"] = "outfits = []",
            ["images/replace/spr_npc_march_beach_idle_south.png"] = Png(4, 4),
        });

        Assert.That(PortraitScanner.CompanionContent(mod),
            Is.EquivalentTo(new[] { "dialogue", "cutscenes", "character_data", "overworld_sprites" }));
    }

    // ── what gets its own sprite ──

    [Test]
    public void ShouldKeepTheOriginalButNotCopyASingleReplacement()
    {
        var result = Build(Vanilla(Beach), ReplacePack("someone.nudity", "Nudity", Beach), Framework());
        var entry = Registry(result)["sprites"]![Beach]!;

        Assert.Multiple(() =>
        {
            Assert.That((string?)entry["src"]!["p0"], Is.EqualTo(Beach), "one replacement is unambiguously that pack's art");
            Assert.That((string?)entry["vanilla"], Is.EqualTo("spr_pw_v_portrait_march_beach_neutral"));
            Assert.That((string?)entry["winner"], Is.EqualTo("p0"));
            Assert.That(result.VariantMod.SpriteCount, Is.EqualTo(1), "only the original is copied");
        });
    }

    [Test]
    public void ShouldCopyEveryVersionWhenPacksOverlap()
    {
        var result = Build(Vanilla(Bath),
            ReplacePack("someone.nudity", "Nudity", Bath),
            ReplacePack("toad2toad.bush", "Bush", Bath),
            Framework());
        var entry = Registry(result)["sprites"]![Bath]!;

        Assert.Multiple(() =>
        {
            Assert.That((string?)entry["src"]!["p0"], Is.EqualTo("spr_pw_p0_portrait_march_beach_bath_neutral"));
            Assert.That((string?)entry["src"]!["p1"], Is.EqualTo("spr_pw_p1_portrait_march_beach_bath_neutral"));
            Assert.That((string?)entry["winner"], Is.EqualTo("p1"), "the last pack in load order is what the game shows by default");
            Assert.That(result.VariantMod.SpriteCount, Is.EqualTo(3), "both packs and the original");
        });
    }

    [Test]
    public void ShouldCopyASameNamedOverrideBecauseWhichOneWinsIsNotCertain()
    {
        var pack = new MockMod(new Dictionary<string, object>
        {
            [$"images/bath portraits/{Bath}.png"] = Png(592, 180),
            [$"images/bath portraits/{Bath}.meta.toml"] = ModNewMeta,
        }) { Id = "x.valen_but_a_man", Name = "Valen But A Man" };

        var result = Build(Vanilla(Bath), pack, Framework());
        var entry = Registry(result)["sprites"]![Bath]!;

        Assert.That((string?)entry["src"]!["p0"], Is.EqualTo("spr_pw_p0_portrait_march_beach_bath_neutral"));
    }

    [Test]
    public void ShouldUseABrandNewPortraitAsIs()
    {
        var pack = new MockMod(new Dictionary<string, object>
        {
            [$"images/bath portrait/{NewOutfit}.png"] = Png(592, 180),
            [$"images/bath portrait/{NewOutfit}.meta.toml"] = ModNewMeta,
        }) { Id = "caterpixie.march_expanded", Name = "March Expanded" };

        var result = Build(Vanilla(), pack, Framework());
        var entry = Registry(result)["sprites"]![NewOutfit]!;

        Assert.Multiple(() =>
        {
            Assert.That((string?)entry["src"]!["p0"], Is.EqualTo(NewOutfit));
            Assert.That(entry["vanilla"], Is.Null, "the game never had it");
            Assert.That(result.VariantMod.SpriteCount, Is.EqualTo(0));
        });
    }

    [Test]
    public void ShouldBorrowTheFrameLayoutFromAnotherPackWhenTheGameHasNone()
    {
        // A recolour replacing a portrait another pack adds, with no meta of its own.
        var adds = new MockMod(new Dictionary<string, object>
        {
            [$"images/bath portrait/{NewOutfit}.png"] = Png(592, 180),
            [$"images/bath portrait/{NewOutfit}.meta.toml"] = ModNewMeta,
        }) { Id = "caterpixie.march_expanded", Name = "March Expanded" };
        var recolour = ReplacePack("x.recolour", "Recolour", NewOutfit);

        var result = Build(Vanilla(), adds, recolour, Framework());
        var entry = Registry(result)["sprites"]![NewOutfit]!;

        Assert.Multiple(() =>
        {
            Assert.That((string?)entry["src"]!["p1"], Is.EqualTo("spr_pw_p1_portrait_march_bath_happy"));
            Assert.That(result.Notes, Is.Empty);
        });
    }

    [Test]
    public void ShouldLeaveOutABrokenPngAndSaySo()
    {
        var broken = new MockMod(new Dictionary<string, object> { [$"images/replace/{Bath}.png"] = new byte[] { 1, 2, 3 } })
            { Id = "x.broken", Name = "Broken" };

        var result = Build(Vanilla(Bath), ReplacePack("a.good", "Good", Bath), broken, Framework());
        var entry = Registry(result)["sprites"]![Bath]!;

        Assert.Multiple(() =>
        {
            Assert.That(entry["src"]!["p1"], Is.Null);
            Assert.That(entry["src"]!["p0"], Is.Not.Null);
            Assert.That(result.Notes, Has.Some.Contains("Broken").And.Contains("not a valid image"));
        });
    }

    [Test]
    public void ShouldResizeFramesTheWayReplacementsAreResized()
    {
        var hd = new MockMod(new Dictionary<string, object> { [$"images/replace/{Bath}.png"] = Png(1184, 360) })
            { Id = "x.hd", Name = "HD" };

        var result = Build(Vanilla(Bath), ReplacePack("a.sd", "SD", Bath), hd, Framework());
        var meta = result.VariantMod.ReadFile($"animations/PortraitWardrobe/march/spr_pw_p1_portrait_march_beach_bath_neutral.meta.toml");
        var table = TomlSerializer.Deserialize<TomlTable>(meta)!;
        var asset = (TomlTable)table["asset_properties"];

        Assert.Multiple(() =>
        {
            Assert.That(((TomlArray)asset["frame_size"]).Select(Convert.ToInt32), Is.EqualTo(new[] { 592, 360 }));
            Assert.That(Convert.ToInt32(asset["frame_len"]), Is.EqualTo(2));
            Assert.That(asset["atlas"], Is.EqualTo("PortraitsSummer"), "kept beside the game's own copy");
        });
    }

    [Test]
    public void ShouldNotScanTheFrameworkOrPacksWithoutPortraits()
    {
        var noPortraits = new MockMod(new Dictionary<string, object> { ["images/replace/spr_npc_x.png"] = Png(4, 4) })
            { Id = "x.sprites", Name = "Sprites" };

        var result = Build(Vanilla(Beach), noPortraits, ReplacePack("a.b", "Pack", Beach), Framework());
        var packs = (JArray)Registry(result)["packs"]!;

        Assert.Multiple(() =>
        {
            Assert.That(packs.Count, Is.EqualTo(1));
            Assert.That((string?)packs[0]["id"], Is.EqualTo("a.b"));
            Assert.That((int?)packs[0]["order"], Is.EqualTo(1), "order is the install position");
        });
    }

    // ── the registry ──

    [Test]
    public void ShouldWriteARegistryTheGameCanRead()
    {
        var result = Build(Vanilla(Beach), ReplacePack("a.b", "Pack \"Quoted\" 'n' \\ slashed", Beach), Framework());

        var toml = TomlSerializer.Deserialize<TomlTable>(result.RegistryToml)!;
        var data = JObject.Parse((string)toml["data"]);

        Assert.Multiple(() =>
        {
            Assert.That(Convert.ToInt32(toml["format"]), Is.EqualTo(1));
            Assert.That((bool?)data["built"], Is.True);
            Assert.That((string?)data["builder"], Is.EqualTo("AIM test"));
            Assert.That((string?)data["packs"]![0]!["name"], Is.EqualTo("Pack \"Quoted\" 'n' \\ slashed"));
            Assert.That(data["sprites"]![Beach], Is.Not.Null);
        });
    }

    [Test]
    public void ShouldListManifestRequirementsWithWhetherTheyAreInstalled()
    {
        var needsMe = ReplacePack("x.reskin", "Reskin", NewOutfit);
        var reskin = new MockMod(new Dictionary<string, object> { [$"images/replace/{Beach}.png"] = Png(592, 180) })
        {
            Id = "x.reskin", Name = "Reskin",
            Requirements = [new ModRequirement("March Expanded", "caterpixie", "https://www.nexusmods.com/fieldsofmistria/mods/669"),
                            new ModRequirement("Pack", "a")],
        };
        var result = Build(Vanilla(Beach), ReplacePack("a.pack", "Pack", Beach), reskin, Framework());
        var reqs = (JArray)Registry(result)["packs"]![1]!["requirements"]!;

        Assert.Multiple(() =>
        {
            Assert.That((string?)reqs[0]["name"], Is.EqualTo("March Expanded"));
            Assert.That((bool?)reqs[0]["installed"], Is.False);
            Assert.That((string?)reqs[0]["url"], Is.EqualTo("https://www.nexusmods.com/fieldsofmistria/mods/669"));
            Assert.That((bool?)reqs[1]["installed"], Is.True, "a.pack is in this install");
        });
        _ = needsMe;
    }

    [Test]
    public void ShouldCountARequirementInstalledByHandAsInstalled()
    {
        var fetcher = new FakeFetcher(new Dictionary<int, List<NexusRequirement>>
        {
            [1500] =
            [
                new NexusRequirement(669, "March Expanded", "", "", false, 6685),
                new NexusRequirement(157, "Halloween Outfits (new engine)", "", "", false, 6685),
                new NexusRequirement(999, "Not Here", "", "", false, 6685),
            ],
        });
        // unzipped by hand: no AIM download record, found by name / by its manifest's Nexus link
        var me = new MockMod(new Dictionary<string, object> { ["fiddle/npcs/march.toml"] = "x = 1" }) { Id = "caterpixie.march_expanded", Name = "March  Expanded!" };
        var halloween = new MockMod(new Dictionary<string, object> { ["gml/HalloweenCostumes.gml"] = "//" })
        {
            Id = "jezebel.halloween", Name = "Halloween Outfits",
            DownloadUrl = "https://www.nexusmods.com/fieldsofmistria/mods/157?tab=files",
        };
        var pack = ReplacePack("x.reskin", "Reskin", Beach);
        PortraitPackSource? SourceOf(IMod mod) => mod.GetId() == "x.reskin"
            ? new PortraitPackSource(1500, "https://www.nexusmods.com/fieldsofmistria/mods/1500") : null;

        var result = new PortraitWardrobeBuilder(Vanilla(Beach), SourceOf, new PortraitRequirementsCache(null), fetcher)
            .Build([me, halloween, pack, Framework()], "AIM test", DateTimeOffset.UnixEpoch);
        var reqs = (JArray)Registry(result)["packs"]![0]!["requirements"]!;

        Assert.Multiple(() =>
        {
            Assert.That((bool?)reqs[0]["installed"], Is.True, "same name, spelled a little differently");
            Assert.That((bool?)reqs[1]["installed"], Is.True, "its manifest links the Nexus page");
            Assert.That((bool?)reqs[2]["installed"], Is.False);
        });
    }

    [Test]
    public void ShouldNotOfferASingleReplacementTheInstallerWillSkip()
    {
        var bad = new MockMod(new Dictionary<string, object> { [$"images/replace/{Beach}.png"] = Png(591, 180) })
        {
            Id = "x.bad", Name = "Bad Strip",
        };
        var result = Build(Vanilla(Beach), bad, Framework());
        var entry = Registry(result)["sprites"]![Beach];

        Assert.Multiple(() =>
        {
            Assert.That(entry, Is.Not.Null, "the original art is still kept");
            Assert.That(((JObject)entry!["src"]!).Count, Is.EqualTo(0), "591 px doesn't divide into 2 frames");
            Assert.That(result.Notes.Any(n => n.Contains("Bad Strip") && n.Contains("isn't offered")), Is.True);
        });
    }

    [Test]
    public void ShouldKeyTheRegistryByTheGamesLowerCaseName()
    {
        var upper = "spr_portrait_March_beach_neutral";
        var odd = new MockMod(new Dictionary<string, object> { [$"images/replace/{upper}.png"] = Png(592, 180) })
        {
            Id = "x.odd", Name = "Odd Case",
        };
        var result = Build(Vanilla(Beach), odd, Framework());
        var sprites = (JObject)Registry(result)["sprites"]!;

        Assert.Multiple(() =>
        {
            Assert.That(sprites[Beach], Is.Not.Null);
            Assert.That(sprites.Properties().Any(p => p.Name == upper), Is.False);
            Assert.That((string?)sprites[Beach]!["src"]!["p0"], Is.EqualTo("spr_pw_p0_portrait_march_beach_neutral"),
                "a differently spelled file gets its own copy instead of trusting the game's sprite");
        });
    }

    [Test]
    public void ShouldShrugOffADamagedRequirementsCache()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pw_cache_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, PortraitRequirementsCache.FileName),
                "{ \"mods\": { \"1500\": { \"fetchedAt\": \"2999-01-01T00:00:00Z\", \"requirements\": [ null, "
                + "{ \"ModId\": 669, \"ModName\": \"March Expanded\", \"Url\": \"\", \"Notes\": \"\", \"External\": false, \"GameId\": 6685 } ] } } }");
            var cache = new PortraitRequirementsCache(dir);
            var fetcher = new FakeFetcher(new Dictionary<int, List<NexusRequirement>> { [1500] = [] });

            Assert.That(cache.Get(1500)?.Count, Is.EqualTo(1), "the null is dropped");
            Assert.That(cache.IsFresh(1500, DateTimeOffset.UtcNow), Is.False, "a date in the future isn't trusted");
            cache.Refresh([1500], fetcher, DateTimeOffset.UtcNow);
            Assert.That(fetcher.Calls.Count, Is.EqualTo(1));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private sealed class FakeFetcher(Dictionary<int, List<NexusRequirement>> answer) : INexusRequirementsFetcher
    {
        public List<IReadOnlyCollection<int>> Calls { get; } = [];

        public Dictionary<int, List<NexusRequirement>>? Fetch(IReadOnlyCollection<int> modIds)
        {
            Calls.Add(modIds);
            return answer;
        }
    }

    [Test]
    public void ShouldAddNexusRequirementsAndSkipTheInstallerItself()
    {
        var fetcher = new FakeFetcher(new Dictionary<int, List<NexusRequirement>>
        {
            [1500] =
            [
                new NexusRequirement(78, "Mods of Mistria Installer", "", "", false, 6685),
                new NexusRequirement(669, "March Expanded - A Tsundere Dialogue Overhaul", "", "for the bath scene", false, 6685),
                new NexusRequirement(0, "Some tool", "https://example.com/tool", "", true, 0),
            ],
        });
        var pack = ReplacePack("x.reskin", "Reskin", Beach);
        var me = new MockMod(new Dictionary<string, object> { ["fiddle/npcs/march.toml"] = "x = 1" }) { Id = "caterpixie.march_expanded", Name = "March Expanded" };
        PortraitPackSource? SourceOf(IMod mod) => mod.GetId() switch
        {
            "x.reskin" => new PortraitPackSource(1500, "https://www.nexusmods.com/fieldsofmistria/mods/1500"),
            "caterpixie.march_expanded" => new PortraitPackSource(669, "https://www.nexusmods.com/fieldsofmistria/mods/669"),
            _ => null,
        };

        var cache = new PortraitRequirementsCache(null);
        var result = new PortraitWardrobeBuilder(Vanilla(Beach), SourceOf, cache, fetcher)
            .Build([me, pack, Framework()], "AIM test", DateTimeOffset.UnixEpoch);
        var packJson = Registry(result)["packs"]![0]!;
        var reqs = (JArray)packJson["requirements"]!;

        Assert.Multiple(() =>
        {
            Assert.That((string?)packJson["url"], Is.EqualTo("https://www.nexusmods.com/fieldsofmistria/mods/1500"));
            Assert.That(reqs.Count, Is.EqualTo(2), "the installer itself is never listed");
            Assert.That((string?)reqs[0]["url"], Is.EqualTo("https://www.nexusmods.com/fieldsofmistria/mods/669"));
            Assert.That((bool?)reqs[0]["installed"], Is.True, "March Expanded is in this install (by its Nexus id)");
            Assert.That((string?)reqs[0]["notes"], Is.EqualTo("for the bath scene"));
            Assert.That((string?)reqs[1]["url"], Is.EqualTo("https://example.com/tool"));
            Assert.That((bool?)reqs[1]["installed"], Is.False, "external requirements can't be checked");
            Assert.That(fetcher.Calls.Single(), Is.EquivalentTo(new[] { 1500 }), "only portrait packs are asked about");
        });
    }

    [Test]
    public void ShouldNotAskNexusAgainWhileTheCacheIsFresh()
    {
        var fetcher = new FakeFetcher(new Dictionary<int, List<NexusRequirement>> { [1500] = [] });
        var cache = new PortraitRequirementsCache(null);
        PortraitPackSource? SourceOf(IMod mod) => new(1500, "https://www.nexusmods.com/fieldsofmistria/mods/1500");
        var builder = new PortraitWardrobeBuilder(Vanilla(Beach), SourceOf, cache, fetcher);

        builder.Build([ReplacePack("x.a", "A", Beach), Framework()], "AIM", DateTimeOffset.UnixEpoch);
        builder.Build([ReplacePack("x.a", "A", Beach), Framework()], "AIM", DateTimeOffset.UnixEpoch.AddDays(1));
        builder.Build([ReplacePack("x.a", "A", Beach), Framework()], "AIM", DateTimeOffset.UnixEpoch.AddDays(5));

        Assert.That(fetcher.Calls.Count, Is.EqualTo(2));
    }

    [Test]
    public void ShouldKeepWorkingOfflineWithWhatItKnew()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pw_cache_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var online = new PortraitRequirementsCache(dir);
            online.Refresh([1500], new FakeFetcher(new Dictionary<int, List<NexusRequirement>>
            {
                [1500] = [new NexusRequirement(669, "March Expanded", "", "", false, 6685)],
            }), DateTimeOffset.UnixEpoch);

            var reloaded = new PortraitRequirementsCache(dir);
            reloaded.Refresh([1500], new OfflineFetcher(), DateTimeOffset.UnixEpoch.AddDays(30));

            Assert.That(reloaded.Get(1500)?.Single().ModName, Is.EqualTo("March Expanded"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private sealed class OfflineFetcher : INexusRequirementsFetcher
    {
        public Dictionary<int, List<NexusRequirement>>? Fetch(IReadOnlyCollection<int> modIds) => null;
    }

    // ── end to end through the real installers ──

    [Test]
    public void ShouldInstallTheKeptPortraitsAsOrdinarySprites()
    {
        var result = Build(Vanilla(Bath),
            ReplacePack("someone.nudity", "Nudity", Bath),
            ReplacePack("toad2toad.bush", "Bush", Bath),
            Framework());

        var modifier = new MockFileModifier(new Dictionary<string, string>());
        var atlas = new AtlasUtilities("", modifier);
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var info = new Garethp.ModsOfMistriaInstallerLib.Collector.TOMLCollector().Collect(result.VariantMod);
        new ImageInstaller(mapping, atlas, modifier).Install(result.VariantMod, info, (_, _) => { });
        new TOMLInstaller(mapping, modifier).Install(result.VariantMod, info, (_, _) => { });
        atlas.Flush();

        const string metaPath = "assets/animations/PortraitWardrobe/march/spr_pw_p1_portrait_march_beach_bath_neutral.meta.toml";
        var meta = TomlSerializer.Deserialize<TomlTable>(modifier.GetFile(metaPath))!;
        var id = (string)((TomlTable)meta["meta_properties"])["id"];

        Assert.Multiple(() =>
        {
            Assert.That(id, Is.Not.Empty, "the installer gave it an id");
            Assert.That(atlas.ExtractFrame(id), Is.Not.Null, "and packed it");
            Assert.That(modifier.Exists("assets/animations/PortraitWardrobe/march/spr_pw_v_portrait_march_beach_bath_neutral.meta.toml"), Is.True);
            Assert.That(((TomlTable)meta["asset_properties"])["atlas"], Is.EqualTo("PortraitsSummer"));
        });
    }
}

[TestFixture]
public class NexusRequirementsApiTest
{
    // A real response from api.nexusmods.com/v2/graphql (2026-10-10), trimmed to two mods.
    private const string Response = """
        {"data":{"legacyModsByDomain":{"nodes":[
          {"modId":1208,"name":"Alternative - Balor (MOMI)","uploader":{"name":"cursedcure"},"modRequirements":{"nexusRequirements":{"nodes":[{"modId":"78","modName":"Mods of Mistria Installer","url":"","notes":"","externalRequirement":false,"gameId":"6685"}]}}},
          {"modId":669,"name":"March Expanded - A Tsundere Dialogue Overhaul","uploader":{"name":"caterpixie"},"modRequirements":{"nexusRequirements":{"nodes":[]}}}
        ]}}}
        """;

    [Test]
    public void ShouldParseTheGraphQlResponse()
    {
        var parsed = NexusRequirementsApi.Parse(Response);

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Keys, Is.EquivalentTo(new[] { 1208, 669 }));
            Assert.That(parsed[1208].Single().ModId, Is.EqualTo(78));
            Assert.That(parsed[1208].Single().GameId, Is.EqualTo(6685));
            Assert.That(parsed[669], Is.Empty);
        });
    }

    [Test]
    public void ShouldTreatGarbageAsNoAnswer()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NexusRequirementsApi.Parse("<html>bot check</html>"), Is.Empty);
            Assert.That(NexusRequirementsApi.Parse("{\"errors\":[{\"message\":\"x\"}]}"), Is.Empty);
        });
    }

    [Test]
    public void ShouldBatchEveryIdIntoOneQuery()
    {
        var body = JObject.Parse(NexusRequirementsApi.BuildQuery([669, 1208, 669]));
        var query = (string)body["query"]!;

        Assert.Multiple(() =>
        {
            Assert.That(query, Does.Contain("{gameDomain: \"fieldsofmistria\", modId: 669}, {gameDomain: \"fieldsofmistria\", modId: 1208}"));
            Assert.That(query, Does.Contain("nexusRequirements { nodes { modId modName url notes externalRequirement gameId } }"));
        });
    }

    [Test]
    public void ShouldLinkToTheRequirementsPage()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NexusRequirementsApi.LinkFor(new NexusRequirement(669, "ME", "", "", false, 6685)),
                Is.EqualTo("https://www.nexusmods.com/fieldsofmistria/mods/669"));
            Assert.That(NexusRequirementsApi.LinkFor(new NexusRequirement(0, "Tool", "https://example.com", "", true, 0)),
                Is.EqualTo("https://example.com"));
        });
    }
}
