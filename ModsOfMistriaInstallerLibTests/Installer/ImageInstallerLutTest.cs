using Garethp.ModsOfMistriaInstallerLib;
using Garethp.ModsOfMistriaInstallerLib.Installer;
using Garethp.ModsOfMistriaInstallerLib.Models.SDK;
using Garethp.ModsOfMistriaInstallerLib.Utils;
using ModsOfMistriaInstallerLibTests.Fixtures;
using ModsOfMistriaInstallerLibTests.TestUtils;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Tomlyn;

namespace ModsOfMistriaInstallerLibTests.Installer;

// LUT replacements. The game picks a palette by column index, so a mod adding colour variants
// appends columns (the default), while a recolour has to overwrite the columns already there
// (images/replace/*_lut.meta.toml: lut_mode = "overwrite").
[TestFixture]
public class ImageInstallerLutTest
{
    private const string GameMetaPath = "assets/animations/UI NEW/spr_ui_test_lut.meta.toml";
    private const string ModPng = "images/replace/spr_ui_test_lut.png";
    private const string ModMeta = "images/replace/spr_ui_test_lut.meta.toml";

    private static readonly Rgba32 Red = new(255, 0, 0, 255);
    private static readonly Rgba32 Green = new(0, 255, 0, 255);
    private static readonly Rgba32 Blue = new(0, 0, 255, 255);
    private static readonly Rgba32 White = new(255, 255, 255, 255);

    private const string Overwrite = """
        [asset_properties]
        lut_mode = "overwrite"
        """;

    [Test]
    public void ShouldAppendANewPaletteByDefault()
    {
        var (installed, _, _) = Install([Red, Green], [Blue], modMeta: null);

        Assert.That(Columns(installed), Is.EqualTo(new[] { Red, Green, Blue }));
    }

    [Test]
    public void ShouldOverwriteThePalettesTheGameIndexesWhenTheModAsks()
    {
        var (installed, statuses, _) = Install([Red, Green], [Blue, White], Overwrite);

        Assert.Multiple(() =>
        {
            Assert.That(Columns(installed), Is.EqualTo(new[] { Blue, White }));
            Assert.That(statuses, Has.Some.Contains("Overwrote LUT"));
        });
    }

    [Test]
    public void ShouldKeepColumnsPastTheRecoloursWidth()
    {
        // Another mod appended a third palette earlier; a two-column recolour must not drop it.
        var (installed, _, _) = Install([Red, Green, White], [Blue], Overwrite);

        Assert.That(Columns(installed), Is.EqualTo(new[] { Blue, Green, White }));
    }

    [Test]
    public void ShouldNeverWriteTheLutModeIntoTheGamesOwnMeta()
    {
        var (_, _, gameMeta) = Install([Red], [Blue], Overwrite);

        Assert.Multiple(() =>
        {
            Assert.That(gameMeta, Does.Not.Contain("lut_mode"));
            Assert.That(TomlSerializer.Deserialize<SpriteMetaFile>(gameMeta)?.Asset?.LutMode, Is.Null);
        });
    }

    [Test]
    public void ShouldReadTheLutModeFromAModMeta()
    {
        var meta = TomlSerializer.Deserialize<SpriteMetaFile>(Overwrite);
        var game = new SpriteMetaFile { Asset = new SpriteMetaFileAssetProperties() };
        game.Merge(meta);

        Assert.Multiple(() =>
        {
            Assert.That(meta?.Asset?.LutMode, Is.EqualTo("overwrite"));
            Assert.That(game.Asset?.LutMode, Is.Null, "the mode belongs to the mod, not to the merged game meta");
        });
    }

    [Test]
    public void LutMergerOverwriteRefusesADifferentHeight()
    {
        using var existing = new Image<Rgba32>(2, 4);
        using var incoming = new Image<Rgba32>(2, 8);

        Assert.That(LutMerger.Overwrite(existing, incoming), Is.Null);
    }

    // Packs a game LUT made of the given solid columns, installs a mod LUT over it and returns
    // what ended up in the atlas, the installer's statuses, and the rewritten game meta.
    private static (Image<Rgba32>? Installed, List<string> Statuses, string GameMeta) Install(
        Rgba32[] game, Rgba32[] mod, string? modMeta)
    {
        var modifier = new MockFileModifier(new Dictionary<string, string>());
        var atlas = new AtlasUtilities("", modifier);
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var id = atlas.AddStrip("Default", game.Length, 4, 1, new MemoryStream(Lut(game)), mapping, "ui_test_lut");
        modifier.Write(GameMetaPath, $"""
            [meta_properties]
            id = "{id}"
            asset_kind = "Animation"

            [asset_properties]
            frame_size = [{game.Length}, 4]
            atlas = "Default"
            """);

        var files = new Dictionary<string, object> { [ModPng] = Lut(mod) };
        if (modMeta is not null) files[ModMeta] = modMeta;

        var statuses = new List<string>();
        new ImageInstaller(mapping, atlas, modifier)
            .Install(new MockMod(files), new GeneratedInformation(), (status, _) => statuses.Add(status));

        return (atlas.ExtractFrame(id), statuses, modifier.GetFile(GameMetaPath));
    }

    private static byte[] Lut(Rgba32[] columns)
    {
        using var image = new Image<Rgba32>(columns.Length, 4);
        for (var x = 0; x < columns.Length; x++)
            for (var y = 0; y < 4; y++)
                image[x, y] = columns[x];

        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private static Rgba32[] Columns(Image<Rgba32>? image)
    {
        Assert.That(image, Is.Not.Null, "the LUT should still be in the atlas");
        return Enumerable.Range(0, image!.Width).Select(x => image[x, 0]).ToArray();
    }
}
