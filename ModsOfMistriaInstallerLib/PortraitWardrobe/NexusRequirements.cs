using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Garethp.ModsOfMistriaInstallerLib.PortraitWardrobe;

/// <summary>One "Nexus requirements" entry from a mod page.</summary>
public sealed record NexusRequirement(int ModId, string ModName, string Url, string Notes, bool External, int GameId);

/// <summary>
/// A mod page's requirements, read from Nexus's public GraphQL API (no sign-in needed). Portrait
/// packs tied to another mod's scenes say so there ("Requires: March Expanded"), and the in-game
/// menu shows those, with the link, next to the pack.
/// </summary>
public static class NexusRequirementsApi
{
    public const string Endpoint = "https://api.nexusmods.com/v2/graphql";
    public const string GameDomain = "fieldsofmistria";
    public const int GameId = 6685;

    /// <summary>
    /// Mods of Mistria Installer itself. Almost every mod lists it, and AIM is what is reading the
    /// list, so it is always satisfied.
    /// </summary>
    public const int InstallerModId = 78;

    /// <summary>The request body: one batched query for every id, so an install costs one request.</summary>
    public static string BuildQuery(IEnumerable<int> modIds)
    {
        var ids = string.Join(", ", modIds.Distinct().OrderBy(i => i)
            .Select(i => $"{{gameDomain: \"{GameDomain}\", modId: {i}}}"));
        var query = "{ legacyModsByDomain(ids: [" + ids + "]) { nodes { modId name modRequirements { "
                    + "nexusRequirements { nodes { modId modName url notes externalRequirement gameId } } } } } }";
        return JsonConvert.SerializeObject(new { query });
    }

    /// <summary>
    /// The response, keyed by the requiring mod's id. Mods Nexus doesn't know are simply absent.
    /// Anything malformed yields an empty result rather than an exception: requirements are a hint.
    /// </summary>
    public static Dictionary<int, List<NexusRequirement>> Parse(string json)
    {
        var result = new Dictionary<int, List<NexusRequirement>>();
        JObject root;
        try
        {
            root = JObject.Parse(json);
        }
        catch (JsonException)
        {
            return result;
        }

        if (root.SelectToken("data.legacyModsByDomain.nodes") is not JArray nodes) return result;
        foreach (var node in nodes.OfType<JObject>())
        {
            var modId = node.Value<int?>("modId");
            if (modId is null) continue;
            var list = new List<NexusRequirement>();
            if (node.SelectToken("modRequirements.nexusRequirements.nodes") is JArray reqs)
            {
                foreach (var req in reqs.OfType<JObject>())
                {
                    if (!int.TryParse(req.Value<string>("modId"), out var reqId)) continue;
                    int.TryParse(req.Value<string>("gameId"), out var gameId);
                    list.Add(new NexusRequirement(
                        reqId,
                        req.Value<string>("modName") ?? $"Mod {reqId}",
                        req.Value<string>("url") ?? "",
                        req.Value<string>("notes") ?? "",
                        req.Value<bool?>("externalRequirement") ?? false,
                        gameId));
                }
            }
            result[modId.Value] = list;
        }
        return result;
    }

    /// <summary>The link to show for a requirement: its Nexus page, or the external link it names.</summary>
    public static string LinkFor(NexusRequirement requirement)
    {
        if (!requirement.External && (requirement.GameId == GameId || requirement.GameId == 0) && requirement.ModId > 0)
            return $"https://www.nexusmods.com/{GameDomain}/mods/{requirement.ModId}";
        return requirement.Url;
    }
}

/// <summary>Fetches requirements for a batch of Nexus mod ids. Null means "couldn't ask" (offline etc).</summary>
public interface INexusRequirementsFetcher
{
    Dictionary<int, List<NexusRequirement>>? Fetch(IReadOnlyCollection<int> modIds);
}

/// <summary>The real fetcher: one POST, short timeout, never throws.</summary>
public sealed class HttpNexusRequirementsFetcher(TimeSpan? timeout = null) : INexusRequirementsFetcher
{
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(8);

    public Dictionary<int, List<NexusRequirement>>? Fetch(IReadOnlyCollection<int> modIds)
    {
        if (modIds.Count == 0) return [];
        try
        {
            using var http = new HttpClient();
            http.Timeout = _timeout;
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AIM-PortraitWardrobe", "1.0"));
            using var content = new StringContent(NexusRequirementsApi.BuildQuery(modIds), Encoding.UTF8, "application/json");
            using var response = http.PostAsync(NexusRequirementsApi.Endpoint, content).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode) return null;
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return NexusRequirementsApi.Parse(body);
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>
/// Remembers requirements between installs (<c>aim_portrait_requirements.json</c> beside the mods),
/// so an install never waits on Nexus for pages it read recently, and an offline install still
/// shows what was known.
/// </summary>
public sealed class PortraitRequirementsCache
{
    public const string FileName = "aim_portrait_requirements.json";
    private static readonly TimeSpan FreshFor = TimeSpan.FromDays(3);

    private readonly string? _path;
    private readonly Dictionary<int, (DateTimeOffset FetchedAt, List<NexusRequirement> Requirements)> _entries = new();

    public PortraitRequirementsCache(string? modsLocation)
    {
        _path = string.IsNullOrEmpty(modsLocation) ? null : Path.Combine(modsLocation, FileName);
        Load();
    }

    public List<NexusRequirement>? Get(int modId) =>
        _entries.TryGetValue(modId, out var entry) ? entry.Requirements : null;

    public bool IsFresh(int modId, DateTimeOffset now) =>
        _entries.TryGetValue(modId, out var entry) && now - entry.FetchedAt < FreshFor;

    public void Put(int modId, List<NexusRequirement> requirements, DateTimeOffset now) =>
        _entries[modId] = (now, requirements);

    /// <summary>
    /// Fills in what's missing or stale with one request. Ids Nexus didn't return (deleted, hidden)
    /// are remembered as having no requirements, so they aren't asked about again every install.
    /// </summary>
    public void Refresh(IEnumerable<int> modIds, INexusRequirementsFetcher? fetcher, DateTimeOffset now)
    {
        if (fetcher is null) return;
        var stale = modIds.Distinct().Where(id => !IsFresh(id, now)).ToList();
        if (stale.Count == 0) return;
        var fetched = fetcher.Fetch(stale);
        if (fetched is null) return; // offline: keep whatever we had
        foreach (var id in stale)
            Put(id, fetched.TryGetValue(id, out var reqs) ? reqs : [], now);
        Save();
    }

    private void Load()
    {
        if (_path is null || !File.Exists(_path)) return;
        try
        {
            var root = JObject.Parse(File.ReadAllText(_path));
            if (root["mods"] is not JObject mods) return;
            foreach (var (key, value) in mods)
            {
                if (!int.TryParse(key, out var id) || value is not JObject entry) continue;
                var at = entry.Value<DateTime?>("fetchedAt");
                var list = (entry["requirements"]?.ToObject<List<NexusRequirement?>>() ?? [])
                    .OfType<NexusRequirement>().ToList(); // a hand-edited null is just dropped
                var fetchedAt = at is null ? DateTimeOffset.MinValue : new DateTimeOffset(at.Value.ToUniversalTime());
                if (fetchedAt > DateTimeOffset.UtcNow.AddDays(1)) fetchedAt = DateTimeOffset.MinValue; // clock or file from the future: refetch
                _entries[id] = (fetchedAt, list);
            }
        }
        catch (Exception)
        {
            _entries.Clear(); // a damaged cache is just an empty one
        }
    }

    private void Save()
    {
        if (_path is null) return;
        try
        {
            var mods = new JObject();
            foreach (var (id, entry) in _entries.OrderBy(e => e.Key))
            {
                mods[id.ToString()] = new JObject
                {
                    ["fetchedAt"] = entry.FetchedAt.UtcDateTime,
                    ["requirements"] = JArray.FromObject(entry.Requirements),
                };
            }
            File.WriteAllText(_path, new JObject { ["mods"] = mods }.ToString(Formatting.Indented));
        }
        catch (Exception)
        {
            // best effort
        }
    }
}
