namespace Garethp.ModsOfMistriaGUI.Views;

/// <summary>
/// Shortens an issue's headline when it names a great many mods.
///
/// A cosmetics pile-up can put thirty-odd mod names in one sentence, which pushes every other issue
/// off the screen before the user has opened anything. The names are all still there once the issue
/// is opened (one row per mod), so the headline only has to say who it is about and how many.
/// </summary>
public static class IssueSummary
{
    /// <summary>More mods than this and the headline is shortened.</summary>
    public const int Limit = 6;

    private const int Shown = 3;

    /// <summary>
    /// Replaces the leading list of quoted mod names with the first few and a count. Returns the
    /// message unchanged when it is short, or does not start with exactly that list (an issue
    /// worded some other way is never mangled).
    /// </summary>
    public static string Condense(string message, IReadOnlyList<string> names, string andMore)
    {
        if (names.Count <= Limit) return message;

        var all = string.Join(", ", names.Select(name => $"\"{name}\""));
        if (!message.StartsWith(all, StringComparison.Ordinal)) return message;

        var first = string.Join(", ", names.Take(Shown).Select(name => $"\"{name}\""));
        return string.Format(andMore, first, names.Count - Shown) + message[all.Length..];
    }
}
