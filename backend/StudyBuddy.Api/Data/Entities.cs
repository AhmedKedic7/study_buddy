using Pgvector;

namespace StudyBuddy.Api.Data;

public enum DocumentStatus
{
    Processing,
    Ready,
    Failed,
}

public class StudyDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string FileName { get; set; }
    public required string Title { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Processing;
    public string? Error { get; set; }
    public int PageCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<DocumentChunk> Chunks { get; set; } = [];
}

public class DocumentChunk
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public StudyDocument Document { get; set; } = null!;
    public int PageNumber { get; set; }
    public int ChunkIndex { get; set; }
    public required string Content { get; set; }
    public required Vector Embedding { get; set; }
}
