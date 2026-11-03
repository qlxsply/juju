using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Juju.Update;
using Xunit;

namespace Juju.Update.Tests;

public sealed class VersionAndManifestTests
{
    [Theory]
    [InlineData("juju_v1.2.3", 1, 2, 3)]
    [InlineData("1.12.0", 1, 12, 0)]
    public void Parses_stable_versions(string input, int major, int minor, int patch)
    {
        Assert.True(SemanticVersion.TryParse(input, out var version));
        Assert.Equal(new SemanticVersion(major, minor, patch), version);
    }

    [Theory]
    [InlineData("v1.2.3")]
    [InlineData("1.2")]
    [InlineData("1.2.3-beta.1")]
    [InlineData("01.2.3")]
    [InlineData("999999999999.2.3")]
    public void Rejects_non_stable_or_malformed_versions(string value) =>
        Assert.False(SemanticVersion.TryParse(value, out _));

    [Fact]
    public void Compares_numeric_version_parts()
    {
        Assert.True(new SemanticVersion(1, 10, 0) > new SemanticVersion(1, 9, 9));
        Assert.True(new SemanticVersion(2, 0, 0) > new SemanticVersion(1, 99, 99));
    }

    [Fact]
    public void Validates_manifest_identity_and_asset_safety()
    {
        var manifest = new ReleaseManifest
        {
            SchemaVersion = 1,
            Version = "1.2.3",
            Channel = "stable",
            Rid = "win-x64",
            PackageAsset = "juju_v1.2.3_win-x64.zip",
            PackageSizeBytes = 10,
            Sha256 = new string('a', 64),
            MinimumLauncherVersion = "1.0.0",
            ReleaseNotesUrl = "https://github.com/qlxsply/juju/releases/tag/juju_v1.2.3"
        };
        manifest.Validate("1.2.3");
        Assert.Throws<InvalidDataException>(() => (manifest with { PackageAsset = "../evil.zip" }).Validate("1.2.3"));
        Assert.Throws<InvalidDataException>(() => (manifest with { Channel = "beta" }).Validate("1.2.3"));
        Assert.Throws<InvalidDataException>(() => (manifest with { ReleaseNotesUrl = "https://attacker.example/x" }).Validate("1.2.3"));
    }

    [Fact]
    public async Task Rejects_downloads_whose_sha256_does_not_match()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(path, "package", Encoding.UTF8);
            var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("different")));
            await Assert.ThrowsAsync<InvalidDataException>(() => PackageHashVerifier.VerifyAsync(path, expected));
        }
        finally
        {
            if ( File.Exists(path) )
                File.Delete(path);
        }
    }

    [Fact]
    public async Task Version_state_is_written_atomically_and_validated()
    {
        var root = Path.Combine(Path.GetTempPath(), "juju-update-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new VersionStateStore(root);
            Assert.Null(await store.ReadAsync());
            await store.WriteAsync(new VersionState("1.2.3", "1.3.0", "1.2.3", "test"));
            Assert.Equal(new VersionState("1.2.3", "1.3.0", "1.2.3", "test"), await store.ReadAsync());
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(root, "state"), "*.tmp"));
            await Assert.ThrowsAsync<InvalidDataException>(() => store.WriteAsync(new VersionState("bad")));
        }
        finally
        {
            if ( Directory.Exists(root) )
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Version_state_commits_or_rolls_back_pending_versions()
    {
        var root = Path.Combine(Path.GetTempPath(), "juju-update-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new VersionStateStore(root);
            await store.WriteAsync(new VersionState("1.2.3"));
            var pending = await store.BeginPendingAsync("1.3.0");
            Assert.Equal("1.2.3", pending.ActiveVersion);
            Assert.Equal("1.3.0", pending.PendingVersion);
            Assert.Equal("1.2.3", pending.PreviousVersion);
            var rolledBack = await store.RollbackPendingAsync();
            Assert.Equal("1.2.3", rolledBack.ActiveVersion);
            Assert.Null(rolledBack.PendingVersion);

            await store.BeginPendingAsync("1.3.0");
            var committed = await store.CommitPendingAsync();
            Assert.Equal("1.3.0", committed.ActiveVersion);
            Assert.Equal("1.2.3", committed.PreviousVersion);
            Assert.Null(committed.PendingVersion);
        }
        finally
        {
            if ( Directory.Exists(root) )
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Version_state_recovers_from_corrupt_primary_using_atomic_backup()
    {
        var root = Path.Combine(Path.GetTempPath(), "juju-update-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new VersionStateStore(root);
            await store.WriteAsync(new VersionState("1.0.0"));
            await store.WriteAsync(new VersionState("1.1.0", PreviousVersion: "1.0.0"));
            await File.WriteAllTextAsync(Path.Combine(root, "state", "active.json"), "corrupt");

            Assert.Equal("1.0.0", (await store.ReadAsync())!.ActiveVersion);
        }
        finally
        {
            if ( Directory.Exists(root) )
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Secure_extractor_rejects_zip_slip_and_duplicate_case_insensitive_paths()
    {
        var root = Path.Combine(Path.GetTempPath(), "juju-update-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var archivePath = Path.Combine(root, "bad.zip");
        try
        {
            using ( var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create) )
            {
                using var stream = archive.CreateEntry("../escape.txt").Open();
                stream.Write(Encoding.UTF8.GetBytes("bad"));
            }
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                SecureZipExtractor.ExtractAsync(archivePath, Path.Combine(root, "stage")));

            File.Delete(archivePath);
            using ( var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create) )
            {
                using ( var first = archive.CreateEntry("App/file.txt").Open() )
                    first.Write(Encoding.UTF8.GetBytes("one"));
                using ( var second = archive.CreateEntry("app/FILE.txt").Open() )
                    second.Write(Encoding.UTF8.GetBytes("two"));
            }
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                SecureZipExtractor.ExtractAsync(archivePath, Path.Combine(root, "stage2")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Secure_extractor_handles_windows_directory_entries_with_backslash_terminators()
    {
        var root = Path.Combine(Path.GetTempPath(), "juju-update-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var archivePath = Path.Combine(root, "windows-paths.zip");
        try
        {
            using ( var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create) )
            {
                var directory = archive.CreateEntry("Editor\\assets\\");
                using var directoryStream = directory.Open();
                directoryStream.Close();
                using ( var file = archive.CreateEntry("Editor\\assets\\index.html").Open() )
                    file.Write(Encoding.UTF8.GetBytes("ok"));
            }

            var destination = Path.Combine(root, "stage");
            await SecureZipExtractor.ExtractAsync(archivePath, destination);
            Assert.Equal("ok", await File.ReadAllTextAsync(Path.Combine(destination, "Editor", "assets", "index.html")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Version_stager_verifies_hash_and_requires_application_entry_point()
    {
        var root = Path.Combine(Path.GetTempPath(), "juju-update-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var packagePath = Path.Combine(root, "package.zip");
        var payload = Encoding.UTF8.GetBytes("apphost");
        try
        {
            using ( var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create) )
            {
                using ( var app = archive.CreateEntry("Juju.App.exe").Open() )
                    app.Write(payload);
                using ( var editor = archive.CreateEntry("Editor/index.html").Open() )
                    editor.Write(Encoding.UTF8.GetBytes("index"));
            }
            var manifest = new ReleaseManifest
            {
                SchemaVersion = 1,
                Version = "1.2.3",
                Channel = "stable",
                Rid = "win-x64",
                PackageAsset = "juju_v1.2.3_win-x64.zip",
                PackageSizeBytes = new FileInfo(packagePath).Length,
                Sha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(packagePath))),
                MinimumLauncherVersion = "1.0.0"
            };

            var staged = await VersionPackageStager.StageAsync(packagePath, root, manifest, "1.0.0");
            Assert.Equal(payload, await File.ReadAllBytesAsync(Path.Combine(staged, "Juju.App.exe")));
            Assert.True(File.Exists(Path.Combine(staged, "Editor", "index.html")));
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                VersionPackageStager.StageAsync(packagePath, root, manifest, "0.9.0"));
        }
        finally
        {
            if ( Directory.Exists(root) )
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Rejects_illegal_state_transitions()
    {
        Assert.Equal(UpdatePhase.Downloading,
            UpdatePhaseTransitions.Transition(UpdatePhase.UpdateAvailable, UpdatePhase.Downloading));
        Assert.False(UpdatePhaseTransitions.CanTransition(UpdatePhase.Completed, UpdatePhase.Applying));
        Assert.Throws<InvalidOperationException>(() =>
            UpdatePhaseTransitions.Transition(UpdatePhase.Completed, UpdatePhase.Applying));
    }

    [Fact]
    public async Task Startup_check_uses_cached_stable_release_for_24_hour_cooldown()
    {
        var root = Path.Combine(Path.GetTempPath(), "juju-update-tests", Guid.NewGuid().ToString("N"));
        var calls = 0;
        var handler = new StubHandler(_ =>
        {
            calls++;
            var manifest = $$"""{"schemaVersion":1,"version":"1.2.3","channel":"stable","rid":"win-x64","packageAsset":"juju_v1.2.3_win-x64.zip","packageSizeBytes":10,"sha256":"{{new string('a', 64)}}","minimumLauncherVersion":"1.0.0","releaseNotesUrl":"https://github.com/qlxsply/juju/releases/tag/juju_v1.2.3","releaseNotes":"notes"}""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifest) };
        });
        try
        {
            using var client = new HttpClient(handler);
            using var updates = new GitHubUpdateService(root, client);
            var first = await updates.CheckAsync("1.0.0");
            var second = await updates.CheckAsync("1.0.0");
            Assert.True(first.IsUpdateAvailable);
            Assert.Equal("1.2.3", first.Update!.Manifest.Version);
            Assert.True(second.IsUpdateAvailable);
            Assert.True(second.WasSkippedByCooldown);
            Assert.Equal(1, calls);
        }
        finally
        {
            if ( Directory.Exists(root) )
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Stable_release_check_rejects_prereleases_and_reports_network_failures()
    {
        var root = Path.Combine(Path.GetTempPath(), "juju-update-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var prereleaseManifest = $$"""{"schemaVersion":1,"version":"2.0.0-beta.1","channel":"stable","rid":"win-x64","packageAsset":"juju_v2.0.0-beta.1_win-x64.zip","packageSizeBytes":10,"sha256":"{{new string('a', 64)}}","minimumLauncherVersion":"1.0.0"}""";
            using ( var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
                       {
                           Content = new StringContent(prereleaseManifest)
                       })) )
            using ( var updates = new GitHubUpdateService(root + "-prerelease", client) )
            {
                var result = await updates.CheckAsync("1.0.0", manual: true);
                Assert.False(result.IsUpdateAvailable);
                Assert.Contains("manifest", result.Error!, StringComparison.OrdinalIgnoreCase);
            }

            using ( var client = new HttpClient(new StubHandler(_ => throw new HttpRequestException("offline"))) )
            using ( var updates = new GitHubUpdateService(root + "-offline", client) )
            {
                var result = await updates.CheckAsync("1.0.0", manual: true);
                Assert.False(result.IsUpdateAvailable);
                Assert.Contains("检查更新失败", result.Error);
            }
        }
        finally
        {
            if ( Directory.Exists(root + "-prerelease") )
                Directory.Delete(root + "-prerelease", recursive: true);
            if ( Directory.Exists(root + "-offline") )
                Directory.Delete(root + "-offline", recursive: true);
        }
    }

    [Fact]
    public async Task Current_release_does_not_require_update_assets_when_no_newer_version_exists()
    {
        var root = Path.Combine(Path.GetTempPath(), "juju-update-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var manifest = $$"""{"schemaVersion":1,"version":"2.0.3","channel":"stable","rid":"win-x64","packageAsset":"juju_v2.0.3_win-x64.zip","packageSizeBytes":10,"sha256":"{{new string('a', 64)}}","minimumLauncherVersion":"1.0.0"}""";
            using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(manifest)
            }));
            using var updates = new GitHubUpdateService(root, client);

            var result = await updates.CheckAsync("2.0.3", manual: true);

            Assert.False(result.IsUpdateAvailable);
            Assert.Null(result.Error);
        }
        finally
        {
            if ( Directory.Exists(root) )
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Missing_latest_manifest_returns_a_friendly_no_update_metadata_result()
    {
        var root = Path.Combine(Path.GetTempPath(), "juju-update-tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
            using var updates = new GitHubUpdateService(root, client);

            var result = await updates.CheckAsync("2.0.4", manual: true);

            Assert.False(result.IsUpdateAvailable);
            Assert.Equal("GitHub 最新稳定版尚未发布自动更新清单。", result.Error);
        }
        finally
        {
            if ( Directory.Exists(root) )
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Manual_check_uses_cached_etag_and_release_when_server_returns_not_modified()
    {
        var root = Path.Combine(Path.GetTempPath(), "juju-update-tests", Guid.NewGuid().ToString("N"));
        var manifestCalls = 0;
        var handler = new StubHandler(request =>
        {
            manifestCalls++;
            if ( request.Headers.IfNoneMatch.Count > 0 )
                return new HttpResponseMessage(HttpStatusCode.NotModified);
            var json = $$"""{"schemaVersion":1,"version":"1.2.3","channel":"stable","rid":"win-x64","packageAsset":"juju_v1.2.3_win-x64.zip","packageSizeBytes":10,"sha256":"{{new string('a', 64)}}","minimumLauncherVersion":"1.0.0"}""";
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
            response.Headers.ETag = new EntityTagHeaderValue("\"release-v1\"");
            return response;
        });
        try
        {
            using var client = new HttpClient(handler);
            using var updates = new GitHubUpdateService(root, client);
            Assert.True((await updates.CheckAsync("1.0.0", manual: true)).IsUpdateAvailable);
            Assert.True((await updates.CheckAsync("1.0.0", manual: true)).IsUpdateAvailable);
            Assert.Equal(2, manifestCalls);
        }
        finally
        {
            if ( Directory.Exists(root) )
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Update_download_is_hash_verified_before_becoming_a_prepared_package()
    {
        var root = Path.Combine(Path.GetTempPath(), "juju-update-tests", Guid.NewGuid().ToString("N"));
        var payload = Encoding.UTF8.GetBytes("verified-package");
        var hash = Convert.ToHexString(SHA256.HashData(payload));
        var manifest = new ReleaseManifest
        {
            SchemaVersion = 1,
            Version = "1.2.3",
            Channel = "stable",
            Rid = "win-x64",
            PackageAsset = "juju_v1.2.3_win-x64.zip",
            PackageSizeBytes = payload.Length,
            Sha256 = hash,
            MinimumLauncherVersion = "1.0.0"
        };
        var update = new AvailableUpdate(manifest, "", "https://github.com/qlxsply/juju/releases/tag/juju_v1.2.3",
            "https://github.com/qlxsply/juju/releases/download/juju_v1.2.3/juju_v1.2.3_win-x64.zip",
            payload.Length, "juju_v1.2.3");
        try
        {
            using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload)
            }));
            using var updates = new GitHubUpdateService(root, client);
            var prepared = await updates.DownloadAsync(update);
            Assert.Equal(manifest, prepared.Manifest);
            Assert.Equal(payload, await File.ReadAllBytesAsync(prepared.PackagePath));
            Assert.True(File.Exists(prepared.ManifestPath));
        }
        finally
        {
            if ( Directory.Exists(root) )
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Update_download_rejects_redirects_to_untrusted_hosts()
    {
        var root = Path.Combine(Path.GetTempPath(), "juju-update-tests", Guid.NewGuid().ToString("N"));
        var manifest = new ReleaseManifest
        {
            SchemaVersion = 1,
            Version = "1.2.3",
            Channel = "stable",
            Rid = "win-x64",
            PackageAsset = "juju_v1.2.3_win-x64.zip",
            PackageSizeBytes = 10,
            Sha256 = new string('a', 64),
            MinimumLauncherVersion = "1.0.0"
        };
        var update = new AvailableUpdate(manifest, "", "https://github.com/qlxsply/juju/releases/tag/juju_v1.2.3",
            "https://github.com/qlxsply/juju/releases/download/juju_v1.2.3/juju_v1.2.3_win-x64.zip", 10,
            "juju_v1.2.3");
        try
        {
            using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("https://attacker.example/payload.zip") }
            }));
            using var updates = new GitHubUpdateService(root, client);
            await Assert.ThrowsAsync<InvalidDataException>(() => updates.DownloadAsync(update));
        }
        finally
        {
            if ( Directory.Exists(root) )
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Version_pruning_keeps_active_and_previous_versions()
    {
        var root = Path.Combine(Path.GetTempPath(), "juju-update-tests", Guid.NewGuid().ToString("N"));
        var versions = Path.Combine(root, "versions");
        try
        {
            foreach ( var version in new[] { "1.0.0", "1.1.0", "1.2.0", "staging" } )
            {
                var directory = Path.Combine(versions, version);
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "payload"), version);
            }

            var removed = VersionDirectoryManager.Prune(root, new VersionState("1.2.0", PreviousVersion: "1.1.0"));

            Assert.Equal(["1.0.0"], removed);
            Assert.False(Directory.Exists(Path.Combine(versions, "1.0.0")));
            Assert.True(File.Exists(Path.Combine(versions, "1.1.0", "payload")));
            Assert.True(File.Exists(Path.Combine(versions, "1.2.0", "payload")));
            Assert.True(File.Exists(Path.Combine(versions, "staging", "payload")));
        }
        finally
        {
            if ( Directory.Exists(root) )
                Directory.Delete(root, recursive: true);
        }
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
}
