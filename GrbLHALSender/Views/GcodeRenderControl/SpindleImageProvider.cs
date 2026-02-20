using SkiaSharp;
using System;
using System.IO;

namespace GrbLHALSender.Views.GcodeRenderControl
{
    /// <summary>
    /// Loads and caches an SKBitmap from the Config folder for use as the spindle image.
    /// Returns null if the file is not found, signalling fallback to procedural drawing.
    /// Thread-safe: the cached bitmap reference is replaced atomically.
    /// </summary>
    public class SpindleImageProvider : IDisposable
    {
        private SKBitmap? _cachedBitmap;
        private string? _cachedPath;
        private readonly object _lock = new();

        /// <summary>
        /// Gets the currently loaded spindle bitmap, or null if none is loaded.
        /// </summary>
        public SKBitmap? Bitmap
        {
            get
            {
                lock (_lock)
                    return _cachedBitmap;
            }
        }

        /// <summary>
        /// Attempts to load a spindle image from the Config folder.
        /// The <paramref name="relativePath"/> is resolved relative to
        /// {AppDomain.CurrentDomain.BaseDirectory}/Config/.
        /// If the path is null/empty or the file doesn't exist, the cached bitmap is cleared
        /// and null is returned (caller should fall back to procedural drawing).
        /// The bitmap is only reloaded when the path actually changes.
        /// </summary>
        public SKBitmap? TryLoad(string? relativePath)
        {
            lock (_lock)
            {
                // Normalise empty to null
                if (string.IsNullOrWhiteSpace(relativePath))
                    relativePath = null;

                // Already loaded this exact path — return cached result
                if (relativePath == _cachedPath)
                    return _cachedBitmap;

                // Dispose old bitmap
                _cachedBitmap?.Dispose();
                _cachedBitmap = null;
                _cachedPath = relativePath;

                if (relativePath == null)
                    return null;

                try
                {
                    var configDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config");
                    var fullPath = Path.GetFullPath(Path.Combine(configDir, relativePath));

                    // Security: ensure resolved path stays within Config folder
                    if (!fullPath.StartsWith(configDir, StringComparison.OrdinalIgnoreCase))
                        return null;

                    if (!File.Exists(fullPath))
                        return null;

                    _cachedBitmap = SKBitmap.Decode(fullPath);
                }
                catch
                {
                    // Corrupt file, unsupported format, etc. — silently fall back
                    _cachedBitmap = null;
                }

                return _cachedBitmap;
            }
        }

        /// <summary>
        /// Forces a reload of the current path on the next TryLoad call.
        /// Useful when the user replaces the file on disk without changing the path.
        /// </summary>
        public void Invalidate()
        {
            lock (_lock)
            {
                _cachedBitmap?.Dispose();
                _cachedBitmap = null;
                _cachedPath = null;
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _cachedBitmap?.Dispose();
                _cachedBitmap = null;
                _cachedPath = null;
            }
        }
    }
}
