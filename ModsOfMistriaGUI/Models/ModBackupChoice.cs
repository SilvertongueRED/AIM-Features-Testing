using CommunityToolkit.Mvvm.Input;
using Garethp.ModsOfMistriaGUI.Services;
using Garethp.ModsOfMistriaInstallerLib.Nexus;

namespace Garethp.ModsOfMistriaGUI.Models;

/// <summary>
/// One archived copy of a mod, offered in the row's version dropdown.
///
/// The command travels with the choice rather than being reached through the row. A flyout's items
/// take their DataContext from the item, not from the row that opened it, so an item template has
/// no path back to the page view model - the same reason <see cref="ModRowCommands"/> exists.
/// </summary>
/// <param name="ShowChangelog">
/// Opens the mod's release notes at this copy's version, so "what did I have, and what changed
/// since?" is answered from the same place the rollback is chosen. Null where the dropdown is shown
/// without one.
/// </param>
public sealed record ModBackupChoice(
    ModModel Mod,
    ModBackup Backup,
    IRelayCommand<ModBackupChoice> Restore,
    IRelayCommand<ModBackupChoice>? ShowChangelog = null)
{
    /// <summary>Version and date, e.g. "1.2.0 (14/03/2026 09:12)".</summary>
    public string Label => Backup.Describe();

    /// <summary>
    /// The version this copy holds, as the row shows versions - "v2.0.10" - or a plain "unknown
    /// version" when neither the label nor the mod's own manifest said.
    ///
    /// The dropdown used to show only the date for most restore points, because only an update
    /// downloaded from Nexus wrote the version into the label. Three dates and no version is a
    /// choice nobody can make.
    /// </summary>
    public string VersionText => string.IsNullOrWhiteSpace(Backup.Version)
        ? LocalizedTexts.Instance.GUIVersionsUnknownVersion
        : Backup.Version.StartsWith('v') || Backup.Version.StartsWith('V')
            ? Backup.Version
            : $"v{Backup.Version}";

    /// <summary>When the copy was taken.</summary>
    public string When => Backup.When;

    /// <summary>Why it was taken, when it was not simply an update - "before AIM's fix", "replaced".</summary>
    public string Note => Backup.Note;

    public bool HasNote => !string.IsNullOrWhiteSpace(Backup.Note);

    /// <summary>
    /// Only for mods AIM can identify on Nexus, exactly like the row's own icon: an icon that can
    /// only open "no release notes" is worse than none.
    /// </summary>
    public bool CanShowChangelog => ShowChangelog is not null && Mod.CanShowChangelog;

    public string ChangelogTooltip => string.IsNullOrWhiteSpace(Backup.Version)
        ? LocalizedTexts.Instance.GUIVersionsChangelogTooltipNoVersion
        : string.Format(LocalizedTexts.Instance.GUIVersionsChangelogTooltip, Backup.Version);
}
