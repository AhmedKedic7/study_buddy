using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using StudyBuddy.Api.Services;

namespace StudyBuddy.Api.Agent;

public record ToolContext(IReadOnlyCollection<Guid>? DocumentIds, Func<AgentEvent, Task> Emit)
{
    // Reference numbers stay stable across searches within one answer, so [n] citations don't collide.
    public List<SourceRef> Sources { get; } = [];
}

public record ToolResult(string Content);

public class StudyTools(RetrievalService retrieval, IChatClient chat, ILogger<StudyTools> logger)
{
    // Passages sent per call. Kept small because Groq's free tier allows ~8k tokens per minute
    // (one 1500-char chunk is roughly 375 tokens).
    private const int SearchTopK = 5;
    private const int QuizTopK = 6;

    // Declarations only: the model sees name + description + JSON schema; our loop runs the code.
    public static readonly List<AITool> Definitions =
    [
        AIFunctionFactory.CreateDeclaration(
            "search_notes",
            "Semantic search over the student's uploaded lecture notes and textbooks. "
                + "Returns the most relevant passages, each with a reference number [n], document title and page. "
                + "Use specific queries (drug names, mechanisms, drug classes); call it again with different wording if results are weak.",
            JsonElementFrom("""
            {
              "type": "object",
              "properties": {
                "query": { "type": "string", "description": "What to search for" }
              },
              "required": ["query"]
            }
            """)),
        AIFunctionFactory.CreateDeclaration(
            "generate_quiz",
            "Creates an interactive multiple-choice quiz from the student's notes on a topic and shows it to them. "
                + "Use when the student asks to be quizzed, tested or to practice.",
            JsonElementFrom("""
            {
              "type": "object",
              "properties": {
                "topic": { "type": "string", "description": "Topic to quiz on, e.g. 'beta blockers'" },
                "num_questions": { "type": "integer", "minimum": 1, "maximum": 10, "description": "Number of questions (default 5)" },
                "difficulty": { "type": "string", "enum": ["easy", "medium", "hard"] }
              },
              "required": ["topic"]
            }
            """)),
    ];

    public async Task<ToolResult> ExecuteAsync(
        string name, IDictionary<string, object?>? input, ToolContext context, CancellationToken ct)
    {
        input ??= new Dictionary<string, object?>();
        try
        {
            return name switch
            {
                "search_notes" => await SearchNotesAsync(GetString(input, "query"), context, ct),
                "generate_quiz" => await GenerateQuizAsync(
                    GetString(input, "topic"),
                    int.TryParse(Arg(input, "num_questions")?.ToString(), out var count) ? Math.Clamp(count, 1, 10) : 5,
                    Arg(input, "difficulty")?.ToString() is { Length: > 0 } d ? d : "medium",
                    context, ct),
                _ => new ToolResult($"Error: unknown tool '{name}'."),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Tool {Tool} failed", name);
            // Returned to the model (not thrown) so it can recover, e.g. by retrying with other wording.
            return new ToolResult($"Error: the tool failed ({ex.Message}).");
        }
    }

    private async Task<ToolResult> SearchNotesAsync(string query, ToolContext context, CancellationToken ct)
    {
        await context.Emit(new StepEvent("search_notes", $"Searching your notes for \"{query}\""));

        var chunks = await retrieval.SearchAsync(query, context.DocumentIds, topK: SearchTopK, ct: ct);
        if (chunks.Count == 0)
        {
            return new ToolResult("No notes have been uploaded yet, or nothing matched.");
        }

        var output = new StringBuilder();
        var newSources = new List<SourceRef>();
        foreach (var chunk in chunks)
        {
            var existing = context.Sources.FirstOrDefault(s => s.DocumentId == chunk.DocumentId && s.Page == chunk.PageNumber && s.Snippet == Snippet(chunk.Content));
            var source = existing ?? new SourceRef(context.Sources.Count + 1, chunk.DocumentId, chunk.DocumentTitle, chunk.PageNumber, Snippet(chunk.Content));
            if (existing is null)
            {
                context.Sources.Add(source);
                newSources.Add(source);
            }
            output.AppendLine($"[{source.Ref}] {chunk.DocumentTitle}, page {chunk.PageNumber} (relevance {chunk.Score:F2})");
            output.AppendLine(chunk.Content);
            output.AppendLine();
        }

        if (newSources.Count > 0)
        {
            await context.Emit(new SourcesEvent(newSources));
        }
        return new ToolResult(output.ToString());
    }

    private async Task<ToolResult> GenerateQuizAsync(string topic, int count, string difficulty, ToolContext context, CancellationToken ct)
    {
        await context.Emit(new StepEvent("generate_quiz", $"Writing a {difficulty} quiz on \"{topic}\""));

        var chunks = await retrieval.SearchAsync(topic, context.DocumentIds, topK: QuizTopK, ct: ct);
        if (chunks.Count == 0)
        {
            return new ToolResult("Error: couldn't find any notes on this topic, so no quiz was created.");
        }

        var material = string.Join("\n\n", chunks.Select(c => $"<passage source=\"{c.DocumentTitle}, p.{c.PageNumber}\">\n{c.Content}\n</passage>"));

        var prompt = $"""
            Write a {difficulty} multiple-choice quiz with exactly {count} questions on "{topic}"
            for a pharmacy student, using ONLY the facts in the passages below.
            Each question has 4 options with exactly one correct answer; make distractors plausible.
            correctIndex is the 0-based index of the correct option.
            The explanation says why the answer is right (and why a tempting distractor is wrong).
            "source" is the passage's source attribute.

            {material}
            """;

        // Structured output: the model must return JSON matching QuizSchema.
        var response = await chat.GetResponseAsync(prompt, new ChatOptions
        {
            ResponseFormat = ChatResponseFormat.ForJsonSchema(QuizSchema, "quiz", "A multiple-choice quiz"),
            // Strict mode = constrained decoding: the output is guaranteed to match the schema.
            AdditionalProperties = new() { ["strict"] = true },
            MaxOutputTokens = 4096,
        }, ct);

        var quiz = JsonSerializer.Deserialize<QuizPayload>(response.Text, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("Quiz generation returned no content.");

        await context.Emit(new QuizEvent(new Quiz(topic, quiz.Questions)));

        return new ToolResult(
            $"A {quiz.Questions.Count}-question quiz on \"{topic}\" is now displayed to the student as interactive cards. "
            + "Don't repeat the questions or reveal the answers.");
    }

    private static string GetString(IDictionary<string, object?> input, string key) =>
        Arg(input, key)?.ToString() is { Length: > 0 } s
            ? s
            : throw new ArgumentException($"Missing required input '{key}'.");

    private static object? Arg(IDictionary<string, object?> input, string key) =>
        input.TryGetValue(key, out var value) ? value : null;

    private static string Snippet(string content) => content.Length <= 240 ? content : content[..240] + "…";

    private static JsonElement JsonElementFrom(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private record QuizPayload(List<QuizQuestion> Questions);

    // Strict-mode compatible: every property required, additionalProperties false on every object.
    private static readonly JsonElement QuizSchema = JsonElementFrom("""
        {
          "type": "object",
          "properties": {
            "questions": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "question": { "type": "string" },
                  "options": { "type": "array", "items": { "type": "string" } },
                  "correctIndex": { "type": "integer" },
                  "explanation": { "type": "string" },
                  "source": { "type": "string" }
                },
                "required": ["question", "options", "correctIndex", "explanation", "source"],
                "additionalProperties": false
              }
            }
          },
          "required": ["questions"],
          "additionalProperties": false
        }
        """);
}
