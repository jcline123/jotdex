using Jotdex.Core.Templates;
using Jotdex.Core.Vault;
using Jotdex.Infrastructure.Vault;

namespace Jotdex.Server.Templates;

public static class TemplateEndpointExtensions
{
    public static IEndpointRouteBuilder MapTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/templates", (INoteTemplateService templates, string? q, int? limit) =>
        {
            var items = templates.List(q, limit ?? 100);
            return Results.Json(new { items = items.Select(TemplateDto.From).ToArray() });
        });

        app.MapGet("/api/templates/{id:guid}", (Guid id, INoteTemplateService templates) =>
        {
            var note = templates.Get(id);
            return note is null ? Results.NotFound(new { error = "Template not found." }) : Results.Json(note);
        });

        app.MapPost("/api/templates", async (HttpRequest request, INoteTemplateService templates, CancellationToken ct) =>
        {
            CreateTemplateBody? body;
            try { body = await request.ReadFromJsonAsync<CreateTemplateBody>(ct); }
            catch { return Results.BadRequest(new { error = "Invalid JSON body." }); }

            if (body is null || string.IsNullOrWhiteSpace(body.Title))
                return Results.BadRequest(new { error = "Title is required." });

            var (created, error) = templates.Create(new CreateTemplateRequest(
                body.Title,
                body.Markdown ?? body.BodyMarkdown ?? $"# {body.Title.Trim()}\n\n",
                body.Description,
                body.Tags));

            if (created is null)
                return Results.BadRequest(new { error = error ?? "Could not create template." });

            return Results.Json(new { template = TemplateDto.From(created) });
        });

        app.MapPut("/api/templates/{id:guid}", async (Guid id, HttpRequest request, INoteTemplateService templates, CancellationToken ct) =>
        {
            UpdateTemplateBody? body;
            try { body = await request.ReadFromJsonAsync<UpdateTemplateBody>(ct); }
            catch { return Results.BadRequest(new { error = "Invalid JSON body." }); }

            if (body is null || string.IsNullOrWhiteSpace(body.Title))
                return Results.BadRequest(new { error = "Title is required." });

            var (updated, error) = templates.Update(id, new UpdateTemplateRequest(
                body.Title,
                body.Markdown ?? body.BodyMarkdown ?? "",
                body.Description,
                body.Tags,
                body.ETag ?? body.Etag ?? ""));

            if (updated is null)
                return Results.BadRequest(new { error = error ?? "Could not update template." });

            return Results.Json(new { template = TemplateDto.From(updated) });
        });

        app.MapPost("/api/templates/from-note", async (HttpRequest request, INoteTemplateService templates, CancellationToken ct) =>
        {
            FromNoteBody? body;
            try { body = await request.ReadFromJsonAsync<FromNoteBody>(ct); }
            catch { return Results.BadRequest(new { error = "Invalid JSON body." }); }

            if (body?.NoteId is null || body.NoteId == Guid.Empty)
                return Results.BadRequest(new { error = "noteId is required." });

            var (created, error) = templates.SaveFromNote(new SaveNoteAsTemplateRequest(
                body.NoteId.Value,
                body.Title,
                body.Description));

            if (created is null)
                return Results.BadRequest(new { error = error ?? "Could not save as template." });

            return Results.Json(new { template = TemplateDto.From(created) });
        });

        app.MapPost("/api/templates/{id:guid}/instantiate", async (Guid id, HttpRequest request, INoteTemplateService templates, CancellationToken ct) =>
        {
            InstantiateBody? body;
            try { body = await request.ReadFromJsonAsync<InstantiateBody>(ct); }
            catch { return Results.BadRequest(new { error = "Invalid JSON body." }); }

            if (body is null || string.IsNullOrWhiteSpace(body.Title))
                return Results.BadRequest(new { error = "Title is required." });

            var (note, error) = templates.Instantiate(id, new InstantiateTemplateRequest(
                body.Title,
                body.Folder ?? body.FolderPath ?? ""));

            if (note is null)
                return Results.BadRequest(new { error = error ?? "Could not create note from template." });

            return Results.Json(note);
        });

        app.MapPost("/api/templates/{id:guid}/prepare-insert", async (Guid id, HttpRequest request, INoteTemplateService templates, CancellationToken ct) =>
        {
            PrepareInsertBody? body;
            try { body = await request.ReadFromJsonAsync<PrepareInsertBody>(ct); }
            catch { return Results.BadRequest(new { error = "Invalid JSON body." }); }

            if (body?.TargetNoteId is null || body.TargetNoteId == Guid.Empty)
                return Results.BadRequest(new { error = "targetNoteId is required." });

            var result = templates.PrepareInsert(id, body.TargetNoteId.Value);
            if (!result.Success)
                return Results.BadRequest(new { error = result.Error ?? "Could not prepare insert." });

            return Results.Json(new
            {
                success = true,
                markdown = result.Markdown,
                note = result.TargetNote
            });
        });

        app.MapDelete("/api/templates/{id:guid}", (Guid id, INoteCommandService notes, INoteTemplateService templates) =>
        {
            if (templates.Get(id) is null)
                return Results.NotFound(new { error = "Template not found." });
            var ok = notes.MoveToTrash(id);
            return ok
                ? Results.Json(new { success = true })
                : Results.BadRequest(new { error = "Could not move template to trash." });
        });

        return app;
    }

    private sealed class CreateTemplateBody
    {
        public string? Title { get; set; }
        public string? Markdown { get; set; }
        public string? BodyMarkdown { get; set; }
        public string? Description { get; set; }
        public List<string>? Tags { get; set; }
    }

    private sealed class UpdateTemplateBody
    {
        public string? Title { get; set; }
        public string? Markdown { get; set; }
        public string? BodyMarkdown { get; set; }
        public string? Description { get; set; }
        public List<string>? Tags { get; set; }
        public string? ETag { get; set; }
        public string? Etag { get; set; }
    }

    private sealed class FromNoteBody
    {
        public Guid? NoteId { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
    }

    private sealed class InstantiateBody
    {
        public string? Title { get; set; }
        public string? Folder { get; set; }
        public string? FolderPath { get; set; }
    }

    private sealed class PrepareInsertBody
    {
        public Guid? TargetNoteId { get; set; }
    }

    private sealed record TemplateDto(
        Guid NoteId,
        string Title,
        string FolderPath,
        string RelativePath,
        string? Description,
        string[] Tags,
        DateTimeOffset? Modified,
        bool HasAttachments)
    {
        public static TemplateDto From(NoteTemplateSummary s) => new(
            s.NoteId,
            s.Title,
            s.FolderPath,
            s.RelativePath,
            s.Description,
            s.Tags.ToArray(),
            s.Modified,
            s.HasAttachments);
    }
}
