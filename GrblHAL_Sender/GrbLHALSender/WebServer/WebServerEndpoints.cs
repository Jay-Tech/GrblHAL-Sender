using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System;
using System.Text.Json;

namespace GrbLHALSender.WebServer;

public static class WebServerEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void MapEndpoints(this IEndpointRouteBuilder app,
        WebServerService webServerService, FileUploadService fileUploadService)
    {
        // GET /api/status — machine state, position, feed, spindle, file info
        app.MapGet("/api/status", () =>
        {
            var status = webServerService.GetMachineStatus();
            return Results.Json(status, JsonOptions);
        });

        // GET /api/files — list uploaded files
        app.MapGet("/api/files", () =>
        {
            var files = fileUploadService.ListFiles();
            var result = files.ConvertAll(f => new
            {
                f.Name,
                Size = f.Length,
                LastModified = f.LastWriteTimeUtc
            });
            return Results.Json(result, JsonOptions);
        });

        // POST /api/files/upload — upload a G-code file
        app.MapPost("/api/files/upload", async (HttpRequest request) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(new { error = "Expected multipart form data." });

            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");
            if (file == null || file.Length == 0)
                return Results.BadRequest(new { error = "No file provided." });

            try
            {
                var path = await fileUploadService.SaveFileAsync(
                    file.FileName, file.OpenReadStream(), file.Length);
                return Results.Ok(new { fileName = System.IO.Path.GetFileName(path), path });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // POST /api/files/load — load an existing file into the sender
        app.MapPost("/api/files/load", async (HttpRequest request) =>
        {
            var body = await JsonSerializer.DeserializeAsync<LoadFileRequest>(
                request.Body, JsonOptions);

            if (body == null || string.IsNullOrWhiteSpace(body.FileName))
                return Results.BadRequest(new { error = "fileName is required." });

            var filePath = fileUploadService.GetFilePath(body.FileName);
            if (filePath == null)
                return Results.NotFound(new { error = "File not found." });

            webServerService.LoadFileIntoSender(filePath, body.FileName);
            return Results.Ok(new { message = $"{body.FileName} loading into sender." });
        });

        // POST /api/files/upload-and-load — upload + immediately load
        app.MapPost("/api/files/upload-and-load", async (HttpRequest request) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(new { error = "Expected multipart form data." });

            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");
            if (file == null || file.Length == 0)
                return Results.BadRequest(new { error = "No file provided." });

            try
            {
                var path = await fileUploadService.SaveFileAsync(
                    file.FileName, file.OpenReadStream(), file.Length);
                var name = System.IO.Path.GetFileName(path);
                webServerService.LoadFileIntoSender(path, name);
                return Results.Ok(new { fileName = name, path, message = $"{name} uploaded and loading." });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // DELETE /api/files/{fileName} — delete an uploaded file
        app.MapDelete("/api/files/{fileName}", (string fileName) =>
        {
            if (fileUploadService.DeleteFile(fileName))
                return Results.Ok(new { message = $"{fileName} deleted." });
            return Results.NotFound(new { error = "File not found." });
        });
    }

    private record LoadFileRequest(string FileName);
}
