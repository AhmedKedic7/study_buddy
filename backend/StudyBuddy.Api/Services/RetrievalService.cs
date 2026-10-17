using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using StudyBuddy.Api.Data;

namespace StudyBuddy.Api.Services;

public record RetrievedChunk(Guid DocumentId, string DocumentTitle, int PageNumber, string Content, double Score);

public class RetrievalService(AppDbContext db, IEmbeddingService embeddings)
{
    public async Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
        string query, IReadOnlyCollection<Guid>? documentIds, int topK = 6, CancellationToken ct = default)
    {
        var queryVector = new Vector(await embeddings.EmbedQueryAsync(query, ct));

        var chunks = db.Chunks.AsNoTracking();
        if (documentIds is { Count: > 0 })
        {
            chunks = chunks.Where(c => documentIds.Contains(c.DocumentId));
        }

        return await chunks
            .OrderBy(c => c.Embedding.CosineDistance(queryVector))
            .Take(topK)
            .Select(c => new RetrievedChunk(
                c.DocumentId,
                c.Document.Title,
                c.PageNumber,
                c.Content,
                1 - c.Embedding.CosineDistance(queryVector)))
            .ToListAsync(ct);
    }
}
