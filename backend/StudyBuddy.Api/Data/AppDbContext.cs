using Microsoft.EntityFrameworkCore;

namespace StudyBuddy.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // Must match the output dimension of the embedding model (voyage-3.5 = 1024).
    public const int EmbeddingDimensions = 1024;

    public DbSet<StudyDocument> Documents => Set<StudyDocument>();
    public DbSet<DocumentChunk> Chunks => Set<DocumentChunk>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<StudyDocument>(e =>
        {
            e.Property(d => d.Status).HasConversion<string>();
            e.HasMany(d => d.Chunks).WithOne(c => c.Document).HasForeignKey(c => c.DocumentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DocumentChunk>(e =>
        {
            e.Property(c => c.Embedding).HasColumnType($"vector({EmbeddingDimensions})");
            e.HasIndex(c => c.Embedding).HasMethod("hnsw").HasOperators("vector_cosine_ops");
            e.HasIndex(c => c.DocumentId);
        });
    }
}
