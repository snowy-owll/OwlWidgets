using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace OwlWidgets.Updates;

/// <summary>A published release of the application.</summary>
/// <param name="PageUrl">The release page on GitHub, opened in the browser.</param>
internal sealed record ReleaseInfo(SemanticVersion Version, string PageUrl)
{
    /// <summary>
    /// Reads the answer of <c>GET /repos/{owner}/{repo}/releases/latest</c>. A page outside the repository
    /// is not trusted: the list of releases is opened instead.
    /// </summary>
    /// <exception cref="FormatException">The answer has no tag or the tag is not a semantic version.</exception>
    public static ReleaseInfo FromGitHub(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var tag = root.TryGetProperty("tag_name", out var tagName) && tagName.ValueKind == JsonValueKind.String
            ? tagName.GetString()
            : null;
        if (!SemanticVersion.TryParse(tag, out var version))
            throw new FormatException($"The release tag '{tag}' is not a semantic version.");

        var page = root.TryGetProperty("html_url", out var htmlUrl) && htmlUrl.ValueKind == JsonValueKind.String
            ? htmlUrl.GetString()
            : null;
        if (page is null || !page.StartsWith(AppInfo.RepositoryUrl + "/", StringComparison.OrdinalIgnoreCase))
            page = AppInfo.ReleasesUrl;
        return new ReleaseInfo(version, page);
    }
}

/// <summary>Where the latest release is learned from.</summary>
internal interface IReleaseSource
{
    /// <summary>The latest release, or <c>null</c> when none is published yet.</summary>
    /// <exception cref="Exception">The network, the server or its answer failed.</exception>
    Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellation);
}

/// <summary>
/// The GitHub REST API without a token: 60 requests an hour from one address, far above one check a day.
/// Drafts and pre-releases are not returned as the latest release.
/// </summary>
internal sealed class GitHubReleaseSource : IReleaseSource, IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public GitHubReleaseSource()
    {
        // GitHub refuses requests without a User-Agent.
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OwlWidgets", AppInfo.Version));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public async Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellation)
    {
        using var response = await _http.GetAsync(AppInfo.LatestReleaseApiUrl, cancellation).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
        return ReleaseInfo.FromGitHub(json);
    }

    public void Dispose() => _http.Dispose();
}
