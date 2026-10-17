using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace StudyBuddy.Api.Services;

public class VoyageOptions
{
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "voyage-3.5";
    public string BaseUrl { get; set; } = "https://api.voyageai.com/v1";

    // Voyage without a payment method allows 10K tokens/minute, so batches stay well under that.
    public int MaxBatchTokens { get; set; } = 8000;
}

public interface IEmbeddingService
{
    Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
    Task<float[]> EmbedQueryAsync(string text, CancellationToken ct = default);
}

/// <summary>
/// Embeddings via Voyage AI (free tier: 200M tokens). Groq has no embeddings endpoint.
/// Documents and queries use different input_type values, which improves retrieval quality.
/// </summary>
public class VoyageEmbeddingService(HttpClient http, IOptions<VoyageOptions> options, ILogger<VoyageEmbeddingService> logger)
    : IEmbeddingService
{
    private const int MaxBatchSize = 64;
    private const int MaxAttempts = 6;
    private readonly VoyageOptions _options = options.Value;

    public async Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var results = new List<float[]>(texts.Count);
        foreach (var batch in Batches(texts))
        {
            results.AddRange(await EmbedAsync(batch, "document", ct));
        }
        return results;
    }

    // Groups texts so each request stays under MaxBatchTokens (and MaxBatchSize items).
    private IEnumerable<List<string>> Batches(IReadOnlyList<string> texts)
    {
        var batch = new List<string>();
        var batchTokens = 0;
        foreach (var text in texts)
        {
            var tokens = EstimateTokens(text);
            if (batch.Count > 0 && (batchTokens + tokens > _options.MaxBatchTokens || batch.Count == MaxBatchSize))
            {
                yield return batch;
                batch = [];
                batchTokens = 0;
            }
            batch.Add(text);
            batchTokens += tokens;
        }
        if (batch.Count > 0) yield return batch;
    }

    // Rough and deliberately pessimistic (English averages ~4 chars per token).
    private static int EstimateTokens(string text) => text.Length / 3 + 1;

    public async Task<float[]> EmbedQueryAsync(string text, CancellationToken ct = default) =>
        (await EmbedAsync([text], "query", ct))[0];

    private async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> input, string inputType, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl.TrimEnd('/')}/embeddings")
            {
                Content = JsonContent.Create(new { input, model = _options.Model, input_type = inputType }),
            };
            request.Headers.Authorization = new("Bearer", _options.ApiKey);

            using var response = await http.SendAsync(request, ct);

            // Rate limited: wait (as told by Retry-After, else back off) and try again.
            if ((int)response.StatusCode == 429 && attempt < MaxAttempts)
            {
                var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Min(10 * attempt, 60));
                logger.LogInformation("Embedding rate limit hit; retrying in {Seconds}s (attempt {Attempt})", delay.TotalSeconds, attempt);
                await Task.Delay(delay, ct);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException($"Embeddings failed ({(int)response.StatusCode}): {body}");
            }

            var payload = await response.Content.ReadFromJsonAsync<VoyageResponse>(ct)
                ?? throw new InvalidOperationException("Empty response from the embeddings API.");
            return payload.Data.OrderBy(d => d.Index).Select(d => d.Embedding).ToList();
        }
    }

    private record VoyageResponse([property: JsonPropertyName("data")] List<VoyageEmbedding> Data);

    private record VoyageEmbedding(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] float[] Embedding);
}
