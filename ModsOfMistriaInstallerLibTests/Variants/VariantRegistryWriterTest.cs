using Garethp.ModsOfMistriaInstallerLib.Tools;
using Garethp.ModsOfMistriaInstallerLib.Variants;
using ModsOfMistriaInstallerLibTests.Fixtures;
using SixLabors.ImageSharp.PixelFormats;
using static ModsOfMistriaInstallerLibTests.Variants.VariantTestGame;

namespace ModsOfMistriaInstallerLibTests.Variants;

// The registry is GML the framework's runtime executes, built from text players and mod authors
// control (mod names, authors). A stray quote would break the install's compile check, or worse,
// change what the runtime reads.
[TestFixture]
public class VariantRegistryWriterTest
{
    private const string HostileName = "Witchy \"Dark\" Lamps \\ v2\nline two\t// not a comment";

    [TestCase("plain", "\"plain\"")]
    [TestCase("", "\"\"")]
    [TestCase(null, "\"\"")]
    [TestCase("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [TestCase("back\\slash", "\"back\\\\slash\"")]
    [TestCase("line\nbreak\r\ttab\u0001", "\"linebreaktab\"")]
    [TestCase("ünïcödé 名前", "\"ünïcödé 名前\"")]
    public void ShouldEscapeGmlStringLiterals(string? value, string expected)
    {
        Assert.That(VariantRegistryWriter.Str(value), Is.EqualTo(expected));
    }

    private static (VariantSession Session, PristineAssets Pristine) Session()
    {
        var pristine = WithLamp().Build();
        var framework = Framework();
        var witchy = ReplacingMod("alice.witchy_lamps", HostileName, new Rgba32(1, 2, 3, 255), LampSprite);
        var pastel = ReplacingMod("bob.pastel_lamps", "Pastel Lamps", new Rgba32(4, 5, 6, 255), LampSprite);
        var plan = VariantPlanner.Plan([framework, witchy, pastel], framework, pristine, FamilyIndex.Build(pristine),
            new VariantPreferences());
        plan.Warnings.Add("a warning with \"quotes\" and a \\ backslash");
        var (items, animals) = VariantContentPlanner.Plan(plan, pristine);

        var history = new VariantHistory();
        history.Remember([new ItemVariant("furniture:old", "lkgone1234", "old", "old", "old_lkgone1234", "old_lkgone1234")], []);
        return (VariantSession.Create(plan, pristine, history, null, items, animals, retireOnly: false), pristine);
    }

    private static string Render(VariantSession session) =>
        VariantRegistryWriter.Render(session, "1.2.3", new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero));

    [Test]
    public void ShouldRenderTheWholePlan()
    {
        var (session, pristine) = Session();
        using var _ = pristine;
        var gml = Render(session);

        Assert.Multiple(() =>
        {
            Assert.That(gml, Does.Contain($"function {VariantFramework.RegistryFunction}() {{"));
            Assert.That(gml, Does.Contain("installer: \"1.2.3\""));
            Assert.That(gml, Does.Contain("generated: \"2026-10-11T12:00:00Z\""));
            Assert.That(gml, Does.Contain("id: \"furniture:test_lamp\""));
            Assert.That(gml, Does.Contain("kind: \"furniture\""));
            Assert.That(gml, Does.Contain($"default_look: \"{VariantFramework.LookIdForMod("bob.pastel_lamps")}\""));
            Assert.That(gml, Does.Contain(VariantRegistryWriter.Str(HostileName)));
            Assert.That(gml, Does.Contain($"item: \"test_lamp_{VariantFramework.LookIdForMod("alice.witchy_lamps")}\""));
            Assert.That(gml, Does.Contain("{ item: \"old_lkgone1234\", object: \"old_lkgone1234\""));
            Assert.That(gml, Does.Contain($"\"{LampSprite}\", \"{LampSprite}_vanilla\""));
            Assert.That(gml, Does.Contain("stats: { families: 1, install_only: 0, look_sprites: 1, original_copies: 1, variant_items: 1, animal_variants: 0, warnings: 1 }"));
        });
    }

    [Test]
    public void ShouldRenderSyntacticallySaneGml()
    {
        var (session, pristine) = Session();
        using var _ = pristine;
        var gml = Render(session);

        // Walk the text the way a lexer would: every string closes on its own line, and outside
        // strings the brackets balance and nest properly.
        var stack = new Stack<char>();
        foreach (var line in gml.Split('\n'))
        {
            var inString = false;
            var code = new System.Text.StringBuilder(); // the line with string contents removed
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (inString)
                {
                    if (c == '\\') i++;
                    else if (c == '"') inString = false;
                    continue;
                }

                if (c == '/' && i + 1 < line.Length && line[i + 1] == '/') break; // comment
                code.Append(c);
                switch (c)
                {
                    case '"': inString = true; break;
                    case '{' or '[' or '(': stack.Push(c); break;
                    case '}': Assert.That(stack.Pop(), Is.EqualTo('{'), line); break;
                    case ']': Assert.That(stack.Pop(), Is.EqualTo('['), line); break;
                    case ')': Assert.That(stack.Pop(), Is.EqualTo('('), line); break;
                }
            }

            Assert.That(inString, Is.False, $"unterminated string: {line}");
            // Every value in a key: value pair is a quoted string, a number, or a nested literal,
            // never a bare word the runtime would read as a variable.
            Assert.That(code.ToString(), Does.Not.Match(@":\s*[A-Za-z_]"), line);
        }

        Assert.That(stack, Is.Empty);
    }

    [Test]
    public void ShouldCompileWithTheGmlChecker()
    {
        var checker = GmlCompileGate.ResolveChecker();
        if (checker is null)
            Assert.Ignore("needs the momi-gml-check binary (cargo build --release --manifest-path "
                          + "tools/checker/Cargo.toml), or set MOMI_GML_CHECKER");

        var (session, pristine) = Session();
        using var _ = pristine;
        var directory = Path.Combine(Path.GetTempPath(), "aim_registry_" + Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "mistria_variants_registry.gml");
            File.WriteAllText(path, Render(session));

            Assert.DoesNotThrow(() => new GmlCompileGate(checker!).RunFiles([path]));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void ShouldNameEveryFamilyKind()
    {
        foreach (var kind in Enum.GetValues<FamilyKind>())
            Assert.That(VariantRegistryWriter.KindName(kind), Is.Not.EqualTo("other"), kind.ToString());
    }
}
