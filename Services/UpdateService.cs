using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BonbonPet.Services;

public enum UpdateStatus { UpdateAvailable, UpToDate, NoRelease, Failed, Canceled }

public sealed record UpdateCheckResult(
    UpdateStatus Status,
    string CurrentVersion,
    string? LatestVersion,
    Uri? ReleasePageUri,
    string Message)
{
    public bool UpdateAvailable => Status == UpdateStatus.UpdateAvailable;
}

/// <summary>
/// Queries a public GitHub repository only when CheckAsync is called. This service
/// returns a release page; it never downloads or executes a release asset.
/// </summary>
public sealed class UpdateService : IDisposable
{
    private static readonly Regex StableVersionPattern = new(
        @"^[vV]?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$",
        RegexOptions.CultureInvariant);
    private readonly HttpClient _client;
    private readonly Uri _latestReleaseApi;
    private readonly string _releasePathPrefix;
    private readonly Version _currentVersion;
    private readonly TimeSpan _timeout;

    public string CurrentVersion { get; }
    public Uri ReleasesPageUri { get; }

    public UpdateService(string owner, string repository, HttpMessageHandler? handler = null,
        string? currentVersion = null, TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(owner) ||
            !Regex.IsMatch(owner, @"^[A-Za-z0-9][A-Za-z0-9-]{0,38}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Invalid GitHub owner.", nameof(owner));
        if (string.IsNullOrWhiteSpace(repository) || repository is "." or ".." ||
            !Regex.IsMatch(repository, @"^[A-Za-z0-9_.-]{1,100}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Invalid GitHub repository.", nameof(repository));
        if (!TryParseStableVersion(currentVersion ?? GetAssemblyVersion(), out var parsedVersion))
            throw new ArgumentException("The current app version must be a stable semantic version.", nameof(currentVersion));

        _currentVersion = parsedVersion;
        CurrentVersion = _currentVersion.ToString(3);
        _timeout = timeout ?? TimeSpan.FromSeconds(12);
        if (_timeout <= TimeSpan.Zero || _timeout > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(timeout), "Use a timeout between zero and 30 seconds.");

        _latestReleaseApi = new Uri($"https://api.github.com/repos/{owner}/{repository}/releases/latest");
        ReleasesPageUri = new Uri($"https://github.com/{owner}/{repository}/releases");
        _releasePathPrefix = $"/{owner}/{repository}/releases/tag/";
        _client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = Timeout.InfiniteTimeSpan,
            MaxResponseContentBufferSize = 2 * 1024 * 1024
        };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd($"TantanPet/{CurrentVersion}");
        _client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_timeout);
        try
        {
            using var response = await _client.GetAsync(_latestReleaseApi,
                HttpCompletionOption.ResponseContentRead, timeoutSource.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return Result(UpdateStatus.NoRelease, "仓库可能为私有或尚无正式版本。私有仓库请登录 GitHub 发布页查看。");
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                return Result(UpdateStatus.Failed, "GitHub 暂时限制了查询，请稍后再试。");
            if (!response.IsSuccessStatusCode)
                return Result(UpdateStatus.Failed, "暂时无法查询更新，请稍后再试。");

            var json = await response.Content.ReadAsStringAsync(timeoutSource.Token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            var release = document.RootElement;
            if (release.ValueKind != JsonValueKind.Object ||
                !TryGetBoolean(release, "draft", out var draft) ||
                !TryGetBoolean(release, "prerelease", out var prerelease))
                return InvalidRelease();
            if (draft || prerelease)
                return Result(UpdateStatus.NoRelease, "暂未发布正式版本。");
            if (!TryGetString(release, "tag_name", out var tag) ||
                !TryParseStableVersion(tag, out var latestVersion) ||
                !TryGetString(release, "html_url", out var url) ||
                !TryValidateReleasePage(url, tag, out var releasePage))
                return InvalidRelease();

            var latest = latestVersion.ToString(3);
            return latestVersion.CompareTo(_currentVersion) > 0
                ? Result(UpdateStatus.UpdateAvailable, $"发现新版本 {latest}，当前版本 {CurrentVersion}。", latest, releasePage)
                : Result(UpdateStatus.UpToDate, $"当前版本 {CurrentVersion} 已是最新正式版本。", latest, releasePage);
        }
        catch (OperationCanceledException)
        {
            return cancellationToken.IsCancellationRequested
                ? Result(UpdateStatus.Canceled, "已取消检查更新。")
                : Result(UpdateStatus.Failed, "检查更新超时，请检查网络后重试。");
        }
        catch (HttpRequestException)
        {
            return Result(UpdateStatus.Failed, "无法连接到 GitHub，请检查网络后重试。");
        }
        catch (JsonException)
        {
            return InvalidRelease();
        }
    }

    // Keep the release tag parser strict: preview tags must never masquerade as
    // stable updates, and build metadata does not affect version precedence.
    internal static bool TryParseStableVersion(string? value, [NotNullWhen(true)] out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128) return false;
        var match = StableVersionPattern.Match(value);
        if (!match.Success ||
            !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
            !int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
            return false;
        version = new Version(major, minor, patch);
        return true;
    }

    private bool TryValidateReleasePage(string value, string tag, out Uri? page)
    {
        page = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            return false;
        var path = Uri.UnescapeDataString(uri.AbsolutePath);
        if (!path.StartsWith(_releasePathPrefix, StringComparison.OrdinalIgnoreCase) ||
            !path[_releasePathPrefix.Length..].Equals(tag, StringComparison.Ordinal))
            return false;
        page = uri;
        return true;
    }

    private static bool TryGetString(JsonElement element, string name, [NotNullWhen(true)] out string? value)
    {
        value = null;
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String) return false;
        value = property.GetString();
        return !string.IsNullOrEmpty(value);
    }

    private static bool TryGetBoolean(JsonElement element, string name, out bool value)
    {
        value = false;
        if (!element.TryGetProperty(name, out var property) ||
            property.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        value = property.GetBoolean();
        return true;
    }

    private static string GetAssemblyVersion()
    {
        var assembly = typeof(UpdateService).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (TryParseStableVersion(informational, out var version)) return version.ToString(3);
        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    private UpdateCheckResult InvalidRelease() => Result(UpdateStatus.Failed, "发布信息暂时不可用，请稍后再试。");
    private UpdateCheckResult Result(UpdateStatus status, string message, string? latest = null, Uri? page = null) =>
        new(status, CurrentVersion, latest, page, message);

    public void Dispose() => _client.Dispose();
}
