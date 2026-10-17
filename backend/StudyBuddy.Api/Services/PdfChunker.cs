using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace StudyBuddy.Api.Services;

public record TextChunk(int PageNumber, int ChunkIndex, string Content);

/// <summary>
/// Extracts text page by page and splits it into overlapping chunks.
/// Chunks never cross page boundaries so every citation points to an exact page.
/// </summary>
public static partial class PdfChunker
{
    private const int MaxChunkChars = 1500;
    private const int OverlapChars = 200;
    private const int MinPageChars = 20;

    public static (int PageCount, List<TextChunk> Chunks) Chunk(string pdfPath)
    {
        using var pdf = PdfDocument.Open(pdfPath);
        var chunks = new List<TextChunk>();

        foreach (var page in pdf.GetPages())
        {
            var text = Whitespace().Replace(ContentOrderTextExtractor.GetText(page), " ").Trim();
            if (text.Length < MinPageChars)
            {
                continue; // blank or scanned page (no text layer)
            }

            var index = 0;
            foreach (var piece in Split(text))
            {
                chunks.Add(new TextChunk(page.Number, index++, piece));
            }
        }

        return (pdf.NumberOfPages, chunks);
    }

    private static IEnumerable<string> Split(string text)
    {
        if (text.Length <= MaxChunkChars)
        {
            yield return text;
            yield break;
        }

        var start = 0;
        while (start < text.Length)
        {
            var end = Math.Min(start + MaxChunkChars, text.Length);
            if (end < text.Length)
            {
                // Prefer to break at a sentence end, then at a space.
                var sentenceEnd = text.LastIndexOf(". ", end, end - start, StringComparison.Ordinal);
                var space = text.LastIndexOf(' ', end, end - start);
                if (sentenceEnd > start + MaxChunkChars / 2) end = sentenceEnd + 1;
                else if (space > start) end = space;
            }

            yield return text[start..end].Trim();
            if (end >= text.Length) yield break;
            start = Math.Max(end - OverlapChars, start + 1);
        }
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
