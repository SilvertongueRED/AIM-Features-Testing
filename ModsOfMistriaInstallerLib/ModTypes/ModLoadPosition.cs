namespace Garethp.ModsOfMistriaInstallerLib.ModTypes;

/// <summary>
/// Where a mod asks to sit inside its own load-order layer (manifest <c>load_position</c>).
///
/// The layers themselves - frameworks, code, new content, data overrides, replacements - are
/// decided by what a mod installs (see <see cref="Garethp.ModsOfMistriaInstallerLib.ModRoleClassifier"/>),
/// and no manifest can move a mod out of its layer. This only settles where it goes among mods of
/// the same kind, which the planner otherwise leaves entirely to the user.
///
/// The case it exists for is a broad recolour or retexture. It replaces hundreds of files, and every
/// narrower mod of its kind - one building's recolour, a heart icon, a toolbar - should load after it
/// and win the files they share. The broad mod marks itself <c>early</c>; nobody else has to know it
/// exists.
/// </summary>
public enum ModLoadPosition
{
    /// <summary>No preference: the user's order inside the layer stands.</summary>
    Default,

    /// <summary>A base for its kind: loads before the other mods in its layer.</summary>
    Early,

    /// <summary>A patch for its kind: loads after the other mods in its layer.</summary>
    Late
}

public static class ModLoadPositions
{
    /// <summary>
    /// Reads the manifest value. Anything unrecognised - a typo, a value only a newer AIM knows - is
    /// no preference rather than an error: a load-order hint is never worth refusing a mod over.
    /// </summary>
    public static ModLoadPosition Parse(object? value) =>
        value?.ToString()?.Trim().ToLowerInvariant() switch
        {
            "early" or "first" => ModLoadPosition.Early,
            "late" or "last" => ModLoadPosition.Late,
            _ => ModLoadPosition.Default
        };

    /// <summary>Sort key inside a layer: early mods first, late mods last.</summary>
    public static int Rank(ModLoadPosition position) => position switch
    {
        ModLoadPosition.Early => 0,
        ModLoadPosition.Late => 2,
        _ => 1
    };
}
