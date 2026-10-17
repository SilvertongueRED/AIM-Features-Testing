using System.IO.Compression;
using System.Text;
using Garethp.ModsOfMistriaInstallerLib.Variants;
using ModsOfMistriaInstallerLibTests.Fixtures;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ModsOfMistriaInstallerLibTests.Variants;

/// <summary>
/// A tiny stand-in for the game's archive, laid out exactly like the real one (fiddle data under
/// <c>assets/fiddle/</c>, sprites as <c>.meta.toml</c> + <c>.png</c> under <c>assets/animations/</c>,
/// collision shapes under <c>assets/shapes/</c>), plus helpers for the mods that compete over it.
/// </summary>
public sealed class VariantTestGame
{
    public const string FurnitureFolder = "Placeables/Furniture/Test";
    public const string IconFolder = "Item Icons/Test";
    public const string LampSprite = "spr_decor_test_lamp_south";
    public const string LampShadow = "spr_decor_test_lamp_shadow";
    public const string LampIcon = "spr_ui_item_decor_test_lamp";

    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

    public VariantTestGame Text(string path, string text)
    {
        Files[path] = Encoding.UTF8.GetBytes(text);
        return this;
    }

    public VariantTestGame Sprite(string folder, string name, int width, int height, Rgba32 color,
        string atlas = "Default", bool withShape = false)
    {
        Text($"assets/animations/{folder}/{name}.meta.toml", SpriteMeta(width, height, atlas, id: VariantFramework.Hash4(name)));
        Files[$"assets/animations/{folder}/{name}.png"] = Png(width, height, color);
        if (withShape)
        {
            Text($"assets/shapes/{folder}/poly_{name["spr_".Length..]}.meta.toml",
                "[meta_properties]\nid = \"shape0001\"\nasset_kind = \"Shape\"\nrequired_assets = [\"x\"]\n\n" +
                "[asset_properties]\nkind = \"box\"\noffset = [-8, -10]\ndimensions = [16, 8]\n");
        }

        return this;
    }

    public PristineAssets Build() => PristineAssets.FromFiles(Files);

    /// <summary>The same files as a real zip, for code that opens the archive by path.</summary>
    public string WriteZip(string directory)
    {
        var path = Path.Combine(directory, "assets.bak.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, bytes) in Files.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            using var stream = zip.CreateEntry(name).Open();
            stream.Write(bytes);
        }

        return path;
    }

    /// <summary>One placeable lamp (object, item, world sprite with shape and shadow, icon).</summary>
    public static VariantTestGame WithLamp()
    {
        var game = new VariantTestGame();
        game.Text("assets/fiddle/object_prototypes/furniture.toml",
            "[test_lamp]\nsize = [2, 2]\n[test_lamp.south]\nsprite = \"" + LampSprite + "\"\noffset = [0, 0]\n");
        game.Text("assets/fiddle/items/furniture/test_set.toml",
            "[test_lamp]\nname = \"Test Lamp\"\nicon_sprite = \"" + LampIcon + "\"\nobject = \"test_lamp\"\n" +
            "recipe_key = \"test_lamp\"\ntags = [\"furniture\", \"test_decor\"]\n");
        game.Text("assets/data_files/animation/shadow_manifest.json",
            "{ \"" + LampSprite + "\": \"" + LampShadow + "\" }");
        game.Sprite(FurnitureFolder, LampSprite, 16, 16, new Rgba32(200, 200, 200, 255), withShape: true);
        game.Sprite(FurnitureFolder, LampShadow, 16, 16, new Rgba32(0, 0, 0, 128));
        game.Sprite(IconFolder, LampIcon, 8, 8, new Rgba32(200, 200, 200, 255), atlas: "UI");
        return game;
    }

    public static string SpriteMeta(int width, int height, string atlas = "Default", string id = "0000", int? frames = null) =>
        "[meta_properties]\n" +
        $"id = \"{id}\"\n" +
        "asset_kind = \"Animation\"\n\n" +
        "[asset_properties]\n" +
        $"frame_size = [{width}, {height}]\n" +
        (frames is { } count ? $"frame_len = {count}\n" : "") +
        $"atlas = \"{atlas}\"\n\n" +
        "[asset_properties.offset]\nhorizontal = 8.0\nvertical = 8.0\n";

    public static byte[] Png(int width, int height, Rgba32 color)
    {
        using var image = new Image<Rgba32>(width, height);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            image[x, y] = color;
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    /// <summary>A mod that replaces <paramref name="sprites"/> through <c>images/replace/</c>.</summary>
    public static MockMod ReplacingMod(string id, string name, Rgba32 color, params string[] sprites)
    {
        var files = new Dictionary<string, object> { ["manifest.json"] = "{}" };
        foreach (var sprite in sprites)
            files[$"images/replace/{sprite}.png"] = Png(16, 16, color);
        return new MockMod(files) { Id = id, Name = name, Author = id.Split('.')[0] };
    }

    public static MockMod Framework() =>
        new(new Dictionary<string, object>
        {
            [VariantFramework.MarkerFile] = "",
            ["manifest.json"] = "{}",
            [VariantFramework.RegistryGmlPath] = "function mistria_variants_registry_data() { return undefined; }\n",
        })
        {
            Id = "mistria.mistria_variants",
            Name = "Variants Framework",
            Author = "mistria",
        };
}
