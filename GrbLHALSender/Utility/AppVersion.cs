using System.Reflection;

namespace GrbLHALSender.Utility;

/// <summary>
/// The running build's version.
/// <para>
/// Read from the entry assembly's informational version, which MSBuild derives from
/// <c>Version</c> in Directory.Build.props — the single source of truth for the build
/// number. Nothing here should hard-code a version; bump it there.
/// </para>
/// </summary>
public static class AppVersion
{
    private static string? _current;
    private static bool _read;

    /// <summary>
    /// Version as "1.0.11", or null when the assembly carries no version attribute.
    /// </summary>
    public static string? Current
    {
        get
        {
            if (_read) return _current;
            _current = Read();
            _read = true;
            return _current;
        }
    }

    /// <summary>Version for display, with a placeholder rather than a blank.</summary>
    public static string Display => Current ?? "unknown";

    private static string? Read() =>
        Normalize(Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion);

    /// <summary>
    /// Reduces an informational version to its bare "1.0.11" form. Worth getting right
    /// in one place: the update check parses this to decide whether a newer release
    /// exists, and a leftover suffix makes it unparseable.
    /// </summary>
    internal static string? Normalize(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
            return null;

        var version = informationalVersion;

        // Strip metadata suffix like "+abc123" — MSBuild appends the commit hash.
        var plusIndex = version.IndexOf('+');
        if (plusIndex >= 0)
            version = version[..plusIndex];

        // Strip pre-release suffix like "-dev"
        var dashIndex = version.IndexOf('-');
        if (dashIndex >= 0)
            version = version[..dashIndex];

        return string.IsNullOrWhiteSpace(version) ? null : version;
    }
}
