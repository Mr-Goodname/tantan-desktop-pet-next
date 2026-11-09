using System.Net;
using System.Text;
using System.Text.Json;
using BonbonPet.Services;

int checks = 0;
void Check(bool pass, string message) { if (!pass) throw new Exception(message); checks++; }

foreach (var (tag, expected) in new[]
{
    ("v0.4.0", "0.4.0"), ("V1.10.2", "1.10.2"), ("0.0.0", "0.0.0"),
    ("12.24.36+build.123", "12.24.36")
})
{
    Check(UpdateService.TryParseStableVersion(tag, out var version) && version!.ToString(3) == expected,
        $"Stable version parsing: {tag}");
}
foreach (var tag in new string?[]
{
    null, "", " ", "0.4", "0.4.0.0", "v0.4.0-rc.1", "0.4.0-beta", "01.4.0", "0.04.0",
    "0.4.00", "-1.0.0", "release-0.4.0", "v0.4.0 ", "0.4.0+", "0.4.0+bad/metadata", "2147483648.0.0"
})
    Check(!UpdateService.TryParseStableVersion(tag, out _), $"Invalid/preview version rejected: {tag}");

string Release(string tag = "v0.4.0", string? url = null, bool draft = false, bool prerelease = false) =>
    JsonSerializer.Serialize(new
    {
        tag_name = tag,
        html_url = url ?? $"https://github.com/test-owner/BonbonPet/releases/tag/{tag}",
        draft,
        prerelease
    });

async Task<UpdateCheckResult> Query(string json, string current = "0.3.0", HttpStatusCode status = HttpStatusCode.OK)
{
    using var service = new UpdateService("test-owner", "BonbonPet", new StubHandler((request, token) =>
        Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") })), current);
    return await service.CheckAsync();
}

using (var service = new UpdateService("test-owner", "BonbonPet", new StubHandler((request, token) =>
{
    Check(request.RequestUri!.AbsoluteUri == "https://api.github.com/repos/test-owner/BonbonPet/releases/latest",
        "Only public stable latest-release API is queried");
    Check(request.Headers.UserAgent.ToString() == "BonbonPet/0.3.0", "GitHub requires versioned User-Agent");
    Check(request.Headers.Authorization is null, "No token sent");
    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Release()) });
}), "0.3.0"))
{
    var result = await service.CheckAsync();
    Check(result.Status == UpdateStatus.UpdateAvailable && result.UpdateAvailable, "New release offers an update");
    Check(result.CurrentVersion == "0.3.0" && result.LatestVersion == "0.4.0", "Current and latest versions returned");
    Check(result.ReleasePageUri?.AbsoluteUri == "https://github.com/test-owner/BonbonPet/releases/tag/v0.4.0",
        "Trusted release page returned");
}

Check((await Query(Release(), "0.4.0")).Status == UpdateStatus.UpToDate, "Equal release is current");
Check((await Query(Release(), "0.5.0")).Status == UpdateStatus.UpToDate, "Newer local version is not downgraded");
Check((await Query(Release("v0.10.0"), "0.9.9")).UpdateAvailable, "Numeric version precedence, not lexical comparison");
Check((await Query(Release("v0.4.0+build.123"), "0.4.0")).Status == UpdateStatus.UpToDate,
    "Build metadata does not force an update");
Check((await Query(Release(draft: true))).Status == UpdateStatus.NoRelease, "Draft release ignored");
Check((await Query(Release(prerelease: true))).Status == UpdateStatus.NoRelease, "Preview release ignored");
Check((await Query("", status: HttpStatusCode.NotFound)).Status == UpdateStatus.NoRelease,
    "Repository with no stable release handles 404");
Check((await Query("", status: HttpStatusCode.Forbidden)).Status == UpdateStatus.Failed, "API rate limit handled");
Check((await Query("", status: HttpStatusCode.ServiceUnavailable)).Status == UpdateStatus.Failed, "Server outage handled");
Check((await Query("", status: HttpStatusCode.Redirect)).Status == UpdateStatus.Failed, "Unexpected redirect does not offer an update");

foreach (var json in new[]
{
    "{", "[]", "null", "{}", "{\"draft\":false,\"prerelease\":false,\"tag_name\":42,\"html_url\":\"x\"}",
    "{\"draft\":\"false\",\"prerelease\":false}", Release("v0.4.0-rc.1"), Release("unversioned")
})
{
    var result = await Query(json);
    Check(result.Status == UpdateStatus.Failed && !result.UpdateAvailable && result.ReleasePageUri is null,
        "Malformed release rejected without a crash or usable link");
}

foreach (var url in new[]
{
    "http://github.com/test-owner/BonbonPet/releases/tag/v0.4.0",
    "https://github.com.evil.example/test-owner/BonbonPet/releases/tag/v0.4.0",
    "https://github.com/other-owner/BonbonPet/releases/tag/v0.4.0",
    "https://github.com/test-owner/OtherRepo/releases/tag/v0.4.0",
    "https://github.com/test-owner/BonbonPet/releases/tag/v0.4.0?redirect=evil",
    "https://github.com/test-owner/BonbonPet/releases/tag/v0.4.0#fragment",
    "https://evil@github.com/test-owner/BonbonPet/releases/tag/v0.4.0",
    "https://github.com:444/test-owner/BonbonPet/releases/tag/v0.4.0",
    "https://github.com/test-owner/BonbonPet/releases/tag/v0.5.0",
    "https://github.com/test-owner/BonbonPet/releases/tag/v0.4.0/asset.exe",
    "file:///C:/Windows/System32/cmd.exe"
})
{
    var result = await Query(Release(url: url));
    Check(result.Status == UpdateStatus.Failed && result.ReleasePageUri is null, $"Untrusted release URL rejected: {url}");
}

using (var service = new UpdateService("test-owner", "BonbonPet", new StubHandler((request, token) =>
    throw new HttpRequestException("Simulated offline")), "0.3.0"))
    Check((await service.CheckAsync()).Status == UpdateStatus.Failed, "Offline request handled");

Check((await Query(new string(' ', 2 * 1024 * 1024 + 1))).Status == UpdateStatus.Failed,
    "Oversized response is bounded and handled");

int calls = 0;
using (var service = new UpdateService("test-owner", "BonbonPet", new StubHandler((request, token) =>
{
    calls++;
    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
}), "0.3.0"))
{
    Check(calls == 0, "Constructing the service does not query GitHub");
    await service.CheckAsync();
    Check(calls == 1, "Only explicit CheckAsync queries GitHub");
}

foreach (var (owner, repository) in new[]
{
    ("../other", "BonbonPet"), ("owner", "../other"), ("owner", "."), ("owner", "repo?token=x")
})
{
    bool rejected = false;
    try { using var service = new UpdateService(owner, repository, currentVersion: "0.3.0"); }
    catch (ArgumentException) { rejected = true; }
    Check(rejected, "Unsafe repository configuration rejected before any network request");
}

using (var service = new UpdateService("test-owner", "BonbonPet", new StubHandler(async (request, token) =>
{
    await Task.Delay(Timeout.InfiniteTimeSpan, token);
    return new HttpResponseMessage(HttpStatusCode.OK);
}), "0.3.0", TimeSpan.FromMilliseconds(30)))
{
    var result = await service.CheckAsync();
    Check(result.Status == UpdateStatus.Failed && result.Message.Contains("超时"), "Query has a bounded timeout");
}

using (var cancel = new CancellationTokenSource())
using (var service = new UpdateService("test-owner", "BonbonPet", new StubHandler(async (request, token) =>
{
    await Task.Delay(Timeout.InfiniteTimeSpan, token);
    return new HttpResponseMessage(HttpStatusCode.OK);
}), "0.3.0"))
{
    var query = service.CheckAsync(cancel.Token);
    cancel.Cancel();
    Check((await query).Status == UpdateStatus.Canceled, "User cancellation handled separately from timeout");
}

Console.WriteLine($"Update checks passed: {checks} (version precedence, stable releases, malformed data, trusted URLs, offline/timeout/cancel; no network).");

sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        respond(request, cancellationToken);
}
