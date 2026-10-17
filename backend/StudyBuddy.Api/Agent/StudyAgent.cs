using System.ClientModel;
using Microsoft.Extensions.AI;

namespace StudyBuddy.Api.Agent;

/// <summary>
/// Any OpenAI-compatible chat API: Groq by default, but OpenAI, OpenRouter or a local
/// Ollama work by changing BaseUrl and Model.
/// </summary>
public class LlmOptions
{
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1";
    public string Model { get; set; } = "openai/gpt-oss-120b";
}

public record ChatTurn(string Role, string Content);

public record ChatRequest(List<ChatTurn> Messages, List<Guid>? DocumentIds);

/// <summary>
/// The study buddy agent: a tool-use loop where the model decides when to search the
/// student's notes or build a quiz, and every step is streamed to the UI.
/// </summary>
public class StudyAgent(IChatClient chat, StudyTools tools, ILogger<StudyAgent> logger)
{
    private const int MaxIterations = 6;

    private const string SystemPrompt = """
        You are Study Buddy, a friendly and rigorous study assistant for a pharmacy student.
        The student uploads their lecture notes and textbooks; your job is to help them understand
        and remember that material.

        How to work:
        - For any question about course content, call search_notes first. Ground your answer in
          what the notes say and cite sources inline as [n], using the reference numbers that
          search_notes returns. Use only that square-bracket citation format.
          Search again with different wording if the first results are weak.
        - If the notes don't cover something, say so plainly ("This isn't in your notes"). You may
          then add general pharmacology knowledge, clearly labelled as coming from outside the notes.
          Drug facts (doses, interactions, contraindications) matter: never invent them.
        - When the student asks to be quizzed or tested, call generate_quiz. The quiz is shown to
          them as interactive cards, so do not repeat the questions or reveal the answers; just
          introduce it in one or two sentences.
        - Teach, don't just answer: use mnemonics, comparisons and tables where they help memory
          (e.g. comparing drugs within a class).
        - Keep answers focused and well structured with short headings or bullet points. Use Markdown.
        - You support studying only; you don't give medical advice about real patients.
        """;

    public async Task RunAsync(ChatRequest request, Func<AgentEvent, Task> emit, CancellationToken ct)
    {
        var context = new ToolContext(request.DocumentIds, emit);

        List<ChatMessage> messages = [new(ChatRole.System, SystemPrompt)];
        messages.AddRange(request.Messages.Select(m =>
            new ChatMessage(m.Role == "assistant" ? ChatRole.Assistant : ChatRole.User, m.Content)));

        var options = new ChatOptions
        {
            Tools = StudyTools.Definitions,
            ToolMode = ChatToolMode.Auto,
            MaxOutputTokens = 4096,
        };

        try
        {
            for (var iteration = 0; iteration < MaxIterations; iteration++)
            {
                var response = await chat.GetResponseAsync(messages, options, ct);

                // keeping  the assistant turn (including its tool calls) in the history so the model sees what it asked for next to the results.
                messages.AddMessages(response);

                var toolCalls = response.Messages
                    .SelectMany(m => m.Contents)
                    .OfType<FunctionCallContent>()
                    .ToList();

                if (toolCalls.Count == 0)
                {
                    var answer = response.Text.Trim();
                    if (response.FinishReason == ChatFinishReason.Length)
                    {
                        answer += "\n\n_(Answer was cut off; ask me to continue.)_";
                    }
                    await emit(new AnswerEvent(answer));
                    return;
                }

                // Run tools sequentially (they share a DbContext) each result is a "tool" message linked to its call by CallId.
                foreach (var call in toolCalls)
                {
                    var result = await tools.ExecuteAsync(call.Name, call.Arguments, context, ct);
                    messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, result.Content)]));
                }
            }
        }
        catch (ClientResultException ex) when (ex.Status == 429)
        {
            logger.LogWarning("LLM rate limit hit: {Message}", ex.Message);
            await emit(new ErrorEvent("The free API rate limit was reached. Wait about a minute and try again."));
            return;
        }

        logger.LogWarning("Agent hit the iteration limit");
        await emit(new ErrorEvent("I took too many steps on that one. Try asking a more specific question."));
    }
}
