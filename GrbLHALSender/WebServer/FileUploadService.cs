using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GrbLHALSender.WebServer;

public class FileUploadService
{
    private string _uploadDirectory = "";
    private WebServerConfig _config = new();

    /// <summary>
    /// Absolute path uploads are written to, or empty before Initialize has run.
    /// <para>
    /// Worth surfacing rather than leaving implicit. On Linux this resolves under
    /// $XDG_CONFIG_HOME — <c>~/.config/GrblHAL-Sender/GCodeFiles</c> by default — and a
    /// leading-dot directory is hidden from every file picker, so an operator who uploaded
    /// a file over the web server had no way to find it from the file browser.
    /// </para>
    /// </summary>
    public string UploadDirectory => _uploadDirectory;

    public void Initialize(WebServerConfig config)
    {
        _config = config;
        _uploadDirectory = Path.GetFullPath(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GrblHAL-Sender", config.UploadDirectory));

        if (!Directory.Exists(_uploadDirectory))
        {
            Directory.CreateDirectory(_uploadDirectory);
        }
    }

    /// <summary>
    /// Saves an uploaded file to the upload directory after validation.
    /// Returns the absolute path of the saved file.
    /// </summary>
    public async Task<string> SaveFileAsync(string fileName, Stream stream, long length)
    {
        if (length > _config.MaxFileSizeBytes)
            throw new InvalidOperationException(
                $"File exceeds maximum size of {_config.MaxFileSizeBytes / (1024 * 1024)} MB.");

        var ext = Path.GetExtension(fileName)?.ToLowerInvariant() ?? "";
        if (!_config.AllowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"File extension '{ext}' is not allowed. Allowed: {string.Join(", ", _config.AllowedExtensions)}");

        // Sanitize filename — keep only safe characters
        var safeName = SanitizeFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName))
            throw new InvalidOperationException("Invalid file name.");

        var filePath = Path.Combine(_uploadDirectory, safeName);

        // If file exists, add a timestamp suffix
        if (File.Exists(filePath))
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(safeName);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            safeName = $"{nameWithoutExt}_{timestamp}{ext}";
            filePath = Path.Combine(_uploadDirectory, safeName);
        }

        await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        await stream.CopyToAsync(fileStream);

        return filePath;
    }

    /// <summary>
    /// Lists all files in the upload directory, sorted by date descending.
    /// </summary>
    public List<FileInfo> ListFiles()
    {
        if (!Directory.Exists(_uploadDirectory))
            return [];

        return new DirectoryInfo(_uploadDirectory)
            .GetFiles()
            .Where(f => _config.AllowedExtensions.Contains(
                f.Extension.ToLowerInvariant(), StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(f => f.LastWriteTime)
            .ToList();
    }

    /// <summary>
    /// Gets the absolute path of a file in the upload directory.
    /// Returns null if the file does not exist.
    /// </summary>
    public string? GetFilePath(string fileName)
    {
        var safeName = SanitizeFileName(fileName);
        var filePath = Path.Combine(_uploadDirectory, safeName);

        // Prevent directory traversal
        if (!Path.GetFullPath(filePath).StartsWith(_uploadDirectory, StringComparison.OrdinalIgnoreCase))
            return null;

        return File.Exists(filePath) ? filePath : null;
    }

    /// <summary>
    /// Deletes a file from the upload directory.
    /// Returns true if the file was deleted.
    /// </summary>
    public bool DeleteFile(string fileName)
    {
        var path = GetFilePath(fileName);
        if (path == null) return false;

        File.Delete(path);
        return true;
    }

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName); // Strip any path components
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Where(c => !invalid.Contains(c)).ToArray());
    }
}
