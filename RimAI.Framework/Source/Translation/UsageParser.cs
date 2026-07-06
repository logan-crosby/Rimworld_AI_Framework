using Newtonsoft.Json.Linq;
using RimAI.Framework.Contracts;

namespace RimAI.Framework.Translation
{
    /// <summary>
    /// 从提供商响应中容错提取 token 用量。
    /// Tolerant extraction of token usage across provider dialects:
    /// OpenAI-compatible ("usage": prompt_tokens/completion_tokens/total_tokens),
    /// Anthropic native ("usage": input_tokens/output_tokens),
    /// Gemini native ("usageMetadata": promptTokenCount/candidatesTokenCount/totalTokenCount).
    /// Returns null when the payload carries no usage — callers must treat null as "estimate yourself".
    /// </summary>
    internal static class UsageParser
    {
        public static UsageInfo TryParse(JObject root)
        {
            if (root == null) return null;
            try
            {
                if (root["usage"] is JObject u)
                {
                    var prompt = u.Value<int?>("prompt_tokens") ?? u.Value<int?>("input_tokens");
                    var completion = u.Value<int?>("completion_tokens") ?? u.Value<int?>("output_tokens");
                    var total = u.Value<int?>("total_tokens") ?? (prompt.HasValue && completion.HasValue ? prompt + completion : (int?)null);
                    if (prompt.HasValue || completion.HasValue || total.HasValue)
                        return new UsageInfo { PromptTokens = prompt, CompletionTokens = completion, TotalTokens = total };
                }

                if (root["usageMetadata"] is JObject g)
                {
                    var prompt = g.Value<int?>("promptTokenCount");
                    var completion = g.Value<int?>("candidatesTokenCount");
                    var total = g.Value<int?>("totalTokenCount") ?? (prompt.HasValue && completion.HasValue ? prompt + completion : (int?)null);
                    if (prompt.HasValue || completion.HasValue || total.HasValue)
                        return new UsageInfo { PromptTokens = prompt, CompletionTokens = completion, TotalTokens = total };
                }
            }
            catch
            {
                // Malformed usage payloads must never fail a translation.
            }
            return null;
        }
    }
}
