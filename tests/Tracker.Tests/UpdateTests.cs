using System.Net;
using System.Security.Cryptography;
using System.Text;
using Tracker.Core;
using Xunit;

namespace Tracker.Tests;

public sealed class UpdateTests
{
    /// <summary>Vrací připravené odpovědi místo skutečného GitHubu.</summary>
    private sealed class StubHandler(Func<Uri, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(respond(request.RequestUri!));
    }

    private static HttpResponseMessage Text(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8) };

    private static HttpResponseMessage Bytes(byte[] body) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    private static string ReleaseJson(string tag) => $$"""
        {
          "tag_name": "{{tag}}",
          "html_url": "https://github.com/owner/repo/releases/tag/{{tag}}",
          "assets": [
            { "name": "BattlegroundsTracker.exe",
              "browser_download_url": "https://example.test/BattlegroundsTracker.exe" },
            { "name": "BattlegroundsTracker.exe.sha256",
              "browser_download_url": "https://example.test/BattlegroundsTracker.exe.sha256" }
          ]
        }
        """;

    [Fact]
    public async Task IgnoresAReleaseThatIsNotNewerThanTheRunningVersion()
    {
        var service = new UpdateService("owner/repo", new StubHandler(_ => Text(ReleaseJson("v0.0.1"))));

        Assert.Null(await service.FindNewerReleaseAsync());
    }

    [Fact]
    public async Task DownloadsANewerReleaseOnlyWhenTheChecksumMatches()
    {
        var payload = Encoding.UTF8.GetBytes(new string('x', 2_000_000));
        var checksum = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var directory = Path.Combine(Path.GetTempPath(), $"tracker-update-{Guid.NewGuid():N}");
        var executable = Path.Combine(directory, "BattlegroundsTracker.exe");

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(executable, "stará verze");

            var service = new UpdateService("owner/repo", new StubHandler(uri => uri.AbsoluteUri switch
            {
                var url when url.EndsWith("releases/latest", StringComparison.Ordinal) => Text(ReleaseJson("v9.9.9")),
                var url when url.EndsWith(".sha256", StringComparison.Ordinal) =>
                    Text($"{checksum}  BattlegroundsTracker.exe"),
                _ => Bytes(payload)
            }));

            var update = await service.FindNewerReleaseAsync();
            Assert.NotNull(update);
            Assert.Equal("9.9.9", update.Version);
            Assert.Equal(checksum, update.Sha256, ignoreCase: true);

            Assert.True(await service.DownloadAsync(update, executable));
            Assert.True(UpdateInstaller.HasStagedUpdate(executable));

            // Poškozený soubor se zahodí a nic se nenainstaluje.
            var corrupted = new UpdateService("owner/repo", new StubHandler(uri => uri.AbsoluteUri switch
            {
                var url when url.EndsWith(".sha256", StringComparison.Ordinal) =>
                    Text($"{checksum}  BattlegroundsTracker.exe"),
                _ => Bytes(Encoding.UTF8.GetBytes(new string('y', 2_000_000)))
            }));
            File.Delete(UpdateInstaller.StagedPath(executable));
            Assert.False(await corrupted.DownloadAsync(update, executable));
            Assert.False(UpdateInstaller.HasStagedUpdate(executable));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void InstallsTheStagedVersionAndKeepsThePreviousOneUntilTheNextStart()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"tracker-install-{Guid.NewGuid():N}");
        var executable = Path.Combine(directory, "BattlegroundsTracker.exe");

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(executable, "stará verze");
            File.WriteAllText(UpdateInstaller.StagedPath(executable), "nová verze");

            Assert.True(UpdateInstaller.Apply(executable));
            Assert.Equal("nová verze", File.ReadAllText(executable));
            Assert.False(UpdateInstaller.HasStagedUpdate(executable));
            Assert.True(File.Exists(executable + ".old"));

            // Bez připravené verze se jen uklidí zbytek po minulé aktualizaci.
            Assert.False(UpdateInstaller.Apply(executable));
            Assert.False(File.Exists(executable + ".old"));
            Assert.Equal("nová verze", File.ReadAllText(executable));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
