using System.Text;
using Garethp.ModsOfMistriaInstallerLib;
using Garethp.ModsOfMistriaInstallerLib.Installer;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;
using ModsOfMistriaInstallerLibTests.Fixtures;
using ModsOfMistriaInstallerLibTests.TestUtils;

namespace ModsOfMistriaInstallerLibTests.Installer;

[TestFixture]
public class AudioInstallerTest
{
    // Smallest thing that passes the FMOD bank check: a RIFF header with the "FEV " form type.
    private static byte[] FakeBank(string marker) =>
        Encoding.ASCII.GetBytes("RIFF\0\0\0\0FEV FMT " + marker);

    private static MockFileModifier GameWithBanks() => new(new Dictionary<string, string>
    {
        ["assets/audio/Fall.bank"] = "original-fall",
        ["assets/audio/Master.bank"] = "original-master"
    });

    private static byte[] ReadBytes(MockFileModifier modifier, string path)
    {
        using var stream = modifier.GetReadStream(path);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    [Test]
    public void ReplacesAGameBankWithTheModsBank()
    {
        var bank = FakeBank("spooky");
        var mod = new MockMod(new Dictionary<string, object> { ["audio/Fall.bank"] = bank });
        var modifier = GameWithBanks();
        var reports = new List<string>();

        new AudioInstaller(modifier).Install(mod, (message, _) => reports.Add(message));

        Assert.That(ReadBytes(modifier, "assets/audio/Fall.bank"), Is.EqualTo(bank));
        Assert.That(modifier.Read("assets/audio/Master.bank"), Is.EqualTo("original-master"));
        Assert.That(reports, Has.Some.Contains("Fall.bank"));
    }

    [Test]
    public void MatchesTheGamesBankNameRegardlessOfCase()
    {
        var bank = FakeBank("lowercase");
        var mod = new MockMod(new Dictionary<string, object> { ["audio/fall.bank"] = bank });
        var modifier = GameWithBanks();

        new AudioInstaller(modifier).Install(mod, (_, _) => { });

        Assert.That(ReadBytes(modifier, "assets/audio/Fall.bank"), Is.EqualTo(bank));
        Assert.That(modifier.Exists("assets/audio/fall.bank"), Is.False);
    }

    [Test]
    public void SkipsABankTheGameDoesNotHave()
    {
        var mod = new MockMod(new Dictionary<string, object> { ["audio/Autumn.bank"] = FakeBank("typo") });
        var modifier = GameWithBanks();
        var reports = new List<string>();

        new AudioInstaller(modifier).Install(mod, (message, _) => reports.Add(message));

        Assert.That(modifier.Exists("assets/audio/Autumn.bank"), Is.False);
        Assert.That(reports, Has.Some.Contains("Skipped"));
    }

    [Test]
    public void SkipsAFileThatIsNotAnFmodBank()
    {
        var mod = new MockMod(new Dictionary<string, object> { ["audio/Fall.bank"] = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 } });
        var modifier = GameWithBanks();

        new AudioInstaller(modifier).Install(mod, (_, _) => { });

        Assert.That(modifier.Read("assets/audio/Fall.bank"), Is.EqualTo("original-fall"));
    }

    [Test]
    public void IgnoresBanksOutsideTheTopOfTheAudioFolder()
    {
        var mod = new MockMod(new Dictionary<string, object>
        {
            ["sources/Fall.bank"] = FakeBank("elsewhere"),
            ["audio/originals/Fall.bank"] = FakeBank("nested")
        });
        var modifier = GameWithBanks();

        new AudioInstaller(modifier).Install(mod, (_, _) => { });

        Assert.That(modifier.Read("assets/audio/Fall.bank"), Is.EqualTo("original-fall"));
        Assert.That(modifier.Exists("assets/audio/originals/Fall.bank"), Is.False);
    }

    [Test]
    public void AnAudioModLoadsInTheReplacementLayer()
    {
        var mod = new MockMod(new Dictionary<string, object> { ["audio/Fall.bank"] = FakeBank("spooky") });

        Assert.That(ModRoleClassifier.RoleOf(mod, false), Is.EqualTo(ModRole.Replacement));
    }

    [Test]
    public void TwoModsReplacingTheSameBankAreAHardConflict()
    {
        var first = new MockMod(new Dictionary<string, object> { ["audio/Fall.bank"] = FakeBank("a") }) { Id = "first.mod" };
        var second = new MockMod(new Dictionary<string, object> { ["audio/Fall.bank"] = FakeBank("b") }) { Id = "second.mod" };

        var conflicts = ModFileConflictDetector.Find([first, second]);

        Assert.That(conflicts.Single(c => c.Path.EndsWith("Fall.bank")).Kind, Is.EqualTo(ModFileConflictKind.HardReplacement));
    }
}
