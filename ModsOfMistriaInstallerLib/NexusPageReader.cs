using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace Garethp.ModsOfMistriaInstallerLib;

/// <summary>One post from a mod's bug tracker or comment thread.</summary>
public sealed record NexusPagePost(string Author, string Text, string Tab);

/// <summary>Another mod linked from a mod's page - a requirement, a patch, a recommendation.</summary>
public sealed record NexusRelatedMod(int ModId, string Title, string Url);

/// <summary>
/// One comment or bug reply with its place in the conversation: who wrote it, whether that was the
/// mod's own author, and which post it answers.
///
/// <see cref="NexusPagePost"/> is a flat list of text, which is all the conflict researcher needed.
/// It is not enough to answer "is this compatible with March Expanded?", because on Nexus the
/// answer is never in the question - it is the author's reply nested underneath it, and a flat
/// list has already forgotten which reply belongs to which question.
/// </summary>
/// <param name="Id">The site's own comment id, or a position-based stand-in where it has none.</param>
/// <param name="ParentId">The comment this one replies to, or null for a top-level post.</param>
/// <param name="ByModAuthor">True for a post by the mod's author, which Nexus marks on the post.</param>
public sealed record NexusComment(
    string Id,
    string? ParentId,
    string Author,
    bool ByModAuthor,
    string Text,
    string Tab)
{
    public NexusPagePost ToPost() => new(Author, Text, Tab);
}

/// <summary>
/// What came back from one request: the page, or why there is no page.
///
/// The readers below used to return null for every failure alike, and the crash window then said
/// "nothing found" - which was true and told the user nothing, because the pages had never been
/// read. Nexus answers a client its bot check does not like with a challenge page instead of the
/// one asked for, and that is worth saying in so many words.
/// </summary>
/// <param name="Blocked">True when the answer was Nexus's bot check rather than the page.</param>
public sealed record NexusFetch(string? Html, int StatusCode, bool Blocked)
{
    public bool Succeeded => Html is not null;

    public static readonly NexusFetch Offline = new(null, 0, false);
}

/// <summary>
/// Reads the parts of a mod page the Nexus API does not expose: its bug reports and its comments.
///
/// This is scraping, and it is treated as such. The public API has no endpoint for either, yet that
/// is exactly where "does this work with X?" gets answered - so the choice is between reading the
/// HTML and telling the user to go and read it themselves. What makes it acceptable here is that
/// nothing depends on it: every extraction is best-effort, a layout change produces no posts rather
/// than wrong ones, and the caller always shows the links to the real pages regardless.
///
/// It deliberately does not log in, follow pagination, or pretend to be a browser beyond a plain
/// user agent. It reads the first page of what a signed-out visitor would see.
/// </summary>
public static class NexusPageReader
{
    private const int MaxPostsPerTab = 40;
    private const int MaxPostLength = 600;

    // Nexus renders each comment and bug body inside an element carrying one of these classes. More
    // than one is listed because the two tabs differ and the markup has changed before; whichever
    // matches, matches.
    private static readonly string[] BodyClasses =
        ["comment-content", "comment-body", "bug-content", "forum-post-content", "post-content"];

    private static readonly Regex AuthorPattern = new(
        "class=\"[^\"]*\\b(?:comment-user|comment-author|username|user-name)\\b[^\"]*\"[^>]*>(?:\\s*<[^>]+>)*\\s*([^<]{1,60})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Fetches one tab of a mod page and pulls out the posts it can find.
    /// </summary>
    /// <returns>An empty list whenever anything at all goes wrong. This is never an error path.</returns>
    public static async Task<List<NexusPagePost>> ReadTabAsync(
        HttpClient http, string pageUrl, string tab, CancellationToken ct = default) =>
        await ReadTabAsync(http, pageUrl, tab, 1, Research.NexusSession.Anonymous, ct);

    /// <summary>
    /// Reads a tab across several pages of comments or bug reports.
    ///
    /// The first page used to be the whole of it, and on a popular mod that is the *newest* twenty
    /// posts - which are about the mod's latest release, not about the mod you happen to have
    /// installed alongside something else. The answer to "does this work with X" is usually months
    /// old and several pages back, so a single page systematically missed exactly the posts the
    /// researcher exists to find.
    ///
    /// It still stops early rather than reading a thread to the end: a page that yields nothing new
    /// ends the walk, because that is what both the last page and a layout change look like, and
    /// there is no reason to keep asking Nexus for either.
    /// </summary>
    /// <param name="maxPages">
    /// How deep to go. Only the comments actually paginate; the bug tracker is read as one page
    /// because its tab renders whole and there is no equivalent widget to call.
    /// </param>
    /// <param name="gameId">
    /// Nexus's numeric game id, needed to address the comment widget. Zero reads page one only.
    /// </param>
    /// <param name="modId">The mod whose comments these are. Zero reads page one only.</param>
    public static async Task<List<NexusPagePost>> ReadTabAsync(
        HttpClient http,
        string pageUrl,
        string tab,
        int maxPages,
        Research.NexusSession session,
        CancellationToken ct = default,
        int gameId = 0,
        int modId = 0)
    {
        var all = new List<NexusPagePost>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Page one always comes from the tab itself, which renders server-side for both tabs.
        var firstPage = await FetchAsync(http, $"{pageUrl}?tab={tab}", session, ct);
        if (firstPage is null) return all;

        foreach (var post in Extract(firstPage, tab).Where(post => seen.Add(post.Text)))
            all.Add(post);

        var comments = tab.Equals("posts", StringComparison.OrdinalIgnoreCase);
        if (!comments || maxPages <= 1 || gameId <= 0 || modId <= 0) return all;

        // The rest come from the widget the site's own pager calls. Without a thread id there is
        // no way to ask for them, and page one is simply what the user gets.
        var threadId = ExtractCommentThreadId(firstPage);
        if (threadId is null)
        {
            Logger.Log($"No comment thread id on {pageUrl}, so only the first page was read.");
            return all;
        }

        for (var page = 2; page <= maxPages; page++)
        {
            // A short pause between pages. Nothing here is urgent, and a mod manager should not
            // look like a scraper hammering somebody else's server.
            await Task.Delay(250, ct);

            var posts = await ReadCommentPageAsync(
                http, gameId, modId, threadId.Value, page, session, ct);

            // An empty answer is the end of the thread, and a page of nothing but posts already
            // seen is what a widget that ignored the page number would return. Both stop the walk
            // rather than being read as an error.
            var fresh = posts.Where(post => seen.Add(post.Text)).ToList();
            if (fresh.Count == 0) break;

            all.AddRange(fresh);
        }

        return all;
    }

    /// <summary>
    /// Fetches a page, or null when anything goes wrong. Shared by every reader here so they all
    /// identify themselves the same way and all fail the same silent way.
    /// </summary>
    /// <param name="asXhr">
    /// Marks the request as the background fetch it is. The comment widget is only ever called
    /// this way by the site's own pager, and saying so is more accurate than pretending a person
    /// typed the URL in.
    /// </param>
    public static async Task<string?> FetchAsync(
        HttpClient http, string url, Research.NexusSession session, CancellationToken ct = default,
        bool asXhr = false) =>
        (await FetchWithStatusAsync(http, url, session, ct, asXhr)).Html;

    /// <summary>
    /// Fetches a page and says what happened, including when Nexus's bot check answered instead.
    /// </summary>
    /// <param name="form">
    /// Sends the request as a form POST with these fields - the bug tracker's reply list is only
    /// served that way. Null for an ordinary GET.
    /// </param>
    public static async Task<NexusFetch> FetchWithStatusAsync(
        HttpClient http, string url, Research.NexusSession session, CancellationToken ct = default,
        bool asXhr = false, IReadOnlyDictionary<string, string>? form = null)
    {
        try
        {
            using var request = new HttpRequestMessage(form is null ? HttpMethod.Get : HttpMethod.Post, url);
            session.Apply(request);

            if (asXhr) request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            if (form is not null) request.Content = new FormUrlEncodedContent(form);

            using var response = await http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            var status = (int)response.StatusCode;

            // Cloudflare says so in a header when it challenges. Without the header, the page itself
            // is only taken for a challenge when it also came back as a refusal: a real page can
            // load Cloudflare's scripts too, and reading every successful page as blocked would
            // stop the research dead.
            var challenged = response.Headers.TryGetValues("cf-mitigated", out var mitigated) &&
                             mitigated.Any(value => value.Contains("challenge", StringComparison.OrdinalIgnoreCase));

            var refused = status is 403 or 429 or 503;

            if (challenged || (refused && LooksLikeABotCheck(body)))
            {
                Logger.Log($"Nexus answered {url} with its bot check ({status}) instead of the page.");
                return new NexusFetch(null, status, true);
            }

            if (!response.IsSuccessStatusCode)
            {
                Logger.Log($"Could not read {url}: {status}");
                return new NexusFetch(null, status, false);
            }

            return new NexusFetch(body, status, false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A timeout surfaces as a cancellation the caller did not ask for, and is just another
            // failed read - not a reason to abandon the whole research run.
            Logger.Log($"Could not read {url}: {exception.Message}");
            return NexusFetch.Offline;
        }
    }

    /// <summary>
    /// True for the interstitial Cloudflare serves in place of a page it wants a browser to solve.
    ///
    /// Recognised by its own markers rather than by status alone: it arrives as a 403 or a 503,
    /// and so does an ordinary refusal, but only the challenge carries these.
    /// </summary>
    public static bool LooksLikeABotCheck(string? body)
    {
        if (string.IsNullOrEmpty(body)) return false;

        // Only the head of the document: a genuine page can quote these words in a comment.
        var head = body.Length > 6000 ? body[..6000] : body;

        // The interstitial's own title and its configuration object - not merely a reference to
        // Cloudflare's script host, which an ordinary page can carry for a sign-up widget.
        return head.Contains("<title>Just a moment", StringComparison.OrdinalIgnoreCase) ||
               head.Contains("cf_chl_opt", StringComparison.OrdinalIgnoreCase) ||
               head.Contains("/cdn-cgi/challenge-platform/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The comment thread's own id, read out of the mod page.
    ///
    /// Needed because the widget that serves page two of the comments is addressed by thread rather
    /// than by mod, and nothing else on the site exposes the mapping - the page's own JavaScript
    /// reads it from the markup exactly like this.
    /// </summary>
    public static int? ExtractCommentThreadId(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;

        // Written as JSON, as a JS object literal and inside the widget's own query string in
        // different places on the page, so the separator is left loose rather than guessed at.
        var match = Regex.Match(html, @"thread_id[""'\s:=]{1,4}(\d{3,})", RegexOptions.IgnoreCase);

        return match.Success && int.TryParse(match.Groups[1].Value, out var id) ? id : null;
    }

    /// <summary>
    /// One page of a mod's comments, from the widget the site's own pager calls.
    ///
    /// The comment pager is not a link. Its hrefs are literally <c>javascript:;</c>, and
    /// <c>?tab=posts&amp;page=2</c> serves page one again - which is why reading "several pages" of
    /// comments quietly read one page for a while. What the page actually calls is this: a widget
    /// endpoint taking a single parameter whose value is a comma-separated list of key:value pairs.
    ///
    /// The parameters, and the page size, are the site's own rather than anything invented here.
    /// Asking for a bigger page than the site asks for would fetch more per request but is exactly
    /// the sort of thing that gets a client blocked, and the point of this is to read what a
    /// visitor can already see.
    /// </summary>
    /// <param name="objectId">The mod's id. The widget calls it an object id because it serves
    /// comments for images and collections through the same endpoint.</param>
    /// <param name="search">
    /// Restricts the answer to comments containing this text, which is what makes reading a long
    /// thread practical at all - see <see cref="SearchCommentsAsync"/>. Empty returns the page as
    /// the site would show it.
    /// </param>
    public static async Task<List<NexusPagePost>> ReadCommentPageAsync(
        HttpClient http,
        int gameId,
        int objectId,
        int threadId,
        int page,
        Research.NexusSession session,
        CancellationToken ct = default,
        string search = "")
    {
        var url = CommentPageUrl(gameId, objectId, threadId, page, search, 0);

        var html = await FetchAsync(http, url, session, ct, asXhr: true);

        return html is null ? [] : Extract(html, "posts");
    }

    /// <summary>
    /// One page of a comment thread with its replies still attached to their questions, and what
    /// the request came back with. See <see cref="ReadCommentPageAsync"/> for the parameters.
    /// </summary>
    public static async Task<(List<NexusComment> Comments, NexusFetch Fetch)> ReadCommentThreadPageAsync(
        HttpClient http,
        int gameId,
        int objectId,
        int threadId,
        int page,
        Research.NexusSession session,
        CancellationToken ct = default,
        string search = "")
    {
        var fetch = await FetchWithStatusAsync(
            http, CommentPageUrl(gameId, objectId, threadId, page, search, 0), session, ct, asXhr: true);

        return (fetch.Html is null ? [] : ExtractComments(fetch.Html, "posts"), fetch);
    }

    /// <summary>
    /// The page of a thread that holds one particular comment, with the conversation around it.
    ///
    /// A search answers with matching comments only, and flat: an author's "yes, just grab the
    /// portrait patch" arrives without the "does this work with March Sylus?" it was answering,
    /// and a question arrives without its answer. Asked for by comment id, the same widget serves
    /// the page that comment sits on with every reply nested under its question - which is how
    /// the site's own "view in thread" link works - so one request recovers both halves.
    /// </summary>
    public static async Task<(List<NexusComment> Comments, NexusFetch Fetch)> ReadCommentContextAsync(
        HttpClient http,
        int gameId,
        int objectId,
        int threadId,
        string commentId,
        Research.NexusSession session,
        CancellationToken ct = default)
    {
        if (!long.TryParse(commentId, out var id) || id <= 0) return ([], NexusFetch.Offline);

        var fetch = await FetchWithStatusAsync(
            http, CommentPageUrl(gameId, objectId, threadId, 1, "", id), session, ct, asXhr: true);

        return (fetch.Html is null ? [] : ExtractComments(fetch.Html, "posts"), fetch);
    }

    private static string CommentPageUrl(int gameId, int objectId, int threadId, int page, string search, long commentId)
    {
        // Exactly the parameters the site's own pager sends, in its order. comment_id is not
        // optional: without it the widget answers every page with page one, which is precisely the
        // bug that made an earlier attempt at this silently read nothing new. Zero means "no
        // particular comment"; a real id asks for the page that comment is on.
        var parameters = string.Join(",",
            $"comment_id:{commentId}",
            $"game_id:{gameId}",
            $"object_id:{objectId}",
            "object_type:1",
            $"thread_id:{threadId}",
            $"search_text:{Sanitise(search)}",
            "tabbed:1",
            "skip_opening_post:0",
            "display_title:0",
            "user_is_blocked:false",
            "searchable:true",
            $"page_size:{CommentPageSize}",
            $"page:{page}");

        return "https://www.nexusmods.com/Core/Libs/Common/Widgets/CommentContainer" +
               $"?RH_CommentContainer={Uri.EscapeDataString(parameters)}";
    }

    /// <summary>The page size the site itself asks for.</summary>
    private const int CommentPageSize = 20;

    /// <summary>
    /// A comma and a colon are the widget's own separators, so a search term containing either
    /// would be read as more parameters. Nothing else needs escaping - the whole value is URL
    /// encoded by the caller.
    /// </summary>
    private static string Sanitise(string term) =>
        string.IsNullOrWhiteSpace(term) ? "" : term.Replace(',', ' ').Replace(':', ' ').Trim();

    /// <summary>
    /// Every comment in a mod's thread that mentions a given phrase.
    ///
    /// This is the difference between reading a mod's comments and merely sampling them. A busy
    /// mod has five hundred comments across twenty-odd pages, and the one that answers "does this
    /// work with X?" is usually months old and nowhere near the first page - so walking pages from
    /// the front is a lottery. The thread is searchable, and searching it for the *other mod's
    /// name* returns the handful of comments that are actually about this pairing, out of the whole
    /// thread, in one request.
    ///
    /// It needs no account: the site hides the search box behind a login prompt, but the endpoint
    /// answers a signed-out request identically. That was worth checking rather than assuming,
    /// because it is the difference between this working for everybody and working for nobody.
    /// </summary>
    /// <param name="maxPages">
    /// How many pages of matches to take. The search answers in pages like everything else, so a
    /// term that matches thirty comments returns twenty of them and a pager - and reading only page
    /// one quietly threw the rest away. The walk still stops as soon as a page comes back short or
    /// repeats what the last one said, so a term with three matches costs one request.
    /// </param>
    public static async Task<List<NexusPagePost>> SearchCommentsAsync(
        HttpClient http,
        int gameId,
        int modId,
        int threadId,
        string phrase,
        Research.NexusSession session,
        CancellationToken ct = default,
        int maxPages = 1)
    {
        var all = new List<NexusPagePost>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var page = 1; page <= Math.Max(1, maxPages); page++)
        {
            if (page > 1) await Task.Delay(250, ct);

            var posts = await ReadCommentPageAsync(
                http, gameId, modId, threadId, page, session, ct, phrase);

            var fresh = posts.Where(post => seen.Add(post.Text)).ToList();
            all.AddRange(fresh);

            // A short page is the last page, and a page of nothing new is a widget that ignored the
            // page number. Either way there is nothing further to ask for.
            if (fresh.Count == 0 || posts.Count < CommentPageSize) break;
        }

        return all;
    }

    /// <summary>
    /// The readme behind a mod's Docs tab, as plain text.
    ///
    /// Worth a special case rather than being scraped off the tab like everything else, because
    /// Nexus publishes it ready-made: the "View as plain text" link on that tab points at a static
    /// file on its metadata host, so there is no markup to strip, no layout to break, and nothing
    /// to get wrong. An author who documents "do not use this with X" anywhere other than the
    /// description usually does it here.
    /// </summary>
    /// <param name="gameId">
    /// Nexus's numeric id for the game, which is what this host is keyed by - not the
    /// <c>fieldsofmistria</c> domain name the rest of the site uses.
    /// </param>
    /// <returns>Null when the mod has no readme, which is the common case and not an error.</returns>
    public static Task<string?> ReadReadmeAsync(
        HttpClient http, int gameId, int modId, Research.NexusSession session, CancellationToken ct = default) =>
        FetchAsync(http, $"https://file-metadata.nexusmods.com/file/nexus-readmes/{gameId}/{modId}/readme.txt",
            session, ct);

    /// <summary>
    /// The mods listed on a mod's page as requiring it.
    ///
    /// This is the one place a compatibility patch reliably announces itself. A patch for A and B
    /// declares both as requirements, so Nexus lists it under "Mods requiring this file" on *both*
    /// pages - and a mod appearing on both lists is, with very few exceptions, exactly the patch
    /// the user is looking for. Finding it this way needs no search engine and no guessing at
    /// titles, which is why it is preferred to searching for the word "patch".
    /// </summary>
    public static List<NexusRelatedMod> ExtractRequiringMods(string html, string game)
    {
        var related = new List<NexusRelatedMod>();
        if (string.IsNullOrWhiteSpace(html)) return related;

        // Every link from the page to another mod in the same game, deduplicated by id. Casting a
        // wider net than the "requiring" table alone is deliberate: authors also link the patch
        // straight from their description, and the two lists are used the same way - as candidates
        // that have to appear on both mods' pages before anything is claimed.
        var pattern = new Regex(
            $@"href=""(?:https://www\.nexusmods\.com)?/(?:games/)?{Regex.Escape(game)}/mods/(\d+)[^""]*""[^>]*>(?:\s*<[^>]+>)*\s*([^<]{{0,120}})",
            RegexOptions.IgnoreCase);

        var seen = new HashSet<int>();

        foreach (Match match in pattern.Matches(html))
        {
            if (!int.TryParse(match.Groups[1].Value, out var id) || !seen.Add(id)) continue;

            var title = WebUtility.HtmlDecode(match.Groups[2].Value).Trim();
            related.Add(new NexusRelatedMod(id, title,
                $"https://www.nexusmods.com/{game}/mods/{id}"));
        }

        return related;
    }

    /// <summary>
    /// Pulls post bodies out of a page's HTML.
    ///
    /// Split out from the fetch so it can be tested against saved markup - the only way to keep a
    /// scraper honest without a network.
    /// </summary>
    public static List<NexusPagePost> Extract(string html, string tab)
    {
        var posts = new List<NexusPagePost>();
        if (string.IsNullOrWhiteSpace(html)) return posts;

        // Where each name appears, not just the order they appear in. Pairing by index would
        // desynchronise the moment anything produced a name without a kept post - a one-word
        // comment below the length floor, a deleted post, the "uploaded by" box - and every
        // later quote would then be attributed to the wrong person. Wrong attribution is worse
        // than none, so position decides it.
        var authors = AuthorPattern.Matches(html)
            .Select(match => (match.Index, Name: WebUtility.HtmlDecode(match.Groups[1].Value).Trim()))
            .Where(entry => entry.Name.Length > 0)
            .ToList();

        foreach (var body in BodyClasses)
        {
            // Non-greedy to the next closing div: nested markup inside a post is stripped to text
            // afterwards anyway, so the exact boundary matters less than not swallowing the page.
            var pattern = new Regex(
                $"class=\"[^\"]*\\b{Regex.Escape(body)}\\b[^\"]*\"[^>]*>(.{{0,8000}}?)</div>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match match in pattern.Matches(html))
            {
                var text = PlainText(match.Groups[1].Value);
                if (text.Length < 15) continue;

                // The nearest name above this post in the document is the one who wrote it.
                var author = authors
                    .Where(entry => entry.Index < match.Index)
                    .Select(entry => entry.Name)
                    .LastOrDefault() ?? "";

                posts.Add(new NexusPagePost(author, Trim(text), tab));

                if (posts.Count >= MaxPostsPerTab) return posts;
            }

            if (posts.Count > 0) break;
        }

        return posts;
    }

    // ── Comments with their structure ────────────────────────────────────────────

    // List items and the lists that hold them. The lists matter because HTML lets an item go
    // unclosed until its list ends, and a comment body is somebody's own markup: a "<ul><li>one
    // <li>two</ul>" inside one post must not leave that post open, or the next post reads as its reply.
    private static readonly Regex ListItemTag = new(
        @"<li\b(?<attrs>[^>]*)>|</li\s*>|<(?<list>ul|ol)\b[^>]*>|</(?<endlist>ul|ol)\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ClassAttribute = new(
        @"\bclass\s*=\s*""(?<value>[^""]*)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Comments are "comment-123"; the bug tracker's report and replies are "bug-issue-tile-123" and
    // "bug-reply-tile-123", in the same markup otherwise.
    private static readonly Regex CommentIdAttribute = new(
        @"\bid\s*=\s*""(?:comment|bug-issue-tile|bug-reply-tile)-(?<id>\d+)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CommentNamePattern = new(
        @"class=""[^""]*\bcomment-name\b[^""]*""[^>]*>(?:\s*<[^>]+>)*\s*(?<name>[^<]{1,60})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DivTag = new(@"<div\b[^>]*>|</div\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ContentTextDiv = new(
        @"<div\b[^>]*class=""[^""]*\bcomment-content-text\b[^""]*""[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "comment-content" exactly - not "comment-content-text", which the pattern above handles.
    private static readonly Regex ContentDiv = new(
        @"<div\b[^>]*class=""(?:[^""]*\s)?comment-content(?:\s[^""]*)?""[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NotProse = new(
        @"<(time|blockquote|script|style)\b[^>]*>.*?</\1\s*>|<div\b[^>]*class=""[^""]*\b(?:locked|sticky)\b[^""]*""[^>]*>.*?</div\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private const int MaxCommentLength = 2000;

    /// <summary>
    /// Every comment in a page of a thread, with who wrote it and what it replies to.
    ///
    /// Nexus renders a thread as nested lists: each post is an <c>li.comment</c>, the author's own
    /// posts carry <c>comment-author</c> as well, and replies sit in an <c>ol.comment-kids</c>
    /// inside the post they answer. The bug tracker's reply list uses the same markup without the
    /// ids. Walking the list tags with a stack recovers the nesting without an HTML parser, which
    /// this project does not otherwise need.
    ///
    /// Like everything else here, a layout change produces fewer comments rather than wrong ones:
    /// a post whose body cannot be found is skipped, never attributed to a neighbour.
    /// </summary>
    public static List<NexusComment> ExtractComments(string html, string tab)
    {
        var comments = new List<NexusComment>();
        if (string.IsNullOrWhiteSpace(html)) return comments;

        var open = new List<OpenItem>();
        var closed = new List<(OpenItem Item, string? ParentId, int End)>();

        void Close(int end)
        {
            var top = open[^1];
            open.RemoveAt(open.Count - 1);

            if (top.IsComment)
                closed.Add((top, open.LastOrDefault(item => item.IsComment)?.Id, end));
        }

        foreach (Match tag in ListItemTag.Matches(html))
        {
            if (tag.Groups["list"].Success)
            {
                open.Add(new OpenItem(false, "", false, tag.Index, tag.Index + tag.Length) { IsList = true });
                continue;
            }

            if (tag.Groups["endlist"].Success)
            {
                // Ends every item still open inside this list, then the list itself. A stray close
                // with no list open is ignored rather than allowed to close a post.
                if (!open.Any(item => item.IsList)) continue;

                while (open.Count > 0 && !open[^1].IsList) Close(tag.Index);
                if (open.Count > 0) open.RemoveAt(open.Count - 1);
                continue;
            }

            if (tag.Value.StartsWith("</", StringComparison.Ordinal))
            {
                // Closes the innermost open item. A close with only a list above it is a stray.
                if (open.Count == 0 || open[^1].IsList) continue;

                Close(tag.Index);
                continue;
            }

            // An item opened while a sibling item is still open in the same list closes that sibling
            // first - which is what a browser does with "<li>one<li>two".
            if (open.Count >= 2 && !open[^1].IsList && open[^2].IsList)
                Close(tag.Index);

            var attributes = tag.Groups["attrs"].Value;
            var classes = ClassAttribute.Match(attributes) is { Success: true } classMatch
                ? classMatch.Groups["value"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                : [];

            var isComment = classes.Contains("comment", StringComparer.OrdinalIgnoreCase);
            var id = CommentIdAttribute.Match(attributes) is { Success: true } idMatch
                ? idMatch.Groups["id"].Value
                : $"@{tag.Index}";

            open.Add(new OpenItem(isComment, id,
                classes.Contains("comment-author", StringComparer.OrdinalIgnoreCase),
                tag.Index, tag.Index + tag.Length));
        }

        // Truncated markup leaves posts open at the end of the page. They still happened.
        while (open.Count > 0)
        {
            if (open[^1].IsList)
            {
                open.RemoveAt(open.Count - 1);
                continue;
            }

            Close(html.Length);
        }

        var posts = closed.OrderBy(entry => entry.Item.TagStart).ToList();

        foreach (var (item, parentId, end) in posts)
        {
            // A post's own part ends where its first reply begins; the replies are posts of their
            // own and must not be read as part of this one.
            var firstReply = posts
                .Where(other => other.Item.TagStart > item.TagStart && other.Item.TagStart < end)
                .Select(other => other.Item.TagStart)
                .DefaultIfEmpty(end)
                .Min();

            var own = html[item.ContentStart..Math.Max(item.ContentStart, firstReply)];

            var text = BodyOf(own);
            if (text.Length < 10) continue;

            var author = CommentNamePattern.Match(own) is { Success: true } name
                ? WebUtility.HtmlDecode(name.Groups["name"].Value).Trim()
                : AuthorPattern.Match(own) is { Success: true } fallback
                    ? WebUtility.HtmlDecode(fallback.Groups[1].Value).Trim()
                    : "";

            comments.Add(new NexusComment(
                item.Id, parentId, author, item.ByModAuthor,
                text.Length <= MaxCommentLength ? text : text[..MaxCommentLength] + "…",
                tab));
        }

        return comments;
    }

    private sealed record OpenItem(bool IsComment, string Id, bool ByModAuthor, int TagStart, int ContentStart)
    {
        /// <summary>A ul or ol rather than an item - what an unclosed item is closed by.</summary>
        public bool IsList { get; init; }
    }

    /// <summary>The comment a reply answers, from a list <see cref="ExtractComments"/> produced.</summary>
    public static NexusComment? ParentOf(IReadOnlyList<NexusComment> comments, NexusComment reply) =>
        reply.ParentId is null
            ? null
            : comments.FirstOrDefault(comment => comment.Id == reply.ParentId);

    /// <summary>The direct replies to a comment, in the order they were posted.</summary>
    public static IEnumerable<NexusComment> RepliesTo(IReadOnlyList<NexusComment> comments, NexusComment question) =>
        comments.Where(comment => comment.ParentId == question.Id);

    /// <summary>
    /// How many comments a search matched in all, as the widget reports it above the results -
    /// "52 comments". Null when the page does not say, which is not an error: the count is only
    /// used to tell the user how much of the thread was covered.
    /// </summary>
    public static int? ExtractResultCount(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;

        var match = Regex.Match(html, @">\s*(\d[\d,]*)\s+(?:comments?|results?)\s*<", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value.Replace(",", ""), out var count) ? count : null;
    }

    /// <summary>
    /// The prose of one post: the comment text where the page marks it, otherwise the content
    /// block with the reply form and the timestamp taken out.
    /// </summary>
    private static string BodyOf(string own)
    {
        var textDiv = ContentTextDiv.Match(own);
        string inner;

        if (textDiv.Success)
        {
            inner = InnerOfDiv(own, textDiv.Index);
        }
        else
        {
            var contentDiv = ContentDiv.Match(own);
            if (!contentDiv.Success) return "";

            inner = InnerOfDiv(own, contentDiv.Index);

            // The bug tracker puts the reply form inside the post's own content block.
            var form = inner.IndexOf("comment-reply", StringComparison.OrdinalIgnoreCase);
            if (form >= 0)
            {
                var formStart = inner.LastIndexOf("<div", form, StringComparison.OrdinalIgnoreCase);
                inner = inner[..(formStart >= 0 ? formStart : form)];
            }
        }

        return PlainText(NotProse.Replace(inner, " "));
    }

    /// <summary>What is inside a div, counting nested divs so an inner one does not end it early.</summary>
    private static string InnerOfDiv(string html, int openIndex)
    {
        var first = DivTag.Match(html, openIndex);
        if (!first.Success) return "";

        var contentStart = first.Index + first.Length;
        var depth = 0;

        for (var tag = first; tag.Success; tag = tag.NextMatch())
        {
            if (tag.Value.StartsWith("</", StringComparison.Ordinal))
            {
                depth--;
                if (depth == 0) return html[contentStart..tag.Index];
            }
            else
            {
                depth++;
            }
        }

        return html[contentStart..];
    }

    private static string PlainText(string html)
    {
        // Scripts and styles first: their contents are not prose and would otherwise survive the
        // tag strip as a wall of JavaScript.
        var withoutScripts = Regex.Replace(html, "<(script|style)[^>]*>.*?</\\1>", " ",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var withoutTags = Regex.Replace(withoutScripts, "<[^>]+>", " ");

        return Regex.Replace(WebUtility.HtmlDecode(withoutTags), @"\s+", " ").Trim();
    }

    private static string Trim(string text) =>
        text.Length <= MaxPostLength ? text : text[..MaxPostLength] + "…";
}
