using Garethp.ModsOfMistriaInstallerLib.ModTypes;
using Garethp.ModsOfMistriaInstallerLib.Utils;

namespace Garethp.ModsOfMistriaInstallerLib.Installer;

// Installs FMOD Studio sound banks shipped by a mod under audio/.
//
// The game loads its music and sound effects from assets/audio/*.bank. A bank is one
// self-contained FMOD file (event data plus the compressed audio), so the only safe way to
// change it is to swap the whole file: audio/Fall.bank in a mod replaces assets/audio/Fall.bank.
//
// Guard rails, because a bad bank silences the game rather than failing loudly:
//   • only banks the game already has are replaced (a typo'd name would otherwise sit unused);
//   • the file must be a real FMOD bank (RIFF container, "FEV " form type);
//   • only files directly inside audio/ count, so a mod can keep sources in sub-folders.
public sealed class AudioInstaller(IFileModifier fileModifier)
{
    private const string ModFolder = "audio/";

    public void Install(IMod mod, Action<string, string> reportStatus)
    {
        foreach (var sourcePath in mod.GetAllFiles(".bank"))
        {
            var relativePath = GetRelativePath(mod, sourcePath).Replace('\\', '/');
            if (!relativePath.StartsWith(ModFolder, StringComparison.OrdinalIgnoreCase))
                continue;

            var fileName = relativePath[ModFolder.Length..];
            if (fileName.Length == 0 || fileName.Contains('/'))
                continue;

            var destination = FindGameBank(fileName);
            if (destination is null)
            {
                reportStatus($"Skipped audio bank {fileName}: the game has no bank with that name.", "");
                continue;
            }

            using var source = mod.ReadFileAsStream(relativePath);
            using var buffer = new MemoryStream();
            source.CopyTo(buffer);
            var bytes = buffer.ToArray();

            if (!IsFmodBank(bytes))
            {
                reportStatus($"Skipped audio bank {fileName}: it is not an FMOD sound bank.", "");
                continue;
            }

            fileModifier.Write(destination, bytes);
            reportStatus($"Installed audio bank: {fileName}", "");
        }
    }

    // The archive lookup is case-sensitive, so a mod shipping "fall.bank" would otherwise miss
    // the game's "Fall.bank". Prefer the exact spelling, then fall back to a case-insensitive
    // match and write to the game's own spelling so FMOD finds it.
    private string? FindGameBank(string fileName)
    {
        var exact = Path.Combine("assets", "audio", fileName);
        if (fileModifier.Exists(exact)) return exact;

        string[] candidates;
        try
        {
            candidates = fileModifier.FindFiles("assets/audio/", ".bank");
        }
        catch (IOException)
        {
            // The loose-files modifier throws when assets/audio does not exist.
            return null;
        }

        return candidates
            .Select(path => path.Replace('\\', '/'))
            .Where(path => path.EndsWith("/audio/" + path[(path.LastIndexOf('/') + 1)..], StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(path => string.Equals(
                path[(path.LastIndexOf('/') + 1)..], fileName, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsFmodBank(byte[] bytes) =>
        bytes.Length >= 12 &&
        bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F' &&
        bytes[8] == (byte)'F' && bytes[9] == (byte)'E' && bytes[10] == (byte)'V' && bytes[11] == (byte)' ';

    private static string GetRelativePath(IMod mod, string path)
    {
        var normalizedPath = path.Replace('\\', '/');
        var basePath = mod.GetBasePath().Replace('\\', '/').TrimEnd('/');
        if (!string.IsNullOrEmpty(basePath) &&
            normalizedPath.StartsWith(basePath + '/', StringComparison.OrdinalIgnoreCase))
        {
            return normalizedPath[(basePath.Length + 1)..];
        }

        return normalizedPath.TrimStart('/');
    }
}
