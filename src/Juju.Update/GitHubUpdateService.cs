using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Juju.Update;

public sealed record AvailableUpdate(ReleaseManifest Manifest, string ReleaseNotes, string ReleaseNotesUrl,
    string PackageUrl, long PackageSize, string TagName);

public sealed record UpdateCheckResult(bool IsUpdateAvailable, AvailableUpdate? Update, string? Error = null,
    bool WasSkippedByCooldown = false);

public sealed record DownloadProgress(long BytesReceived, long? TotalBytes)
{
    public double? Percentage => TotalBytes is > 0 ? Math.Min(100, BytesReceived * 100d / TotalBytes.Value) : null;
}

public sealed record PreparedUpdate(string PackagePath, string ManifestPath, ReleaseManifest Manifest);

public sealed class GitHubUpdateService : IDisposable
{
    private const string LatestManifestUrl = "https://github.com/qlxsply/juju/releases/latest/download/juju_latest.json";
    private static readonly TimeSpan StartupCheckCooldown = TimeSpan.FromHours(24);
    private const long MaximumManifestBytes = 256 * 1024;
    private const long MaximumPackageBytes = 1024L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly string _statePath;
    private readonly string _stagingDirectory;
    private readonly SemaphoreSlim _checkGate = new(1, 1);
    private readonly SemaphoreSlim _downloadGate = new(1, 1);

    public UpdateCheckResult? LastResult { get; private set; }
    public event Action<UpdateCheckResult>? CheckCompleted;

    public GitHubUpdateService(string installRoot, HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        _ownsClient = httpClient is null;
        if ( _ownsClient )
            _http.Timeout = Timeout.InfiniteTimeSpan;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("juju-updater/1.0");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _statePath = Path.Combine(Path.GetFullPath(installRoot), "state", "update-check.json");
        _stagingDirectory = Path.Combine(Path.GetFullPath(installRoot), "staging");
    }

    public async Task<UpdateCheckResult> CheckAsync(string currentVersion, bool manual = false,
        CancellationToken cancellationToken = default)
    {
        await _checkGate.WaitAsync(cancellationToken);
        try
        { return await CheckCoreAsync(currentVersion, manual, cancellationToken); }
        finally { _checkGate.Release(); }
    }

    private async Task<UpdateCheckResult> CheckCoreAsync(string currentVersion, bool manual,
        CancellationToken cancellationToken = default)
    {
        if ( !SemanticVersion.TryParse(currentVersion, out var current) )
            return Publish(new(false, null, "当前版本号无效，无法检查更新。"));

        CheckCache cache;
        try
        {
            cache = await ReadCacheAsync(cancellationToken);
        }
        catch ( Exception ex ) when ( ex is IOException or JsonException or UnauthorizedAccessException )
        {
            cache = new CheckCache();
        }

        var elapsed = cache.LastAttemptUtc is { } priorAttempt ? DateTimeOffset.UtcNow - priorAttempt : TimeSpan.MaxValue;
        if ( !manual && elapsed >= TimeSpan.Zero && elapsed < StartupCheckCooldown )
            return Publish(CompareCached(cache.Latest, current, skipped: true));

        cache = cache with { LastAttemptUtc = DateTimeOffset.UtcNow };
        try
        { await WriteCacheAsync(cache, cancellationToken); }
        catch ( Exception ex ) when ( ex is IOException or UnauthorizedAccessException ) { }

        try
        {
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeout.CancelAfter(TimeSpan.FromSeconds(30));
            var requestToken = requestTimeout.Token;
            var latestUri = new Uri(LatestManifestUrl);
            using var response = await GetTrustedAssetResponseAsync(latestUri, requestToken, cache.ETag);
            if ( response.StatusCode == HttpStatusCode.NotModified && cache.Latest is not null )
                return Publish(CompareCached(cache.Latest, current, skipped: false));
            response.EnsureSuccessStatusCode();

            EnsureTrustedFinalUri(response, latestUri);
            var manifestBytes = await ReadBoundedContentAsync(response.Content, MaximumManifestBytes, requestToken);
            var manifest = ReleaseManifest.Deserialize(manifestBytes);
            manifest.Validate(manifest.Version);
            var version = manifest.ParsedVersion;
            var packageName = $"juju_v{version}_win-x64.zip";
            if ( !string.Equals(manifest.PackageAsset, packageName, StringComparison.Ordinal) )
                throw new InvalidDataException("Latest manifest does not point to the expected win-x64 update package.");
            var tagName = $"juju_v{version}";
            var packageUrl = $"https://github.com/qlxsply/juju/releases/download/{tagName}/{Uri.EscapeDataString(packageName)}";
            var notesUrl = manifest.ReleaseNotesUrl ?? $"https://github.com/qlxsply/juju/releases/tag/{tagName}";
            var releaseNotes = manifest.ReleaseNotes ?? $"发布说明：{notesUrl}";
            var available = new AvailableUpdate(manifest, releaseNotes, ValidateGitHubUrl(notesUrl),
                ValidateGitHubUrl(packageUrl), manifest.PackageSizeBytes, tagName);
            var latest = CachedRelease.From(available);
            var etag = response.Headers.ETag?.ToString();
            cache = cache with { ETag = etag, Latest = latest };
            await WriteCacheAsync(cache, requestToken);
            return Publish(CompareCached(latest, current, skipped: false));
        }
        catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
        {
            throw;
        }
        catch ( OperationCanceledException ex )
        {
            return Publish(new(false, null, "检查更新失败：" + ex.Message));
        }
        catch ( HttpRequestException ex ) when ( ex.StatusCode == HttpStatusCode.NotFound )
        {
            return Publish(new(false, null, "GitHub 最新稳定版尚未发布自动更新清单。"));
        }
        catch ( Exception ex ) when ( ex is not OperationCanceledException )
        {
            return Publish(new(false, null, "检查更新失败：" + ex.Message));
        }
    }

    public async Task<PreparedUpdate> DownloadAsync(AvailableUpdate update, IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _downloadGate.WaitAsync(cancellationToken);
        try
        { return await DownloadCoreAsync(update, progress, cancellationToken); }
        finally { _downloadGate.Release(); }
    }

    private async Task<PreparedUpdate> DownloadCoreAsync(AvailableUpdate update, IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        update.Manifest.Validate(update.Manifest.Version);
        var packageUri = new Uri(ValidateGitHubUrl(update.PackageUrl));
        Directory.CreateDirectory(_stagingDirectory);
        var packagePath = Path.Combine(_stagingDirectory, update.Manifest.PackageAsset);
        var partialPath = packagePath + ".partial";
        var manifestPath = Path.Combine(_stagingDirectory, $"juju_v{update.Manifest.Version}_manifest.json");
        try
        {
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeout.CancelAfter(TimeSpan.FromMinutes(45));
            var requestToken = requestTimeout.Token;
            using var response = await GetTrustedAssetResponseAsync(packageUri, requestToken);
            response.EnsureSuccessStatusCode();
            EnsureTrustedFinalUri(response, packageUri);
            var total = response.Content.Headers.ContentLength;
            if ( total is > MaximumPackageBytes || total is <= 0 )
                throw new InvalidDataException("下载包大小无效或超过允许上限。");
            await using ( var input = await response.Content.ReadAsStreamAsync(requestToken) )
            await using ( var output = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None,
                             81920, FileOptions.Asynchronous | FileOptions.SequentialScan) )
            {
                var buffer = new byte[81920];
                long received = 0;
                while ( true )
                {
                    var count = await input.ReadAsync(buffer, requestToken);
                    if ( count == 0 )
                        break;
                    received = checked(received + count);
                    if ( received > MaximumPackageBytes || (total.HasValue && received > total.Value) )
                        throw new InvalidDataException("下载内容超出声明大小或允许上限。");
                    await output.WriteAsync(buffer.AsMemory(0, count), requestToken);
                    progress?.Report(new DownloadProgress(received, total));
                }
                await output.FlushAsync(requestToken);
                if ( total.HasValue && received != total.Value )
                    throw new IOException("下载内容不完整。");
            }

            await PackageHashVerifier.VerifyAsync(partialPath, update.Manifest.Sha256, requestToken);
            File.Move(partialPath, packagePath, overwrite: true);
            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(update.Manifest, JsonOptions);
            await File.WriteAllBytesAsync(manifestPath, manifestBytes, requestToken);
            return new PreparedUpdate(packagePath, manifestPath, update.Manifest);
        }
        catch
        {
            if ( File.Exists(partialPath) )
                File.Delete(partialPath);
            if ( File.Exists(packagePath) )
                File.Delete(packagePath);
            if ( File.Exists(manifestPath) )
                File.Delete(manifestPath);
            throw;
        }
    }

    public void Dispose()
    {
        if ( _ownsClient )
            _http.Dispose();
    }

    private async Task<byte[]> ReadBoundedContentAsync(HttpContent content, long maximumBytes,
        CancellationToken cancellationToken)
    {
        var declaredLength = content.Headers.ContentLength;
        if ( declaredLength.HasValue && declaredLength.Value > maximumBytes )
            throw new InvalidDataException("Release manifest exceeds the size limit.");
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        while ( true )
        {
            var read = await stream.ReadAsync(bytes, cancellationToken);
            if ( read == 0 )
                break;
            if ( buffer.Length + read > maximumBytes )
                throw new InvalidDataException("Release manifest exceeds the size limit.");
            await buffer.WriteAsync(bytes.AsMemory(0, read), cancellationToken);
        }
        return buffer.ToArray();
    }

    private async Task<CheckCache> ReadCacheAsync(CancellationToken cancellationToken)
    {
        if ( !File.Exists(_statePath) )
            return new CheckCache();
        await using var stream = new FileStream(_statePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<CheckCache>(stream, JsonOptions, cancellationToken) ?? new CheckCache();
    }

    private async Task WriteCacheAsync(CheckCache cache, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        var temp = _statePath + ".tmp";
        try
        {
            await using ( var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None,
                             4096, FileOptions.Asynchronous | FileOptions.WriteThrough) )
            {
                await JsonSerializer.SerializeAsync(stream, cache, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            if ( File.Exists(_statePath) )
                File.Replace(temp, _statePath, null, ignoreMetadataErrors: true);
            else
                File.Move(temp, _statePath);
        }
        finally
        {
            if ( File.Exists(temp) )
                File.Delete(temp);
        }
    }

    private static UpdateCheckResult CompareCached(CachedRelease? latest, SemanticVersion current, bool skipped)
    {
        if ( latest is null )
            return new(false, null, WasSkippedByCooldown: skipped);
        if ( !SemanticVersion.TryParse(latest.Manifest.Version, out var newest) || newest.CompareTo(current) <= 0 )
            return new(false, null, WasSkippedByCooldown: skipped);
        return new(true, latest.ToAvailableUpdate(), WasSkippedByCooldown: skipped);
    }

    private UpdateCheckResult Publish(UpdateCheckResult result)
    {
        LastResult = result;
        try
        { CheckCompleted?.Invoke(result); }
        catch ( Exception ) { /* Presentation notifications cannot invalidate the network result. */ }
        return result;
    }

    private static string ValidateGitHubUrl(string value)
    {
        if ( !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
             !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
             !uri.AbsolutePath.StartsWith("/qlxsply/juju/releases/", StringComparison.Ordinal) )
            throw new InvalidDataException("Release asset URL is not a trusted juju GitHub Release URL.");
        return uri.ToString();
    }

    private static void EnsureTrustedFinalUri(HttpResponseMessage response, Uri requestedUri)
    {
        var uri = response.RequestMessage?.RequestUri ?? requestedUri;
        if ( uri is null || uri.Scheme != Uri.UriSchemeHttps ||
             !new[] { "github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com" }
                 .Contains(uri.Host, StringComparer.OrdinalIgnoreCase) )
            throw new InvalidDataException("Release download redirected to an untrusted host.");
    }

    private async Task<HttpResponseMessage> GetTrustedAssetResponseAsync(Uri initialUri,
        CancellationToken cancellationToken, string? etag = null)
    {
        var current = initialUri;
        for ( var redirects = 0; redirects <= 5; redirects++ )
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            if ( !string.IsNullOrWhiteSpace(etag) )
                request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if ( !IsRedirect(response.StatusCode) )
            {
                EnsureTrustedFinalUri(response, current);
                return response;
            }

            var location = response.Headers.Location;
            response.Dispose();
            if ( location is null || redirects == 5 )
                throw new HttpRequestException("Release asset redirected too many times or omitted its location.");
            current = location.IsAbsoluteUri ? location : new Uri(current, location);
            if ( current.Scheme != Uri.UriSchemeHttps || !IsAllowedReleaseHost(current.Host) )
                throw new InvalidDataException("Release asset redirect points to an untrusted host.");
        }

        throw new HttpRequestException("Release asset redirected too many times.");
    }

    private static bool IsRedirect(HttpStatusCode status) => status is HttpStatusCode.MovedPermanently or
        HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or
        HttpStatusCode.PermanentRedirect;

    private static bool IsAllowedReleaseHost(string host) =>
        new[] { "github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com" }
            .Contains(host, StringComparer.OrdinalIgnoreCase);

    private sealed record CheckCache(string? ETag = null, DateTimeOffset? LastAttemptUtc = null, CachedRelease? Latest = null);
    private sealed record CachedRelease(ReleaseManifest Manifest, string ReleaseNotes, string ReleaseNotesUrl,
        string PackageUrl, long PackageSize, string TagName)
    {
        public static CachedRelease From(AvailableUpdate update) => new(update.Manifest, update.ReleaseNotes,
            update.ReleaseNotesUrl, update.PackageUrl, update.PackageSize, update.TagName);
        public AvailableUpdate ToAvailableUpdate() => new(Manifest, ReleaseNotes, ReleaseNotesUrl, PackageUrl,
            PackageSize, TagName);
    }

}
