using Newtonsoft.Json.Linq;
using RimAI.Framework.Translation;
using Xunit;

namespace RimAI.Agent.Tests.Framework
{
    public class UsageParserTests
    {
        [Fact]
        public void OpenAiUsage_ParsesAllThreeFields()
        {
            var root = JObject.Parse(@"{""usage"":{""prompt_tokens"":100,""completion_tokens"":25,""total_tokens"":125}}");
            var usage = UsageParser.TryParse(root);
            Assert.NotNull(usage);
            Assert.Equal(100, usage.PromptTokens);
            Assert.Equal(25, usage.CompletionTokens);
            Assert.Equal(125, usage.TotalTokens);
        }

        [Fact]
        public void AnthropicUsage_MapsInputOutputTokens_AndComputesTotal()
        {
            var root = JObject.Parse(@"{""usage"":{""input_tokens"":40,""output_tokens"":10}}");
            var usage = UsageParser.TryParse(root);
            Assert.NotNull(usage);
            Assert.Equal(40, usage.PromptTokens);
            Assert.Equal(10, usage.CompletionTokens);
            Assert.Equal(50, usage.TotalTokens);
        }

        [Fact]
        public void GeminiUsageMetadata_Parses()
        {
            var root = JObject.Parse(@"{""usageMetadata"":{""promptTokenCount"":7,""candidatesTokenCount"":3,""totalTokenCount"":10}}");
            var usage = UsageParser.TryParse(root);
            Assert.NotNull(usage);
            Assert.Equal(7, usage.PromptTokens);
            Assert.Equal(3, usage.CompletionTokens);
            Assert.Equal(10, usage.TotalTokens);
        }

        [Fact]
        public void NoUsage_ReturnsNull()
        {
            Assert.Null(UsageParser.TryParse(JObject.Parse(@"{""choices"":[]}")));
            Assert.Null(UsageParser.TryParse(null));
        }

        [Fact]
        public void EmptyUsageObject_ReturnsNull()
        {
            Assert.Null(UsageParser.TryParse(JObject.Parse(@"{""usage"":{}}")));
        }

        [Fact]
        public void MalformedUsage_DoesNotThrow_ReturnsNull()
        {
            var root = JObject.Parse(@"{""usage"":{""prompt_tokens"":""not-a-number""}}");
            var ex = Record.Exception(() => UsageParser.TryParse(root));
            Assert.Null(ex);
        }
    }
}
