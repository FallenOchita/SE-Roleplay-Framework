using System;
using System.Linq;
using System.Threading.Tasks;
using SE.Roleplay.Framework.Core.Chat;
using Xunit;

namespace SE.Roleplay.Framework.Core.Tests;

public sealed class ChatWarningServiceTests
{
    [Fact]
    public void AddsAndReturnsWarningsNewestFirst()
    {
        var service = new ChatWarningService();
        var first = DateTimeOffset.Parse("2026-01-01T10:00:00Z");
        var second = first.AddMinutes(5);
        service.AddWarning(76561198000000001, 76561198000000002, "Please keep chat respectful.", first);
        service.AddWarning(76561198000000001, 76561198000000003, "Please avoid repeated messages.", second);

        var warnings = service.GetWarnings(76561198000000001);
        Assert.Equal(2, warnings.Count);
        Assert.Equal(second, warnings[0].CreatedAtUtc);
        Assert.Equal(76561198000000003, warnings[0].ModeratorSteamId);
    }

    [Fact]
    public void ClearsOnlySelectedPlayersWarnings()
    {
        var service = new ChatWarningService();
        service.AddWarning(76561198000000001, 76561198000000002, "First player's warning.");
        service.AddWarning(76561198000000003, 76561198000000002, "Second player's warning.");

        Assert.Equal(1, service.ClearWarnings(76561198000000001));
        Assert.Empty(service.GetWarnings(76561198000000001));
        Assert.Single(service.GetWarnings(76561198000000003));
    }

    [Theory]
    [InlineData("")]
    [InlineData("no")]
    [InlineData("bad\nreason")]
    public void RejectsInvalidReasons(string reason)
    {
        var service = new ChatWarningService();
        Assert.Throws<ArgumentException>(() => service.AddWarning(76561198000000001, 76561198000000002, reason));
    }

    [Fact]
    public void EnforcesPerPlayerLimit()
    {
        var service = new ChatWarningService();
        for (var index = 0; index < ChatWarningService.MaximumWarningsPerPlayer; index++)
            service.AddWarning(76561198000000001, 76561198000000002, "Warning number " + index + ".");

        Assert.Throws<InvalidOperationException>(() =>
            service.AddWarning(76561198000000001, 76561198000000002, "One too many warnings."));
    }

    [Fact]
    public async Task ConcurrentAddsAreSafe()
    {
        var service = new ChatWarningService();
        await Task.WhenAll(Enumerable.Range(0, 25).Select(index => Task.Run(() =>
            service.AddWarning(76561198000000001, 76561198000000002, "Concurrent warning " + index + "."))));

        Assert.Equal(25, service.GetWarnings(76561198000000001).Count);
    }

    [Fact]
    public void ExportAndReplacePreserveWarningRecords()
    {
        var source = new ChatWarningService();
        source.AddWarning(76561198000000001, 76561198000000002, "Persisted warning reason.");
        var target = new ChatWarningService();

        target.Replace(source.Export());

        Assert.Single(target.GetWarnings(76561198000000001));
        Assert.Equal("Persisted warning reason.", target.GetWarnings(76561198000000001)[0].Reason);
    }
}
