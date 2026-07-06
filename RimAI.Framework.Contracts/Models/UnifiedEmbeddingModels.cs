using System.Collections.Generic;

namespace RimAI.Framework.Contracts
{
    /// <summary>
    /// Embedding 请求。
    /// </summary>
    public class UnifiedEmbeddingRequest
    {
        /// <summary>
        /// 待向量化文本列表。
        /// </summary>
        public List<string> Inputs { get; set; }
    }

    /// <summary>
    /// 单条嵌入结果。
    /// </summary>
    public class EmbeddingResult
    {
        public int Index { get; set; }
        public List<float> Embedding { get; set; }
    }

    /// <summary>
    /// Embedding 响应。
    /// </summary>
    public class UnifiedEmbeddingResponse
    {
        public List<EmbeddingResult> Data { get; set; }

        /// <summary>
        /// Token 用量。提供商未返回时为 null。
        /// </summary>
        public UsageInfo Usage { get; set; }
    }
}