using Haven.Gateway.Services;
using Xunit;

namespace Haven.Gateway.Tests;

public sealed class PatchRequestPolicyTests
{
    [Theory]
    [InlineData("/patches/PC/0.1.0/abc.rawfile")]
    [InlineData("/patches/PC/0.1.0/abc.bundle")]
    [InlineData("/PATCHES/PC/0.1.0/ABC.BUNDLE")]
    public void FaultModeRejectsOnlyDownloadPayloads(string path)
    {
        Assert.True(PatchRequestPolicy.ShouldSimulateDownloadFailure(path, true));
    }

    [Theory]
    [InlineData("/patches/PC/0.1.0/DefaultPackage.version")]
    [InlineData("/patches/PC/0.1.0/DefaultPackage_0.1.0-hotfix.1.bytes")]
    [InlineData("/health")]
    [InlineData("")]
    public void FaultModeLeavesDiscoveryAndOtherRoutesAvailable(string path)
    {
        Assert.False(PatchRequestPolicy.ShouldSimulateDownloadFailure(path, true));
    }

    [Fact]
    public void DisabledFaultModeNeverRejectsDownloads()
    {
        Assert.False(PatchRequestPolicy.ShouldSimulateDownloadFailure("/patches/PC/0.1.0/abc.rawfile", false));
    }
}
