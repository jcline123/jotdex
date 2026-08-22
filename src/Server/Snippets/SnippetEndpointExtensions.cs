using Jotdex.Core.Snippets;

namespace Jotdex.Server.Snippets;

public static class SnippetEndpointExtensions
{
    public static IEndpointRouteBuilder MapSnippetEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/snippets", (ISnippetIndex index, string? q, string? language, int? limit) =>
        {
            var items = index.List(q, language, limit ?? 50);
            return Results.Json(new
            {
                items = items.Select(SnippetDto.From).ToArray()
            });
        });

        app.MapPost("/api/snippets", async (HttpRequest request, ISnippetCommandService commands, CancellationToken ct) =>
        {
            CreateSnippetBody? body;
            try
            {
                body = await request.ReadFromJsonAsync<CreateSnippetBody>(ct);
            }
            catch
            {
                return Results.BadRequest(new { error = "Invalid JSON body." });
            }

            if (body is null || string.IsNullOrWhiteSpace(body.Title) || string.IsNullOrWhiteSpace(body.Code))
                return Results.BadRequest(new { error = "Title and code are required." });

            var created = commands.Create(new CreateSnippetRequest(
                body.Title,
                body.Trigger ?? "",
                body.Language ?? "plaintext",
                body.Code,
                body.Folder ?? "",
                body.Description,
                body.Tags));

            if (created is null)
                return Results.BadRequest(new { error = "Could not create snippet note." });

            return Results.Json(new { snippet = SnippetDto.From(created) });
        });

        return app;
    }

    private sealed class CreateSnippetBody
    {
        public string? Title { get; set; }
        public string? Trigger { get; set; }
        public string? Language { get; set; }
        public string? Code { get; set; }
        public string? Folder { get; set; }
        public string? Description { get; set; }
        public List<string>? Tags { get; set; }
    }

    private sealed record SnippetDto(
        Guid NoteId,
        string Title,
        string Trigger,
        string Language,
        string FolderPath,
        string RelativePath,
        string? Description,
        string Code,
        string[] Tags)
    {
        public static SnippetDto From(SnippetSummary s) => new(
            s.NoteId,
            s.Title,
            s.Trigger,
            s.Language,
            s.FolderPath,
            s.RelativePath,
            s.Description,
            s.Code,
            s.Tags.ToArray());
    }
}
