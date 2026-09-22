using FMS.API;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FMS.Domain.Tests.Api;

/// <summary>
/// The stamp the deploy job bakes into the API image and the post-deploy smoke check compares
/// against the commit it released.
/// </summary>
/// <remarks>
/// The reader is a few lines, but it is the half of the provenance check that can fail
/// silently: if an unstamped image reported an empty string, the smoke check would compare its
/// commit against nothing and either pass vacuously or fail for an unhelpful reason. "unknown"
/// keeps that case visible.
/// </remarks>
public class BuildStampTests
{
    private static IConfiguration Configuration(string? value) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [BuildStamp.ConfigurationKey] = value
            })
            .Build();

    [Fact]
    public void Sha_ReportsTheCommitTheImageWasBuiltFrom()
    {
        Assert.Equal("84cfe81a1b2c3d4e5f60718293a4b5c6d7e8f900", BuildStamp.Sha(Configuration("84cfe81a1b2c3d4e5f60718293a4b5c6d7e8f900")));
    }

    [Fact]
    public void Sha_TrimsWhatTheBuildArgPassedIn()
    {
        Assert.Equal("abc123", BuildStamp.Sha(Configuration("  abc123  ")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sha_IsVisiblyUnstamped_WhenTheBuildNeverReceivedTheCommit(string? value)
    {
        Assert.Equal(BuildStamp.Unstamped, BuildStamp.Sha(Configuration(value)));
        Assert.NotEqual(string.Empty, BuildStamp.Sha(Configuration(value)));
    }
}
