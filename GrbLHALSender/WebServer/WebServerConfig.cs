namespace GrbLHALSender.WebServer;

public class WebServerConfig
{
    /// <summary>
    /// Enable the embedded web server for remote file uploads.
    /// Requires app restart to take effect.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// TCP port for the web server (default: 5100).
    /// </summary>
    public int Port { get; set; } = 5100;

    /// <summary>
    /// Bind address. Use "0.0.0.0" for LAN access, "127.0.0.1" for localhost only.
    /// </summary>
    public string BindAddress { get; set; } = "0.0.0.0";

    /// <summary>
    /// Directory for uploaded G-code files (relative to app directory).
    /// </summary>
    public string UploadDirectory { get; set; } = "GCodeFiles";

    /// <summary>
    /// Maximum upload file size in bytes (default: 50 MB).
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 50 * 1024 * 1024;

    /// <summary>
    /// Allowed file extensions for upload.
    /// </summary>
    public string[] AllowedExtensions { get; set; } = [".nc", ".gcode", ".ngc", ".tap", ".gc"];
}
