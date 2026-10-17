using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Garethp.ModsOfMistriaInstallerLib.Nexus;

/// <summary>Another mod manager that can receive nxm:// links, and how it expects to be handed one.</summary>
/// <param name="Name">What a person calls it, and the key its location is remembered under.</param>
/// <param name="ProcessName">The process name without ".exe", used to tell whether it is open.</param>
/// <param name="ArgumentTemplate">Its command-line arguments, with %1 standing for the link.</param>
/// <param name="DefaultExecutablePath">Where its installer puts it, when that is predictable.</param>
public sealed record NxmLinkManager(
    string Name,
    string ProcessName,
    string ArgumentTemplate,
    string? DefaultExecutablePath = null);

/// <summary>A mod manager that is open right now, and when it was opened.</summary>
public sealed record NxmRunningManager(string Name, DateTime StartedAt);

/// <summary>Where a clicked link should go. <see cref="ManagerName"/> is null when AIM keeps it.</summary>
public sealed record NxmRouteDecision(string? ManagerName, string Reason)
{
    public bool HandleHere => ManagerName is null;
}

/// <summary>
/// Lets several mod managers share the "Mod Manager Download" button on the Nexus website.
///
/// Windows stores one protected default app for nxm:// links, and only the user can change it in
/// Settings. A manager's own "handle these links" switch can therefore stop working the moment the
/// user has picked a default, which is why handing links between AIM, Vortex and Stardrop breaks.
///
/// With link sharing on, AIM stays the default app and passes each link on: to whichever manager
/// was opened most recently, or, when none is open, to the manager that received the last link.
/// AIM keeps the link itself when it is that manager. Forwarding happens in the short-lived process
/// Windows starts for the link, before any window is built, so the user never sees AIM open.
/// </summary>
public static class NxmLinkRouter
{
    public const string AimName = "AIM";
    private const string AimProcessName = "AIM";
    private const string ProtocolCommandKeyPath = @"Software\Classes\nxm\shell\open\command";

    /// <summary>Managers AIM can hand links to. Argument formats match what each one registers itself.</summary>
    public static IReadOnlyList<NxmLinkManager> KnownManagers { get; } =
    [
        new("Vortex", "Vortex", "-d \"%1\"",
            @"C:\Program Files\Black Tree Gaming Ltd\Vortex\Vortex.exe"),
        new("Stardrop", "Stardrop", "--nxm \"%1\""),
    ];

    /// <summary>
    /// Chooses who receives a link. Kept free of process and registry access so it can be tested.
    /// </summary>
    /// <param name="running">Managers open right now, AIM included (but not the process deciding).</param>
    /// <param name="lastManager">The manager that received the previous link, if any.</param>
    /// <param name="canLaunch">Whether AIM knows where a manager's executable is.</param>
    public static NxmRouteDecision Decide(
        IEnumerable<NxmRunningManager> running, string? lastManager, Func<string, bool> canLaunch)
    {
        var mostRecent = running
            .Where(m => IsAim(m.Name) || canLaunch(m.Name))
            .OrderByDescending(m => m.StartedAt)
            .FirstOrDefault();

        if (mostRecent is not null)
        {
            return IsAim(mostRecent.Name)
                ? new NxmRouteDecision(null, "AIM was opened most recently")
                : new NxmRouteDecision(mostRecent.Name, $"{mostRecent.Name} was opened most recently");
        }

        if (!string.IsNullOrEmpty(lastManager) && !IsAim(lastManager) && canLaunch(lastManager))
            return new NxmRouteDecision(lastManager, $"no mod manager is open; {lastManager} received the last link");

        return new NxmRouteDecision(null, "no other mod manager is open");
    }

    /// <summary>
    /// Builds the command line for a manager, quoting nothing beyond what its template already
    /// quotes. A template without %1 gets the link appended, quoted.
    /// </summary>
    public static string BuildArguments(string argumentTemplate, string link) =>
        argumentTemplate.Contains("%1", StringComparison.Ordinal)
            ? argumentTemplate.Replace("%1", link, StringComparison.Ordinal)
            : $"{argumentTemplate} \"{link}\"".Trim();

    /// <summary>
    /// Called by the process Windows starts for a clicked link. Returns true when the link was handed
    /// to another manager, in which case this process should exit. Any failure returns false so that
    /// AIM handles the link as it always has: sharing must never be the reason a download is lost.
    /// </summary>
    public static bool TryForwardToOtherManager(string link, NexusSettings? settings = null)
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            settings ??= new NexusSettings();
            if (!settings.ShareLinksWithOtherManagers) return false;

            LearnFromProtocolRegistration(settings);
            var running = FindRunningManagers(settings);

            var decision = Decide(running, settings.LastLinkManager,
                name => FindExecutable(name, settings) is not null);

            Logger.Log($"nxm link sharing: {decision.Reason}");

            if (decision.HandleHere)
            {
                settings.LastLinkManager = AimName;
                return false;
            }

            var manager = KnownManagers.First(m => m.Name == decision.ManagerName);
            var executable = FindExecutable(manager.Name, settings)!;

            Process.Start(new ProcessStartInfo(executable, BuildArguments(manager.ArgumentTemplate, link))
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? ""
            })?.Dispose();

            settings.LastLinkManager = manager.Name;
            return true;
        }
        catch (Exception e)
        {
            Logger.Log($"nxm link sharing failed, AIM will handle the link: {e.Message}");
            return false;
        }
    }

    /// <summary>The manager's executable if it can be found on disk, else null.</summary>
    private static string? FindExecutable(string name, NexusSettings settings)
    {
        var manager = KnownManagers.FirstOrDefault(m => m.Name == name);
        if (manager is null) return null;

        var remembered = settings.GetLinkManagerPath(name);
        if (!string.IsNullOrEmpty(remembered) && File.Exists(remembered)) return remembered;

        return manager.DefaultExecutablePath is { } fallback && File.Exists(fallback) ? fallback : null;
    }

    /// <summary>
    /// Lists the known managers that are open, each with the time its earliest process started (a
    /// manager such as Vortex runs several). AIM's own processes count, apart from this one.
    /// Executable locations seen along the way are remembered for when the manager is closed.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static List<NxmRunningManager> FindRunningManagers(NexusSettings settings)
    {
        var result = new List<NxmRunningManager>();
        var currentProcessId = Environment.ProcessId;

        var candidates = KnownManagers
            .Select(m => (m.Name, m.ProcessName))
            .Append((AimName, AimProcessName));

        foreach (var (name, processName) in candidates)
        {
            DateTime? earliest = null;

            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    if (process.Id == currentProcessId) continue;

                    try
                    {
                        var started = process.StartTime;
                        if (earliest is null || started < earliest) earliest = started;
                    }
                    catch
                    {
                        // An elevated process hides its start time. It is still open, so it counts,
                        // but as the oldest possible: AIM cannot tell when it was opened.
                        earliest ??= DateTime.MinValue;
                    }

                    if (IsAim(name)) continue;

                    try
                    {
                        var path = process.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(path) && File.Exists(path)
                            && !string.Equals(settings.GetLinkManagerPath(name), path, StringComparison.OrdinalIgnoreCase))
                        {
                            settings.SetLinkManagerPath(name, path);
                        }
                    }
                    catch
                    {
                        // Elevated processes hide their path too; the remembered one is used instead.
                    }
                }
            }

            if (earliest is not null) result.Add(new NxmRunningManager(name, earliest.Value));
        }

        return result;
    }

    /// <summary>
    /// Vortex and Stardrop still write their command to the legacy nxm:// key when their own switch
    /// is turned on. Windows ignores that key once AIM is the default app, but it records where the
    /// manager is installed, which is how AIM finds a manager that is not open.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static void LearnFromProtocolRegistration(NexusSettings settings)
    {
        using var key = Registry.CurrentUser.OpenSubKey(ProtocolCommandKeyPath);
        if (key?.GetValue("") is not string command) return;

        var executable = ExtractExecutable(command);
        if (string.IsNullOrEmpty(executable) || !File.Exists(executable)) return;

        var processName = Path.GetFileNameWithoutExtension(executable);
        var manager = KnownManagers.FirstOrDefault(m =>
            string.Equals(m.ProcessName, processName, StringComparison.OrdinalIgnoreCase));

        if (manager is not null
            && !string.Equals(settings.GetLinkManagerPath(manager.Name), executable, StringComparison.OrdinalIgnoreCase))
        {
            settings.SetLinkManagerPath(manager.Name, executable);
        }
    }

    internal static string ExtractExecutable(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            var closing = command.IndexOf('"', 1);
            return closing > 1 ? command[1..closing] : command[1..];
        }

        return command.Split(' ', 2)[0];
    }

    private static bool IsAim(string name) => string.Equals(name, AimName, StringComparison.OrdinalIgnoreCase);
}
