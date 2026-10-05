using System.Net;
using System.Text.Json;
using FufuLauncher.Models.Miyoushe;
using FufuLauncher.Models.MiHoYo.Identity;
using FufuLauncher.Services.Miyoushe;

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name); checks++;
}
JsonElement Json(string value) { using var doc = JsonDocument.Parse(value); return doc.RootElement.Clone(); }
HttpResponseMessage Response(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(json) };
const string PostJson = """{"post":{"post_id":"123","game_id":2,"uid":"42","subject":"Test","created_at":1700000000,"structured_content":"[]"},"user":{"uid":"42","nickname":"Author"},"stat":{"view_num":10,"like_num":2,"reply_num":3},"image_list":[],"topics":[]}""";

var signature = MiyousheClient.CreateDs("salt", false, new Dictionary<string, string> { ["z"] = "中文", ["a"] = "x y" }, "{}", 1700000000, "123456");
var expected = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes("salt=salt&t=1700000000&r=123456&b={}&q=a=x y&z=中文"))).ToLowerInvariant();
Check(signature == "1700000000,123456," + expected, "DS2 sorts raw query values and hashes the exact body");
Check(MiyousheClient.CreateDs("salt", true, new Dictionary<string, string> { ["a"] = "x" }, "body", 1700000000, "abcdef") ==
    MiyousheClient.CreateDs("salt", true, new Dictionary<string, string>(), "", 1700000000, "abcdef"), "DS1 excludes body and query");
Check(MiyousheClient.CreateDs("r3KppdID2yT6ht6P7MxzQykauJj0Cmtg", true, new Dictionary<string, string>(), "",
    1790954918, "xwwZ3s") == "1790954918,xwwZ3s,21dd08e30ea1f056d62a962ae6c35e4c",
    "Web DS matches the output of the official webpage signature function");

string malicious = """<script>alert(1)</script><iframe src="https://evil.test"></iframe><p onclick="evil()" style="font-size:24px;background:url(https://evil.test)">Text <a href="https://example.com/?a=1&amp;b=2">named link</a><a href="javascript:evil()">bad</a><img src="https://example.com/image.png" onerror="evil()"></p>""";
string clean = CommunityContent.Sanitize(malicious);
Check(!clean.Contains("script") && !clean.Contains("iframe") && !clean.Contains("onclick") && !clean.Contains("onerror") && !clean.Contains("javascript:") && !clean.Contains("background:"), "Untrusted HTML cannot execute or embed a page");
Check(clean.Contains("font-size:24px") && clean.Contains("href=\"https://example.com/?a=1&amp;b=2\""), "HTML keeps font sizes and named href links");
var post = CommunityPost.Parse(Json(PostJson));
var delta = JsonSerializer.Serialize(new object[] { new { insert = "标题" }, new { insert = "\n", attributes = new { header = 2, align = "center" } },
    new { insert = "https://example.com/test正文不能被包含，下一句" }, new { insert = " 链接", attributes = new { link = "https://example.com/target", bold = true, size = "large" } } });
var article = post with { Raw = Json(JsonSerializer.Serialize(new { post = new { structured_content = delta }, image_list = Array.Empty<object>() })) };
string html = CommunityContent.Render(article, true);
Check(html.Contains("<h2 style=\"text-align:center\">标题</h2>"), "Quill line attributes apply to the preceding heading");
Check(html.Contains("href=\"https://example.com/test\"") && !html.Contains("href=\"https://example.com/test正文"), "Bare URLs stop before Chinese text");
Check(html.Contains("href=\"https://example.com/target\"") && html.Contains("font-size:22px"), "Structured content keeps links and font sizes");
var album = post with { Raw = Json(JsonSerializer.Serialize(new { post = new { view_type = 2, structured_content = "[{\"insert\":\"图集说明\"}]" },
    image_list = new[] { new { url = "https://example.com/album.png" } } })) };
Check(CommunityContent.Render(album, true).Contains("src=\"https://example.com/album.png\""), "Image albums retain attachments when the structured body only contains a caption");
var legacyAlbum = post with { Raw = Json(JsonSerializer.Serialize(new { post = new { structured_content = "[]",
    content = "{\"imgs\":[\"https://example.com/first.png\",\"https://example.com/second.jpg\"],\"describe\":\"图集正文\"}" }, image_list = Array.Empty<object>() })) };
string legacyHtml = CommunityContent.Render(legacyAlbum, true);
Check(legacyHtml.Contains("src=\"https://example.com/first.png\"") && legacyHtml.Contains("src=\"https://example.com/second.jpg\"") &&
    legacyHtml.Contains("图集正文") && !legacyHtml.Contains("&quot;imgs&quot;"), "Legacy content JSON renders album images and captions instead of raw JSON");
var commentOptions = new CommunityCommentOptions { ActionRoot = "https://miyoushe-native.invalid/test-session/", Title = "评论", Hot = "热门",
    Latest = "最新", Oldest = "最早", OnlyAuthor = "只看楼主", More = "加载更多", SubReplies = "查看 {0} 条回复", Empty = "暂无评论", Loading = "加载中", HasMore = true,
    Like = "点赞", Liked = "已赞", Reply = "回复" };
var comment = new CommunityReply("1", "10", "42", "<script>Author</script>", "Today", "<img src=x onerror=evil()>\nhttps://example.com/test中文",
    new[] { "javascript:evil()", "https://example.com/reply.png" }, Array.Empty<CommunityReply>(), 2) { Avatar = "javascript:evil()" };
string commentsHtml = CommunityContent.RenderComments(new[] { comment }, commentOptions);
Check(commentsHtml.Contains("&lt;script&gt;Author&lt;/script&gt;") && commentsHtml.Contains("&lt;img") && !commentsHtml.Contains("<script>") && !commentsHtml.Contains("javascript:"),
    "Inline comments escape author and body HTML and reject executable image URLs");
Check(commentsHtml.Contains("href=\"https://example.com/test\"") && commentsHtml.Contains("src=\"https://example.com/reply.png\"") &&
    commentsHtml.Contains("test-session/floor/10") && commentsHtml.Contains("test-session/more"), "Inline comments retain safe links, images, subreply and pagination actions");
Check(CommunityContent.RenderComments(Array.Empty<CommunityReply>(), commentOptions with { IsLoading = true }).Contains("加载中") &&
    !CommunityContent.RenderComments(Array.Empty<CommunityReply>(), commentOptions with { IsLoading = true }).Contains("test-session/more"), "Loading inline comments do not expose another pagination request");
Check(!commentsHtml.Contains("test-session/publish") && commentsHtml.Contains("test-session/like/1") && commentsHtml.Contains("test-session/reply/1"),
    "Inline interaction links retain the action root and reply ID without a duplicate publish button");
var likedPost = CommunityPost.Parse(Json(PostJson[..^1] + ",\"self_operation\":{\"attitude\":1}}"));
var likedReply = CommunityReply.Parse(Json("""{"reply":{"reply_id":"55","floor_id":"10","content":"Comment"},"stat":{"like_num":12},"self_operation":{"attitude":1}}"""));
Check(likedPost.IsLiked && likedReply.IsLiked && likedReply.LikeCount == 12, "Post and reply like states come from self_operation");
var avatarReply = CommunityReply.Parse(Json("""{"reply":{"reply_id":"56","uid":"42"},"user":{"nickname":"Author","avatar_url":"//example.com/avatar.png"},"sub_replies":[{"reply":{"reply_id":"57"},"user":{"uid":"43","avatar_url":"javascript:bad()","avatar":"https://example.com/child.png"}}]}"""));
Check(avatarReply.AuthorId == "42" && avatarReply.Avatar == "https://example.com/avatar.png" && avatarReply.Children[0].Avatar == "https://example.com/child.png",
    "Replies and subreplies retain safe avatar URLs and recover a missing user UID from reply metadata");
Check(!CommunityUser.IsValidId("0") && !CommunityUser.IsValidId("42&other=1") && CommunityUser.IsValidId("42") &&
    CommunityContent.RenderComments(new[] { avatarReply }, commentOptions).Contains("src=\"https://example.com/avatar.png\"") &&
    CommunityContent.RenderComments(new[] { avatarReply }, commentOptions).Contains("accountCenter/postList?id=42"),
    "Comment avatars retain the matching profile link and reject invalid profile IDs");
Check(!JsonSerializer.Serialize(likedPost).Contains("IsLiked"), "Local post snapshots omit account-specific like state");
Check(!CommunityContent.TryInternalLink(new Uri("https://evil.test/ys/article/123"), out _, out _), "External lookalike links do not enter native routes");
Check(CommunityContent.TryInternalLink(new Uri("https://www.miyoushe.com/ys/accountCenter/postList?id=42&id=42"), out var kind, out var uid) && kind == CommunityFeed.User && uid == "42", "Duplicate query keys do not break author navigation");

var ctx = new AccountContext("cn_42", ServerType.Cn, new Dictionary<string, string>
{
    ["account_id"] = "42", ["cookie_token"] = "cookie-secret", ["ltuid"] = "42", ["ltoken"] = "login-secret", ["stoken"] = "NEVER-SEND"
}, new("42", "mid-secret"), new("device-id", "bbs-id", "real-device-fp", "Device", "12", "Model", DateTimeOffset.UtcNow), new("Mozilla/5.0 Mobile miHoYoBBS/2.112.0", "okhttp"));
var requests = new List<(string Path, Dictionary<string, string> Headers)>();
var handler = new FakeHandler(async (request, ct) =>
{
    requests.Add((request.RequestUri!.AbsolutePath, request.Headers.ToDictionary(h => h.Key.ToLowerInvariant(), h => string.Join(";", h.Value))));
    await Task.CompletedTask;
    return Response("""{"retcode":0,"data":{"list":[],"is_last":true}}""");
});
using (var client = new MiyousheClient(ctx, handler))
{
    await client.GetFeedAsync(new(CommunityFeed.Search, Target: "中文"), "", default);
    Check(!requests[0].Headers.ContainsKey("cookie") && !requests[0].Headers.ContainsKey("x-rpc-device_fp"), "Public searches never receive account cookies");
    await client.GetFeedAsync(new(CommunityFeed.Following), "", default);
    var headers = requests[1].Headers;
    Check(headers["cookie"].Contains("ltoken=") && !headers["cookie"].Contains("cookie_token=") && !headers["cookie"].Contains("stoken="), "Following sends only the required login cookie family");
    Check(headers["x-rpc-device_id"] == "bbs-id" && headers["x-rpc-device_fp"] == "real-device-fp" && headers["user-agent"].Contains("2.115.0"), "Signed requests reuse the account's stable identity with a consistent version");
}

var mixedCookies = new Dictionary<string, string>(ctx.Cookies)
{
    ["ltoken_v2"] = "current-ltoken", ["ltuid_v2"] = "42", ["ltmid_v2"] = "current-ltmid",
    ["cookie_token_v2"] = "current-cookie", ["account_id_v2"] = "42", ["account_mid_v2"] = "current-account-mid"
};
var inheritedHeaders = new List<string>();
using (var client = new MiyousheClient(ctx with { Cookies = mixedCookies }, new FakeHandler((request, _) =>
{
    inheritedHeaders.Add(string.Join("; ", request.Headers.GetValues("Cookie")));
    return Task.FromResult(Response("""{"retcode":0,"data":{"list":[],"is_last":true}}"""));
})))
{
    await client.GetFeedAsync(new(CommunityFeed.Following), "", default);
    await client.GetFeedAsync(new(CommunityFeed.Favorites), "", default);
    Check(inheritedHeaders[0].Contains("ltoken_v2=current-ltoken") && inheritedHeaders[0].Contains("ltuid_v2=42") &&
        inheritedHeaders[0].Contains("ltmid_v2=current-ltmid") && !inheritedHeaders[0].Contains("ltoken=") && !inheritedHeaders[0].Contains("cookie_token"),
        "Following inherits the complete current V2 login family instead of a retained legacy token");
    Check(inheritedHeaders[1].Contains("cookie_token_v2=current-cookie") && inheritedHeaders[1].Contains("account_id_v2=42") &&
        inheritedHeaders[1].Contains("account_mid_v2=current-account-mid") && inheritedHeaders[1].Contains("ltmid_v2=current-ltmid") &&
        !inheritedHeaders[1].Contains("cookie_token=") && !inheritedHeaders[1].Contains("; mid=") && !inheritedHeaders[1].Contains("stoken="),
        "Account requests include V2 binding fields without sending raw mid or stoken");
}

int incompleteCalls = 0;
foreach (var invalidCookies in new[]
{
    new Dictionary<string,string> { ["ltoken_v2"] = "token", ["ltuid_v2"] = "42" },
    new Dictionary<string,string> { ["ltoken"] = "token", ["ltuid_v2"] = "42" },
    new Dictionary<string,string> { ["ltoken_v2"] = "token", ["ltuid_v2"] = "99", ["ltmid_v2"] = "binding" }
})
{
    using var client = new MiyousheClient(ctx with { Cookies = invalidCookies }, new FakeHandler((_, _) =>
    {
        incompleteCalls++; return Task.FromResult(Response("""{"retcode":0,"data":{"list":[],"is_last":true}}"""));
    }));
    bool rejected = false;
    try { await client.GetFeedAsync(new(CommunityFeed.Following), "", default); } catch (CommunityApiException e) when (e.LoginExpired) { rejected = true; }
    Check(rejected && !client.IsAuthenticated && incompleteCalls == 0, "Incomplete, mixed or other-account credentials cannot authenticate: " + checks);
}

string? fallbackCookies = null;
using (var client = new MiyousheClient(ctx with { Cookies = new Dictionary<string,string>(mixedCookies.Where(k => k.Key != "ltmid_v2")) }, new FakeHandler((request, _) =>
{
    fallbackCookies = string.Join("; ", request.Headers.GetValues("Cookie"));
    return Task.FromResult(Response("""{"retcode":0,"data":{"list":[],"is_last":true}}"""));
})))
{
    await client.GetFeedAsync(new(CommunityFeed.Following), "", default);
    Check(fallbackCookies!.Contains("ltoken=login-secret") && fallbackCookies.Contains("ltuid=42") && !fallbackCookies.Contains("ltoken_v2="),
        "An incomplete V2 family falls back to a complete V1 family without mixing identifiers");
}

var writes = new List<(string Path, string Body, Dictionary<string, string> Headers)>();
using (var client = new MiyousheClient(ctx, new FakeHandler(async (request, ct) =>
{
    writes.Add((request.RequestUri!.AbsolutePath, await request.Content!.ReadAsStringAsync(ct),
        request.Headers.ToDictionary(h => h.Key.ToLowerInvariant(), h => string.Join(";", h.Value))));
    return Response("""{"retcode":0,"data":{}}""");
})))
{
    await client.SetPostLikeAsync("123", true, default);
    await client.SetPostLikeAsync("123", false, default);
    await client.SetReplyLikeAsync("123", "55", true, default);
    Check(writes[0].Path == "/apihub/api/upvotePost" && Json(writes[0].Body).GetProperty("post_id").GetString() == "123" &&
        !Json(writes[0].Body).GetProperty("is_cancel").GetBoolean() && Json(writes[1].Body).GetProperty("is_cancel").GetBoolean(), "Post likes send the requested final state and cancellation flag");
    Check(writes[2].Path == "/apihub/api/upvoteReply" && Json(writes[2].Body).GetProperty("reply_id").GetString() == "55", "Reply likes target the reply ID");
    Check(writes.Take(3).All(w => w.Headers["x-rpc-client_type"] == "2" && w.Headers.ContainsKey("ds") &&
        w.Headers["x-rpc-device_id"] == "bbs-id" && w.Headers["x-rpc-device_fp"] == "real-device-fp" &&
        !w.Headers["cookie"].Contains("stoken=") && !w.Headers["cookie"].Contains("mid=")), "Interaction signatures reuse stable identity and exclude stoken and mid");
    await client.PublishReplyAsync(post, "保留  空格\r\n\"内容\"", null, default);
    var body = Json(writes[3].Body);
    Check(writes[3].Path == "/post/wapi/releaseReply" && body.GetProperty("gids").GetString() == "2" &&
        body.GetProperty("content").GetString() == "保留  空格\n\"内容\"" && !body.TryGetProperty("reply_id", out _) &&
        Json(body.GetProperty("structured_content").GetString()!)[0].GetProperty("insert").GetString() == "保留  空格\n\"内容\"\n", "Root comments preserve text and serialize Quill content without a reply target");
    await client.PublishReplyAsync(post, "楼中楼", "55", default);
    Check(Json(writes[4].Body).GetProperty("reply_id").GetString() == "55", "Subreply publication targets reply_id instead of floor_id or UID");
    Check(writes.Skip(3).All(w => w.Headers["x-rpc-client_type"] == "4" && w.Headers["x-rpc-app_version"] == "2.102.0" &&
        w.Headers["origin"] == "https://www.miyoushe.com" && w.Headers["referer"] == "https://www.miyoushe.com/" &&
        !w.Headers.ContainsKey("x-requested-with") && !w.Headers["user-agent"].Contains("miHoYoBBS/")),
        "Root and nested comments use the official desktop webpage profile");
    Check(writes.Skip(3).All(w =>
    {
        var ds = w.Headers["ds"].Split(',');
        return w.Headers["ds"] == MiyousheClient.CreateDs("r3KppdID2yT6ht6P7MxzQykauJj0Cmtg", true,
            new Dictionary<string, string>(), w.Body, long.Parse(ds[0]), ds[1]);
    }), "Comment requests carry the webpage salt instead of X4 or K2");
    int invalid = 0;
    foreach (var content in new[] { "", " \n", new string('x', 1001) })
        try { await client.PublishReplyAsync(post, content, null, default); } catch (ArgumentException) { invalid++; }
    try { await client.PublishReplyAsync(post, "valid", "invalid", default); } catch (ArgumentException) { invalid++; }
    Check(invalid == 4 && writes.Count == 5, "Invalid drafts and reply targets cannot issue a write");
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    try { await client.PublishReplyAsync(post, "cancelled", null, cancelled.Token); } catch (OperationCanceledException) { }
    Check(writes.Count == 5, "Cancelling before submission sends nothing");
}

int guestWrites = 0, loginErrors = 0;
using (var guest = new MiyousheClient(handler: new FakeHandler((_, _) =>
{
    guestWrites++; return Task.FromResult(Response("""{"retcode":0,"data":{}}"""));
})))
{
    try { await guest.SetPostLikeAsync("123", true, default); } catch (CommunityApiException e) when (e.LoginExpired) { loginErrors++; }
    try { await guest.SetReplyLikeAsync("123", "55", true, default); } catch (CommunityApiException e) when (e.LoginExpired) { loginErrors++; }
    try { await guest.PublishReplyAsync(post, "guest", null, default); } catch (CommunityApiException e) when (e.LoginExpired) { loginErrors++; }
    Check(guestWrites == 0 && loginErrors == 3, "Guest interactions stop locally with a login hint");
}

foreach (var outcome in new[] { "disconnect", "cancel", "malformed", "missing-code", "server-error" })
{
    int calls = 0; bool uncertain = false;
    using var client = new MiyousheClient(ctx, new FakeHandler((_, _) =>
    {
        calls++;
        if (outcome == "disconnect") throw new HttpRequestException("connection lost after write");
        if (outcome == "cancel") throw new OperationCanceledException();
        return Task.FromResult(Response(outcome == "malformed" ? "invalid" : "{}", outcome == "server-error" ? HttpStatusCode.InternalServerError : HttpStatusCode.OK));
    }));
    try { await client.PublishReplyAsync(post, "draft", null, default); } catch (CommunitySubmissionException) { uncertain = true; }
    Check(uncertain && calls == 1, "Unconfirmed comment outcome is never automatically replayed: " + outcome);
}

int submissionCalls = 0, proofCount = 0, webVerificationCalls = 0; bool blocked = false;
using (var client = new MiyousheClient(ctx, new FakeHandler(async (request, ct) =>
{
    string path = request.RequestUri!.AbsolutePath;
    if (path.EndsWith("createVerification") || path.EndsWith("verifyVerification"))
    {
        var ds = request.Headers.GetValues("DS").Single().Split(',');
        if (request.Headers.GetValues("x-rpc-client_type").Single() == "4" &&
            request.Headers.GetValues("DS").Single() == MiyousheClient.CreateDs("r3KppdID2yT6ht6P7MxzQykauJj0Cmtg", true,
                new Dictionary<string, string>(), "", long.Parse(ds[0]), ds[1])) webVerificationCalls++;
    }
    if (path.EndsWith("createVerification")) return Response("""{"retcode":0,"data":{"gt":"official-id","challenge":"initial"}}""");
    if (path.EndsWith("verifyVerification")) return Response("""{"retcode":0,"data":{"challenge":"write-proof"}}""");
    submissionCalls++;
    string content = Json(await request.Content!.ReadAsStringAsync(ct)).GetProperty("content").GetString()!;
    bool proof = request.Headers.Contains("x-rpc-challenge");
    if (proof) proofCount++;
    if (content == "same draft" && !proof) return Response("""{"retcode":1028,"message":"verify"}""");
    return Response("""{"retcode":0,"data":{}}""");
})))
{
    try { await client.PublishReplyAsync(post, "same draft", null, default); } catch (CommunityApiException e) when (e.NeedsVerification) { blocked = true; }
    try { await client.PublishReplyAsync(post, "other draft", null, default); } catch (CommunityApiException) { }
    Check(blocked && submissionCalls == 1, "Comment risk response pauses writes without submitting another draft");
    await client.VerifyAsync((_, _, _) => Task.FromResult<JsonElement?>(Json("""{"geetest_challenge":"x","geetest_validate":"y","geetest_seccode":"z"}""")), default);
    Check(webVerificationCalls == 2, "Comment captcha creation and verification retain the webpage signing profile");
    await client.PublishReplyAsync(post, "other draft", null, default);
    Check(proofCount == 0, "Verification proof cannot move to a changed comment body");
    await client.PublishReplyAsync(post, "same draft", null, default);
    try { await client.PublishReplyAsync(post, "same draft", null, default); } catch (CommunityApiException) { }
    Check(proofCount == 1 && client.NeedsVerification, "Comment proof matches the exact draft and is consumed only once");
}

int riskCalls = 0;
var riskHandler = new FakeHandler((request, ct) =>
{
    riskCalls++;
    string path = request.RequestUri!.AbsolutePath;
    if (path.EndsWith("createVerification")) return Task.FromResult(Response("""{"retcode":0,"data":{"gt":"official-captcha-id","challenge":"initial-challenge"}}"""));
    if (path.EndsWith("verifyVerification")) return Task.FromResult(Response("""{"retcode":0,"data":{"challenge":"verified-challenge"}}"""));
    if (path.EndsWith("getNewsList")) return Task.FromResult(Response("""{"retcode":0,"data":{"list":[],"is_last":true}}"""));
    if (request.Headers.TryGetValues("x-rpc-challenge", out var challenge) && challenge.Single() == "verified-challenge")
        return Task.FromResult(Response("{\"retcode\":0,\"data\":{\"post\":" + PostJson + "}}"));
    return Task.FromResult(Response("""{"retcode":1034,"message":"verify"}"""));
});
using (var client = new MiyousheClient(ctx, riskHandler))
{
    try { await client.GetPostAsync("123", 2, default); } catch (CommunityApiException e) { Check(e.NeedsVerification, "1034 becomes an explicit verification state"); }
    try { await client.GetFeedAsync(new(CommunityFeed.News), "", default); } catch (CommunityApiException) { }
    Check(riskCalls == 1, "Risk pauses subsequent requests without automatic retries");
    try { await client.VerifyAsync((_, _, _) => Task.FromResult<JsonElement?>(null), default); } catch (OperationCanceledException) { }
    Check(client.NeedsVerification, "Cancelling the captcha keeps requests paused");
    await client.VerifyAsync((_, _, _) => Task.FromResult<JsonElement?>(Json("""{"geetest_challenge":"x","geetest_validate":"y","geetest_seccode":"z"}""")), default);
    await client.GetFeedAsync(new(CommunityFeed.News), "", default);
    var recovered = await client.GetPostAsync("123", 2, default);
    Check(recovered.Id == "123" && !client.NeedsVerification, "Official proof is attached to the explicitly retried request");
    try { await client.GetPostAsync("123", 2, default); } catch (CommunityApiException) { }
    Check(client.NeedsVerification, "Proof is consumed once and a repeated risk blocks again");
}

int limitedCalls = 0;
using (var client = new MiyousheClient(handler: new FakeHandler((request, ct) =>
{
    limitedCalls++; var response = Response("{}", HttpStatusCode.TooManyRequests);
    response.Headers.RetryAfter = new(TimeSpan.FromSeconds(30)); return Task.FromResult(response);
})))
{
    for (int i = 0; i < 2; i++) try { await client.GetFeedAsync(new(CommunityFeed.News), "", default); } catch (CommunityApiException) { }
    Check(limitedCalls == 1, "HTTP 429 enforces the server cooldown");
    using var cancel = new CancellationTokenSource(); cancel.Cancel();
    try { await client.GetPostAsync("123", 2, cancel.Token); } catch (Exception) { }
    Check(limitedCalls == 1, "Cancelled navigation cannot issue another request");
}

string libraryPath = Path.Combine(Path.GetTempPath(), "miyoushe-tests-" + Guid.NewGuid() + ".json");
try
{
    var library = new CommunityLibrary(libraryPath);
    await library.ToggleAsync(post, default); await library.VisitAsync(post, default); await library.VisitAsync(post, default);
    var loaded = new CommunityLibrary(libraryPath); await loaded.LoadAsync(default);
    Check(loaded.Contains(post.Id) && loaded.History.Count == 1, "Bookmarks persist and history is deduplicated");
    Check(!File.ReadAllText(libraryPath).Contains("Raw"), "Local storage excludes raw API content and credentials");
    await File.WriteAllTextAsync(libraryPath, "invalid-json");
    var corrupt = new CommunityLibrary(libraryPath);
    try { await corrupt.LoadAsync(default); } catch (JsonException) { }
    try { await corrupt.ToggleAsync(post, default); } catch (IOException) { }
    Check(File.ReadAllText(libraryPath) == "invalid-json", "Unreadable libraries are preserved instead of overwritten");
}
finally { File.Delete(libraryPath); File.Delete(libraryPath + ".tmp"); }

if (args.Contains("--live"))
{
    using var client = new MiyousheClient();
    var navigation = await client.GetNavigationAsync(default);
    Check(navigation.Games.Any(g => g.Id == 2) && navigation.Forums.Any(f => f.Id == 26), "LIVE: games and forums");
    foreach (var feed in new[] { new FeedRequest(CommunityFeed.News, Sort: 1), new FeedRequest(CommunityFeed.Forum), new FeedRequest(CommunityFeed.Search, Target: "原神", Sort: 1) })
    {
        var page = await client.GetFeedAsync(feed, "", default);
        Check(page.Items.Count > 0, "LIVE: " + feed.Kind);
        if (feed.Kind != CommunityFeed.News) continue;
        var full = await client.GetPostAsync(page.Items[0].Id, 2, default);
        Check(CommunityContent.Render(full, true).Contains("<body>"), "LIVE: post detail and rich rendering");
        var comments = await client.GetRepliesAsync(full, "", 0, false, default);
        Check(comments.Items.Count > 0, "LIVE: comments");
        var userPage = await client.GetFeedAsync(new(CommunityFeed.User, Target: full.AuthorId), "", default);
        Check(userPage.Items.Count > 0, "LIVE: author posts");
        if (full.Topics.Count > 0)
        {
            var topics = await client.GetFeedAsync(new(CommunityFeed.Topic, Target: full.Topics[0].Id, Sort: 0), "", default);
            Check(topics.Items.Count > 0, "LIVE: topic posts");
        }
    }
}
Console.WriteLine($"{checks} checks passed.");

sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
}
