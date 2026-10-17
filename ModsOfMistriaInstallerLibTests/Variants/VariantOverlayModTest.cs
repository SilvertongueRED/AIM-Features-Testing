using System.Text;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;
using Garethp.ModsOfMistriaInstallerLib.Variants;
using ModsOfMistriaInstallerLibTests.Fixtures;

namespace ModsOfMistriaInstallerLibTests.Variants;

// The installers read a mod only through IMod, so the overlay has to answer every listing and
// read the way the real containers do, with the rewrite applied: hidden files are simply not
// there, and generated files look like the mod's own.
[TestFixture]
public class VariantOverlayModTest
{
    private static ModRewrite Rewrite()
    {
        var rewrite = new ModRewrite();
        rewrite.Hidden.Add("images/replace/spr_lamp.png");
        rewrite.Hidden.Add("images/replace/spr_lamp.meta.toml");
        rewrite.Add(new VirtualFile { Path = "animations/Furniture/spr_lamp_lkabc.png", Bytes = () => [1, 2, 3] });
        rewrite.Add(new VirtualFile { Path = "animations/Furniture/spr_lamp_lkabc.meta.toml", Text = "[meta_properties]\n" });
        rewrite.Add(new VirtualFile { Path = "gml/data/registry.gml", Text = "// generated\n" });
        return rewrite;
    }

    private static MockMod Inner() => new(new Dictionary<string, object>
    {
        ["manifest.json"] = "{}",
        ["images/replace/spr_lamp.png"] = new byte[] { 9 },
        ["images/replace/spr_lamp.meta.toml"] = "meta",
        ["images/replace/spr_chair.png"] = new byte[] { 8 },
        ["gml/data/registry.gml"] = "// stub\n",
    }) { Id = "alice.lamps", Name = "Lamps", Author = "alice", Version = "1.2" };

    [Test]
    public void ShouldHideRewrittenFilesFromEveryListing()
    {
        var overlay = new VariantOverlayMod(Inner(), Rewrite());

        Assert.Multiple(() =>
        {
            Assert.That(overlay.FileExists("images/replace/spr_lamp.png"), Is.False);
            Assert.That(overlay.FileExists("images/replace/spr_chair.png"), Is.True);
            Assert.That(overlay.GetFilesInFolder("images/replace", ".png"), Is.EqualTo(new[] { "images/replace/spr_chair.png" }));
            Assert.That(overlay.GetAllFiles(".png"), Does.Not.Contain("images/replace/spr_lamp.png"));
            Assert.That(overlay.GetAllFiles(".toml"), Does.Not.Contain("images/replace/spr_lamp.meta.toml"));
            Assert.That(overlay.ReadFile("images/replace/spr_lamp.meta.toml"), Is.Empty);
            Assert.Throws<FileNotFoundException>(() => overlay.ReadFileAsStream("images/replace/spr_lamp.png"));
        });
    }

    [Test]
    public void ShouldListAndReadAddedFilesAsTheModsOwn()
    {
        var overlay = new VariantOverlayMod(Inner(), Rewrite());

        Assert.Multiple(() =>
        {
            Assert.That(overlay.FileExists("animations/Furniture/spr_lamp_lkabc.png"), Is.True);
            Assert.That(overlay.FolderExists("animations/Furniture"), Is.True);
            Assert.That(overlay.HasFilesInFolder("animations/Furniture", ".png"), Is.True);
            Assert.That(overlay.GetFilesInFolder("animations/Furniture"),
                Is.EquivalentTo(new[] { "animations/Furniture/spr_lamp_lkabc.png", "animations/Furniture/spr_lamp_lkabc.meta.toml" }));
            // Folder listings are not recursive, exactly like the real containers.
            Assert.That(overlay.GetFilesInFolder("animations"), Is.Empty);
            Assert.That(overlay.GetAllFiles(".png"), Does.Contain("animations/Furniture/spr_lamp_lkabc.png"));
            Assert.That(overlay.ReadFile("animations/Furniture/spr_lamp_lkabc.meta.toml"), Is.EqualTo("[meta_properties]\n"));
        });

        using var stream = overlay.ReadFileAsStream("animations/Furniture/spr_lamp_lkabc.png");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        Assert.That(buffer.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));

        using var text = overlay.ReadFileAsStream("animations/Furniture/spr_lamp_lkabc.meta.toml");
        Assert.That(new StreamReader(text).ReadToEnd(), Is.EqualTo("[meta_properties]\n"));
    }

    [Test]
    public void ShouldLetAnAddedFileShadowTheModsOwnCopyOnce()
    {
        // The framework's generated registry replaces the stub it ships at the same path.
        var overlay = new VariantOverlayMod(Inner(), Rewrite());

        Assert.Multiple(() =>
        {
            Assert.That(overlay.ReadFile("gml/data/registry.gml"), Is.EqualTo("// generated\n"));
            Assert.That(overlay.GetAllFiles(".gml"), Is.EqualTo(new[] { "gml/data/registry.gml" }));
            Assert.That(overlay.GetFilesInFolder("gml/data", ".gml"), Is.EqualTo(new[] { "gml/data/registry.gml" }));
        });
    }

    [Test]
    public void ShouldPassEverythingElseThroughToTheMod()
    {
        var inner = Inner();
        var overlay = new VariantOverlayMod(inner, Rewrite());

        Assert.Multiple(() =>
        {
            Assert.That(overlay.GetId(), Is.EqualTo("alice.lamps"));
            Assert.That(overlay.GetName(), Is.EqualTo("Lamps"));
            Assert.That(overlay.GetVersion(), Is.EqualTo("1.2"));
            Assert.That(overlay.GetValidation(), Is.SameAs(inner.GetValidation()));
            Assert.That(overlay.ReadFile("images/replace/spr_chair.png"), Is.EqualTo(inner.ReadFile("images/replace/spr_chair.png")));
            Assert.That(VariantSession.Original(overlay), Is.SameAs(inner));
            Assert.That(VariantSession.Original(inner), Is.SameAs(inner));
        });
    }

    [Test]
    public void ShouldWorkOverAFolderModThatListsAbsolutePaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "aim_overlay_" + Path.GetRandomFileName());
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "images", "replace"));
            File.WriteAllText(Path.Combine(root, "manifest.json"),
                "{ \"name\": \"Lamps\", \"author\": \"alice\", \"version\": \"1.0.0\", \"minInstallerVersion\": \"0.1.0\", \"manifestVersion\": 1 }");
            File.WriteAllBytes(Path.Combine(root, "images", "replace", "spr_lamp.png"), [9]);
            File.WriteAllBytes(Path.Combine(root, "images", "replace", "spr_chair.png"), [8]);
            var mod = FolderMod.FromManifest(root);

            var overlay = new VariantOverlayMod(mod, Rewrite());
            var listed = overlay.GetFilesInFolder("images/replace", ".png");
            var all = overlay.GetAllFiles(".png");

            Assert.Multiple(() =>
            {
                Assert.That(listed.Select(Path.GetFileName), Is.EqualTo(new[] { "spr_chair.png" }));
                Assert.That(all.Select(Path.GetFileName), Is.EquivalentTo(new[] { "spr_chair.png", "spr_lamp_lkabc.png" }));
                // Added files come back under the mod's own base path, like its real files.
                var added = all.Single(p => p.EndsWith("spr_lamp_lkabc.png", StringComparison.Ordinal));
                Assert.That(Path.IsPathRooted(added), Is.True);
                Assert.That(overlay.FileExists(added), Is.True);
                Assert.That(overlay.FileExists(Path.Combine(root, "images", "replace", "spr_lamp.png")), Is.False);
                using var stream = overlay.ReadFileAsStream(added);
                Assert.That(stream.ReadByte(), Is.EqualTo(1));
            });
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Test]
    public void ShouldTreatAnEmptyRewriteAsEmpty()
    {
        Assert.That(new ModRewrite().IsEmpty, Is.True);
        Assert.That(Rewrite().IsEmpty, Is.False);
        Assert.That(Encoding.UTF8.GetString(new VariantOverlayMod(Inner(), new ModRewrite())
            .ReadFileAsStream("images/replace/spr_lamp.meta.toml") is MemoryStream m ? m.ToArray() : []), Is.EqualTo("meta"));
    }
}
