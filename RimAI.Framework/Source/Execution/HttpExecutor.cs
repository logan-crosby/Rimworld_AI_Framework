// 引入必要的命名空间
using System;
using System.Net.Http;
using System.Threading; // [新增] 引入 CancellationToken
using System.Threading.Tasks;
using RimAI.Framework.Execution.Models;
using RimAI.Framework.Contracts; // [新增] 引入 Result<T>
using RimAI.Framework.Shared.Logging;

namespace RimAI.Framework.Execution
{
    /// <summary>
    /// 负责发送 HttpRequestMessage、接收 HttpResponseMessage，并应用重试策略。
    /// 【新增】现在它也负责响应取消信号。
    /// </summary>
    public class HttpExecutor
    {
        private readonly HttpClient _client;

        public HttpExecutor()
        {
            _client = HttpClientFactory.GetClient();
        }

        /// <summary>
        /// 异步执行一个HTTP请求，并根据提供的策略进行重试。
        /// </summary>
        /// <param name="request">已完全构建好的HTTP请求消息。</param>
        /// <param name="cancellationToken">用于中断操作的令牌。</param>
        /// <param name="isStreaming">是否为流式请求。流式请求将只读取响应头，非流式将读取完整响应体。</param>
        /// <param name="policy">本次请求要遵循的重试策略。如果为null，则使用默认策略。</param>
        /// <returns>一个封装了成功时的 HttpResponseMessage 或失败时的错误信息的 Result 对象。</returns>
        public async Task<Result<HttpResponseMessage>> ExecuteAsync(HttpRequestMessage request, CancellationToken cancellationToken, bool isStreaming = false, RetryPolicy policy = null)
        {
            policy ??= new RetryPolicy();

            // Pre-read content for cloning on retries. HttpRequestMessage.Content is a forward-only
            // stream that gets consumed by SendAsync, so we must save the bytes before the first send.
            byte[] contentBytes = null;
            string contentType = null;
            if (request.Content != null && policy.MaxRetries > 0)
            {
                contentBytes = await request.Content.ReadAsByteArrayAsync();
                contentType = request.Content.Headers?.ContentType?.ToString();
            }

            for (int i = 0; i <= policy.MaxRetries; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Clone the request for retry attempts — the original's content is consumed
                // after the first SendAsync and cannot be reused.
                var attemptRequest = (i == 0)
                    ? request
                    : CloneHttpRequestMessage(request, contentBytes, contentType);

                // Per-request timeout via linked CancellationTokenSource.
                // Creates a token that fires when EITHER the caller cancels OR the
                // per-request timeout elapses. We distinguish which one fired in the catch block.
                CancellationTokenSource timeoutCts = null;
                CancellationTokenSource linkedCts = null;
                try
                {
                    timeoutCts = new CancellationTokenSource();
                    linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken, timeoutCts.Token);

                    if (policy.PerRequestTimeout > TimeSpan.Zero)
                    {
                        timeoutCts.CancelAfter(policy.PerRequestTimeout);
                    }

                    var completionOption = isStreaming
                        ? HttpCompletionOption.ResponseHeadersRead
                        : HttpCompletionOption.ResponseContentRead;

                    var response = await _client.SendAsync(
                        attemptRequest, completionOption, linkedCts.Token);

                    return Result<HttpResponseMessage>.Success(response);
                }
                catch (OperationCanceledException)
                {
                    // DISTINGUISH: caller cancellation (fail immediately) vs. timeout (retryable).
                    if (cancellationToken.IsCancellationRequested)
                    {
                        RimAILogger.Log("HttpExecutor: Request was cancelled by the user.");
                        return Result<HttpResponseMessage>.Failure("Request was cancelled by the user.");
                    }

                    // Per-request timeout — treat as retryable failure.
                    RimAILogger.Log($"HttpExecutor: Request timed out after {policy.PerRequestTimeout.TotalSeconds:F0}s. (Attempt {i + 1}/{policy.MaxRetries + 1})");

                    if (i >= policy.MaxRetries)
                    {
                        return Result<HttpResponseMessage>.Failure(
                            "HttpExecutor: All retry attempts timed out.");
                    }
                }
                catch (Exception ex)
                {
                    var inner = ex.InnerException != null
                        ? $" | Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}"
                        : string.Empty;
                    RimAILogger.Error($"HttpExecutor: Request failed. Retrying... (Attempt {i + 1}/{policy.MaxRetries + 1}). Error: {ex.GetType().Name}: {ex.Message}{inner}");

                    if (i >= policy.MaxRetries)
                    {
                        return Result<HttpResponseMessage>.Failure(
                            "HttpExecutor: All retry attempts failed to get a response due to network or other exceptions.");
                    }
                }
                finally
                {
                    linkedCts?.Dispose();
                    timeoutCts?.Dispose();
                }

                // Retry delay between attempts.
                if (i < policy.MaxRetries)
                {
                    try
                    {
                        var delay = policy.InitialDelay;
                        if (policy.UseExponentialBackoff)
                        {
                            try
                            {
                                delay = TimeSpan.FromMilliseconds(
                                    policy.InitialDelay.TotalMilliseconds * Math.Pow(2, i));
                            }
                            catch { delay = policy.InitialDelay; }
                        }
                        await Task.Delay(delay, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        RimAILogger.Log("HttpExecutor: Retry delay was cancelled by the user.");
                        return Result<HttpResponseMessage>.Failure(
                            "Request was cancelled by the user during retry delay.");
                    }
                }
            }

            return Result<HttpResponseMessage>.Failure(
                "HttpExecutor: All retry attempts failed to get a response due to network or other exceptions.");
        }

        /// <summary>
        /// Creates a distinct, identical copy of an HttpRequestMessage for retry attempts.
        /// The original request's Content stream is consumed after the first SendAsync,
        /// so we reconstruct the body from pre-saved bytes and content-type.
        /// Headers, properties, version, method, and URI are copied from the (still-valid) original.
        /// </summary>
        private static HttpRequestMessage CloneHttpRequestMessage(
            HttpRequestMessage original, byte[] contentBytes, string contentType)
        {
            var clone = new HttpRequestMessage(original.Method, original.RequestUri);

            // Reconstruct content from saved bytes.
            if (contentBytes != null)
            {
                var newContent = new ByteArrayContent(contentBytes);
                if (!string.IsNullOrEmpty(contentType))
                {
                    newContent.Headers.ContentType =
                        System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
                }
                clone.Content = newContent;
            }

            // Copy request headers (these remain valid after the original was sent).
            foreach (var header in original.Headers)
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

            // Copy properties.
            foreach (var prop in original.Properties)
                clone.Properties.Add(prop);

            clone.Version = original.Version;

            return clone;
        }
    }
}