using RimAI.Agent.Perception;
using Xunit;

namespace RimAI.Agent.Tests.Perception
{
    public class EntityIdFormatterTests
    {
        [Theory]
        [InlineData("bill_", "Bill_42", "bill_42")]
        [InlineData("faction_", "Faction_17", "faction_17")]
        [InlineData("bill_", "42", "bill_42")]
        public void FromLoadId_UsesCanonicalPrefixAndStableSuffix(
            string prefix,
            string uniqueLoadId,
            string expected)
        {
            Assert.Equal(expected, EntityIdFormatter.FromLoadId(prefix, uniqueLoadId));
        }

        [Fact]
        public void FromLoadId_NullLoadId_ReturnsNull()
        {
            Assert.Null(EntityIdFormatter.FromLoadId("bill_", null));
        }
    }
}
