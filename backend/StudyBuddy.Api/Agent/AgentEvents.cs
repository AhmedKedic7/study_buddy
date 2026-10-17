using System.Text.Json.Serialization;

namespace StudyBuddy.Api.Agent;

/// <summary>Events streamed to the frontend over SSE while the agent works.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(StepEvent), "step")]
[JsonDerivedType(typeof(SourcesEvent), "sources")]
[JsonDerivedType(typeof(QuizEvent), "quiz")]
[JsonDerivedType(typeof(AnswerEvent), "answer")]
[JsonDerivedType(typeof(ErrorEvent), "error")]
public abstract record AgentEvent;

public record StepEvent(string Tool, string Label) : AgentEvent;

public record SourcesEvent(IReadOnlyList<SourceRef> Sources) : AgentEvent;

public record SourceRef(int Ref, Guid DocumentId, string DocumentTitle, int Page, string Snippet);

public record QuizEvent(Quiz Quiz) : AgentEvent;

public record AnswerEvent(string Text) : AgentEvent;

public record ErrorEvent(string Message) : AgentEvent;

public record Quiz(string Topic, IReadOnlyList<QuizQuestion> Questions);

public record QuizQuestion(string Question, IReadOnlyList<string> Options, int CorrectIndex, string Explanation, string Source);
