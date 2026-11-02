using System;
using System.Threading.Tasks;\nusing System.Linq;
using SE.Roleplay.Framework.Core.Chat;
using Xunit;

namespace SE.Roleplay.Framework.Core.Tests;

public sealed class ChatModerationServiceTests
{
    [Fact]
    public void BlocksCaseInsensitiveSubstringMatches()
    {
        var service = new ChatModerationService();
        Assert.True(service.AddBlockedPhrase("spoiler phrase"));

        Assert.True(service.IsBlocked("Please avoid SPOILER PHRASE in chat.", out var matched));
        Assert.Equal("spoiler phrase", matched);
        Assert.False(service.IsBlocked("A normal roleplay message.", out _));
    }

    [Fact]
    public void DisabledRulesDoNotBlockAndCanBeReenabled()
    {
        var service = new ChatModerationService();
        service.AddBlockedPhrase("blocked");
        service.IsEnabled = false;

        Assert.False(service.IsBlocked("this is blocked", out _));

        service.IsEnabled = true;
        Assert.True(service.IsBlocked("this is blocked", out _));
    }

    [Fact]
    public void RulesCanBeRemovedAndReplaced()
    {
        var service = new ChatModerationService();
        service.AddBlockedPhrase("first");
        Assert.True(service.RemoveBlockedPhrase("FIRST"));
        Assert.False(service.RemoveBlockedPhrase("missing"));

        service.ReplaceRules(new[] { "second", "third", "SECOND" }, enabled: false);
        Assert.Equal(2, service.GetBlockedPhrases().Count);
        Assert.False(service.IsBlocked("second", out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("x")]
    [InlineData("bad\nphrase")]
    public void InvalidPhrasesAreRejected(string phrase)
    {
        var service = new ChatModerationService();
        Assert.Throws<ArgumentException>(() => service.AddBlockedPhrase(phrase));
    }

    [Fact]
    public void RuleCountIsCappedAtFiveHundred()
    {
        var service = new ChatModerationService();
        service.ReplaceRules(Enumerable.Range(0, 500).Select(index => "phrase-" + index));

        Assert.Throws<InvalidOperationException>(() => service.AddBlockedPhrase("one-more"));
        Assert.Equal(500, service.GetBlockedPhrases().Count);
    }

    [Fact]
    public async Task ConcurrentReadsAndWritesRemainSafe()
    {
        var service = new ChatModerationService();
        await Task.WhenAll(
            Task.Run(() => service.AddBlockedPhrase("alpha")),
            Task.Run(() => service.AddBlockedPhrase("beta")),
            Task.Run(() => service.IsBlocked("alpha beta", out _)),
            Task.Run(() => service.GetBlockedPhrases()));

        Assert.Equal(2, service.GetBlockedPhrases().Count);
    }
}
