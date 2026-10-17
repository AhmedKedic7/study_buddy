using System.ClientModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenAI;
using StudyBuddy.Api;
using StudyBuddy.Api.Agent;
using StudyBuddy.Api.Data;
using StudyBuddy.Api.Services;

// Must run before CreateBuilder, which reads environment variables into configuration.
DotEnv.Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection("Llm"));
builder.Services.Configure<VoyageOptions>(builder.Configuration.GetSection("Voyage"));

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Postgres"), npgsql => npgsql.UseVector()));

// Groq exposes an OpenAI-compatible API, so the OpenAI SDK works by pointing it at Groq's URL.
// IChatClient (Microsoft.Extensions.AI) keeps the rest of the code provider-agnostic.
builder.Services.AddSingleton<IChatClient>(sp =>
{
    var llm = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
    if (string.IsNullOrWhiteSpace(llm.ApiKey))
        throw new InvalidOperationException("Llm:ApiKey is not configured.");
    var client = new OpenAIClient(new ApiKeyCredential(llm.ApiKey), new OpenAIClientOptions { Endpoint = new Uri(llm.BaseUrl) });
    return client.GetChatClient(llm.Model).AsIChatClient();
});
builder.Services.AddHttpClient<IEmbeddingService, VoyageEmbeddingService>();
builder.Services.AddScoped<RetrievalService>();
builder.Services.AddScoped<StudyTools>();
builder.Services.AddScoped<StudyAgent>();
builder.Services.AddSingleton<FileStorage>();
builder.Services.AddSingleton<IngestionQueue>();
builder.Services.AddHostedService<IngestionWorker>();

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:3000"])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

// Scaffold convenience: create the schema on startup. Switch to EF migrations once the model settles.
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
}

app.UseCors();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

var api = app.MapGroup("/api");

api.MapGet("/documents", async (AppDbContext db) =>
    await db.Documents
        .OrderByDescending(d => d.CreatedAt)
        .Select(d => new { d.Id, d.Title, d.FileName, Status = d.Status.ToString(), d.Error, d.PageCount, d.CreatedAt })
        .ToListAsync());

const long maxUploadBytes = 50 * 1024 * 1024;

api.MapPost("/documents", async (IFormFile file, AppDbContext db, FileStorage storage, IngestionQueue queue) =>
{
    if (file.Length is 0 or > maxUploadBytes)
        return Results.BadRequest(new { error = "File must be between 1 byte and 50 MB." });
    if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { error = "Only PDF files are supported for now." });

    var document = new StudyDocument
    {
        FileName = file.FileName,
        Title = Path.GetFileNameWithoutExtension(file.FileName),
    };

    await using (var stream = File.Create(storage.PathFor(document.Id)))
    {
        await file.CopyToAsync(stream);
    }

    db.Documents.Add(document);
    await db.SaveChangesAsync();
    await queue.EnqueueAsync(document.Id);

    return Results.Accepted($"/api/documents/{document.Id}", new { document.Id, document.Title, Status = document.Status.ToString() });
}).DisableAntiforgery();

api.MapDelete("/documents/{id:guid}", async (Guid id, AppDbContext db, FileStorage storage) =>
{
    var deleted = await db.Documents.Where(d => d.Id == id).ExecuteDeleteAsync();
    if (deleted == 0) return Results.NotFound();
    storage.Delete(id);
    return Results.NoContent();
});

// Streams agent events as Server-Sent Events: step -> sources -> quiz -> answer.
api.MapPost("/chat", async (ChatRequest request, StudyAgent agent, HttpContext http, ILogger<Program> logger) =>
{
    http.Response.Headers.ContentType = "text/event-stream";
    http.Response.Headers.CacheControl = "no-cache";
    var ct = http.RequestAborted;

    async Task Emit(AgentEvent e)
    {
        await http.Response.WriteAsync($"data: {JsonSerializer.Serialize(e, JsonSerializerOptions.Web)}\n\n", ct);
        await http.Response.Body.FlushAsync(ct);
    }

    try
    {
        await agent.RunAsync(request, Emit, ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        logger.LogError(ex, "Chat request failed");
        await Emit(new ErrorEvent("Something went wrong on my side. Please try again."));
    }

    await http.Response.WriteAsync("data: [DONE]\n\n", ct);
});

app.Run();
