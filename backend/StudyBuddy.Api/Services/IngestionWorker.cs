using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using StudyBuddy.Api.Data;

namespace StudyBuddy.Api.Services;

/// <summary>Queue of uploaded documents waiting to be parsed and embedded.</summary>
public class IngestionQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();

    public ValueTask EnqueueAsync(Guid documentId) => _channel.Writer.WriteAsync(documentId);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}

/// <summary>Background worker: PDF -> chunks -> embeddings -> pgvector.</summary>
public class IngestionWorker(
    IngestionQueue queue,
    IServiceScopeFactory scopeFactory,
    FileStorage storage,
    ILogger<IngestionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var documentId in queue.ReadAllAsync(stoppingToken))
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var embeddings = scope.ServiceProvider.GetRequiredService<IEmbeddingService>();

            var document = await db.Documents.FindAsync([documentId], stoppingToken);
            if (document is null) continue;

            try
            {
                var (pageCount, chunks) = PdfChunker.Chunk(storage.PathFor(documentId));
                if (chunks.Count == 0)
                {
                    throw new InvalidOperationException("No extractable text found. Is this a scanned PDF?");
                }

                var vectors = await embeddings.EmbedDocumentsAsync(chunks.Select(c => c.Content).ToList(), stoppingToken);

                db.Chunks.AddRange(chunks.Select((c, i) => new DocumentChunk
                {
                    DocumentId = documentId,
                    PageNumber = c.PageNumber,
                    ChunkIndex = c.ChunkIndex,
                    Content = c.Content,
                    Embedding = new Vector(vectors[i]),
                }));
                document.PageCount = pageCount;
                document.Status = DocumentStatus.Ready;
                logger.LogInformation("Ingested {Title}: {Pages} pages, {Chunks} chunks", document.Title, pageCount, chunks.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to ingest document {Id}", documentId);
                db.ChangeTracker.Clear();
                document = await db.Documents.FindAsync([documentId], stoppingToken);
                if (document is null) continue;
                document.Status = DocumentStatus.Failed;
                document.Error = ex.Message;
            }

            await db.SaveChangesAsync(stoppingToken);
        }
    }
}

/// <summary>Stores uploaded files on local disk (swap for Blob/R2 storage in production).</summary>
public class FileStorage(IConfiguration config)
{
    private readonly string _root = Directory.CreateDirectory(config["Storage:UploadPath"] ?? "data/uploads").FullName;

    public string PathFor(Guid documentId) => Path.Combine(_root, $"{documentId}.pdf");

    public void Delete(Guid documentId) => File.Delete(PathFor(documentId));
}
