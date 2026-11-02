using SE.Roleplay.Framework.Core.Admission;
using Xunit;

namespace SE.Roleplay.Framework.Core.Tests.Admission
{
    public sealed class AdmissionDecisionParserTests
    {
        [Fact]
        public void ParsesAllowDecision()
        {
            Assert.True(AdmissionDecisionParser.TryParse("{\"shouldAdmit\":true}", out var shouldAdmit));
            Assert.True(shouldAdmit);
        }

        [Fact]
        public void ParsesDenyDecision()
        {
            Assert.True(AdmissionDecisionParser.TryParse("{\"shouldAdmit\":false}", out var shouldAdmit));
            Assert.False(shouldAdmit);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not-json")]
        [InlineData("{}")]
        [InlineData("{\"shouldAdmit\":null}")]
        [InlineData("{\"shouldAdmit\":\"true\"}")]
        [InlineData("{\"shouldAdmit\":1}")]
        public void RejectsMissingOrMalformedDecision(string? json)
        {
            Assert.False(AdmissionDecisionParser.TryParse(json, out var shouldAdmit));
            Assert.False(shouldAdmit);
        }
    }
}
