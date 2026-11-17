using Arcadia.Services;
using Xunit;

namespace Arcadia.Tests;

public class UpdateTests
{
    [Theory]
    [InlineData("v1.2.0", 1, 2, 0)]
    [InlineData("1.3", 1, 3, 0)]
    [InlineData("v2.0.1-beta", 2, 0, 1)]
    [InlineData("V10", 10, 0, 0)]
    public void ReadsReleaseTags(string tag, int major, int minor, int build)
    {
        Assert.True(UpdateService.TryParseVersion(tag, out var v));
        Assert.Equal(new Version(major, minor, build), v);
    }

    [Fact]
    public void RejectsTagsThatAreNotVersions() => Assert.False(UpdateService.TryParseVersion("latest", out _));

    [Fact]
    public void ReadsGitHubRelease()
    {
        const string json = """
            {
              "tag_name": "v1.4.0", "name": "Arcadia 1.4", "draft": false,
              "html_url": "https://github.com/someone/arcadia-releases/releases/tag/v1.4.0",
              "body": "- Nouveaux thèmes\n- Corrections",
              "assets": [
                { "name": "notes.txt", "size": 10, "browser_download_url": "https://example.com/notes.txt" },
                { "name": "Arcadia.exe", "size": 70246263, "browser_download_url": "https://example.com/Arcadia.exe" }
              ]
            }
            """;
        var r = UpdateService.ParseRelease(json)!;
        Assert.Equal(new Version(1, 4, 0), r.Version);
        Assert.Equal("https://example.com/Arcadia.exe", r.DownloadUrl);
        Assert.Equal(70246263, r.Size);
        Assert.Contains("Nouveaux thèmes", r.Notes);
        Assert.True(r.Version > new Version(1, 0, 0));
    }

    [Fact]
    public void IgnoresDraftReleases() =>
        Assert.Null(UpdateService.ParseRelease("""{ "tag_name": "v9.0.0", "draft": true, "assets": [] }"""));
}
