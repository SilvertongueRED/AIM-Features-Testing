using Garethp.ModsOfMistriaInstallerLib;

namespace ModsOfMistriaInstallerLibTests.Research;

// Reading a Nexus comment thread with its shape intact: who wrote each post, whether that was the
// mod's author, and which question each reply answers.
//
// The markup below is the real thing - the page the site's own comment widget serves when asked
// for one comment in context, captured from March Expanded's thread in October 2026 - with the
// links, images and form fields stripped and the two account names replaced. Nothing else about
// its structure has been touched, because the structure is what is under test.
[TestFixture]
public class NexusCommentThreadTest
{
    private const string QuestionAndAnswer = """
        <div class="container comments condensed"><ol><li class="comment " id="comment-173549301"> <a href="#"></a> <div class="comment-head clearfix"> <a class="comment-user" href="#"> <img></img> </a> <div class="comment-details"> <span class="comment-name"> <a href="#"> Questioner </a> </span> <ul class="clearfix"> <li> <span class="status-user"> member </span> </li> <li>0 kudos</li> </ul> </div> <a href="#"></a> <div class="comment-actions"> <ul class="actions clearfix"> <li> <a class="popup-btn-ajax btn inline-flex" href="#"> <svg></svg> <span class="flex-label">Report</span> </a> </li> <li> <a class="btn inline-flex" id="button-reply-to-173549301" href="#"> <svg></svg> <span class="flex-label">Reply</span> </a> </li> </ul> </div> </div> <div class="comment-content"> <div class="locked" id="locked-comment-label-173549301"> Locked </div> <div class="sticky" id="sticky-comment-label-173549301"> Sticky </div> <time class="dst-date-adjust">09 Aug 2026, 5:58AM</time> <div class="comment-content-text" id="comment-content-173549301"> Does this work with the March Sylus mod? </div> <ul class="comment-inline clearfix"> </ul> <div class="comment-reply clearfix" id="add-comment-form-173549301"> <div> <h3>Add a reply</h3> <textarea></textarea> <div class="comment-char-limit" id="char-limit-add-comment-173549301">0 / 5000</div> <ul class="choice-list"> <li> <input></input> <label><span class="choice-check"></span><span class="check-label">Use emoticons</span></label> </li> </ul> <ul class="actions clearfix"> <li> <a class="btn  submit-comment" id="submit-add-comment-173549301" href="#"> Submit </a> </li> <li> <a class="btn" href="#"> Cancel </a> </li> </ul> <div class="info warning notice clearfix" id="add-comment-error-box-173549301"> <svg></svg> <div class="info-content" id="add-comment-error-message-173549301"></div> </div> </div> </div> </div> <ol class="comment-kids"> <li class="comment comment-author" id="comment-173551326"> <a href="#"></a> <div class="comment-head clearfix"> <a class="comment-user" href="#"> <img></img> </a> <div class="comment-details"> <span class="comment-name"> <a href="#"> ModAuthor </a> </span> <ul class="clearfix"> <li> <span class="status-premium"> premium </span> </li> <li>248 kudos</li> </ul> </div> <a href="#"></a> <div class="comment-actions"> <ul class="actions clearfix"> <li> <a class="popup-btn-ajax btn inline-flex" href="#"> <svg></svg> <span class="flex-label">Report</span> </a> </li> <li> <a class="btn inline-flex" id="button-reply-to-173551326" href="#"> <svg></svg> <span class="flex-label">Reply</span> </a> </li> </ul> </div> </div> <div class="comment-content"> <div class="locked" id="locked-comment-label-173551326"> Locked </div> <div class="sticky" id="sticky-comment-label-173551326"> Sticky </div> <time class="dst-date-adjust">09 Aug 2026, 7:10AM</time> <div class="comment-content-text" id="comment-content-173551326"> Hey! If it only changes the portraits then yeah it should be compatible! Just make sure to download the portrait compatibility path as well </div> <ul class="comment-inline clearfix"> </ul> <div class="comment-reply clearfix" id="add-comment-form-173551326"> <div> <h3>Add a reply</h3> <textarea></textarea> <div class="comment-char-limit" id="char-limit-add-comment-173551326">0 / 5000</div> <ul class="choice-list"> <li> <input></input> <label><span class="choice-check"></span><span class="check-label">Use emoticons</span></label> </li> </ul> <ul class="actions clearfix"> <li> <a class="btn  submit-comment" id="submit-add-comment-173551326" href="#"> Submit </a> </li> <li> <a class="btn" href="#"> Cancel </a> </li> </ul> <div class="info warning notice clearfix" id="add-comment-error-box-173551326"> <svg></svg> <div class="info-content" id="add-comment-error-message-173551326"></div> </div> </div> </div> </div> </li> </ol> </li></ol></div>
        """;

    // The bug tracker's reply list: the same comment markup, but flat, without the author flag, and
    // with the timestamp inside the content block rather than beside it.
    private const string BugThread = """
        <div class="comments"><ol>
        <li class="comment" id="bug-issue-tile-852000"> <div class="comment-head clearfix"> <a class="comment-user" href="#"> <img></img> </a> <div class="comment-details"> <span class="comment-name"> <a href="#">Reporter</a> </span> <ul class="clearfix"> <li><span class="status-user">member</span></li> </ul> </div> <div class="comment-actions"> <ul class="actions clearfix"> <li> <a class="popup-btn-ajax btn inline-flex" href="#"> <svg></svg> <span class="flex-label">Report</span> </a> </li> <li class="reply-link-to_852000"> <a class="btn inline-flex show-bug-reply" href="#"> <svg></svg> <span class="flex-label">Reply</span> </a> </li> </ul> </div> </div> <div class="comment-content"> <time>21 August 2024, 3:35PM</time> The game won't open with March and Ryis installed after replacing the files. </div> </li>
        <li class="comment" id="bug-reply-tile-4242861"> <div class="comment-head clearfix"> <a class="comment-user" href="#"> <img></img> </a> <div class="comment-details"> <span class="comment-name"> <a href="#">ModAuthor</a> </span> <ul class="clearfix"> <li><span class="status-user">member</span></li> </ul> </div> <div class="comment-actions"> <ul class="actions clearfix"> <li> <a class="popup-btn-ajax btn inline-flex" href="#"> <svg></svg> <span class="flex-label">Report</span> </a> </li> </ul> </div> </div> <div class="comment-content"> <time>21 August 2024, 5:41PM</time> sorry about that! it's probably because of the game's new update. Fixed in 2.0.4, please update. </div> </li>
        </ol></div>
        """;

    [Test]
    public void ShouldReadTheQuestionAndTheAuthorsAnswerAsTwoPosts()
    {
        var comments = NexusPageReader.ExtractComments(QuestionAndAnswer, "posts");

        Assert.That(comments, Has.Count.EqualTo(2), "the question and its one reply, and nothing from the reply form");
        Assert.That(comments[0].Text, Is.EqualTo("Does this work with the March Sylus mod?"));
        Assert.That(comments[1].Text, Does.StartWith("Hey! If it only changes the portraits"));
    }

    [Test]
    public void ShouldKnowWhichReplyAnswersWhichQuestion()
    {
        var comments = NexusPageReader.ExtractComments(QuestionAndAnswer, "posts");
        var question = comments[0];
        var answer = comments[1];

        Assert.That(question.Id, Is.EqualTo("173549301"));
        Assert.That(answer.Id, Is.EqualTo("173551326"));
        Assert.That(answer.ParentId, Is.EqualTo(question.Id), "the reply is nested in the question's comment-kids list");
        Assert.That(NexusPageReader.ParentOf(comments, answer), Is.SameAs(question));
        Assert.That(NexusPageReader.RepliesTo(comments, question).Single(), Is.SameAs(answer));
    }

    [Test]
    public void ShouldTellTheModAuthorFromEverybodyElse()
    {
        var comments = NexusPageReader.ExtractComments(QuestionAndAnswer, "posts");

        Assert.That(comments[0].Author, Is.EqualTo("Questioner"));
        Assert.That(comments[0].ByModAuthor, Is.False);
        Assert.That(comments[1].Author, Is.EqualTo("ModAuthor"));
        Assert.That(comments[1].ByModAuthor, Is.True, "Nexus marks the author's posts with comment-author");
    }

    [Test]
    public void ShouldNotReadTheLockedAndStickyLabelsOrTheTimestampAsTheComment()
    {
        var comments = NexusPageReader.ExtractComments(QuestionAndAnswer, "posts");

        foreach (var comment in comments)
        {
            Assert.That(comment.Text, Does.Not.Contain("Locked"));
            Assert.That(comment.Text, Does.Not.Contain("Sticky"));
            Assert.That(comment.Text, Does.Not.Contain("2026"));
            Assert.That(comment.Text, Does.Not.Contain("Add a reply"));
        }
    }

    [Test]
    public void ShouldReadABugReportsConversationInOrder()
    {
        var posts = NexusPageReader.ExtractComments(BugThread, "bugs");

        Assert.That(posts, Has.Count.EqualTo(2));
        Assert.That(posts[0].Id, Is.EqualTo("852000"), "the report itself comes first");
        Assert.That(posts[0].Author, Is.EqualTo("Reporter"));
        Assert.That(posts[0].Text, Is.EqualTo("The game won't open with March and Ryis installed after replacing the files."));
        Assert.That(posts[1].Author, Is.EqualTo("ModAuthor"));
        Assert.That(posts[1].Text, Does.StartWith("sorry about that!"));
        Assert.That(posts[1].ParentId, Is.Null, "the tracker's replies are flat");
    }

    [Test]
    public void ShouldRecogniseNexussBotCheckRatherThanReadItAsAnEmptyPage()
    {
        const string challenge =
            "<!DOCTYPE html><html lang=\"en-US\"><head><title>Just a moment...</title>" +
            "<script src=\"https://challenges.cloudflare.com/turnstile/v0/api.js\"></script></head></html>";

        Assert.That(NexusPageReader.LooksLikeABotCheck(challenge), Is.True);
        Assert.That(NexusPageReader.LooksLikeABotCheck(QuestionAndAnswer), Is.False);
        Assert.That(NexusPageReader.LooksLikeABotCheck(null), Is.False);
    }

    // Somebody's own markup inside a comment: a list item left open until its list ends. It must not
    // leave the comment open, or the next comment on the page reads as a reply to it.
    [Test]
    public void ShouldNotLetAnUnclosedListItemInAPostSwallowTheNextPost()
    {
        const string html = """
            <ol>
            <li class="comment" id="comment-1"><span class="comment-name"><a href="#">One</a></span>
              <div class="comment-content"><div class="comment-content-text">My load order:<ul><li>first mod<li>second mod</ul></div></div></li>
            <li class="comment" id="comment-2"><span class="comment-name"><a href="#">Two</a></span>
              <div class="comment-content"><div class="comment-content-text">Unrelated top-level comment here.</div></div></li>
            </ol>
            """;

        var comments = NexusPageReader.ExtractComments(html, "posts");

        Assert.That(comments.Select(comment => comment.Id), Is.EqualTo(new[] { "1", "2" }));
        Assert.That(comments[1].ParentId, Is.Null);
    }

    [Test]
    public void ShouldNotTakeAPageThatMerelyLoadsCloudflaresScriptForTheBotCheck()
    {
        const string page =
            "<html><head><title>March Expanded at Fields of Mistria Nexus</title>" +
            "<script src=\"https://challenges.cloudflare.com/turnstile/v0/api.js\"></script></head><body></body></html>";

        Assert.That(NexusPageReader.LooksLikeABotCheck(page), Is.False);
    }
}
