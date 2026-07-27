using System;
using System.IO;

namespace GrbLHALSender.Utility;

/// <summary>
/// Ensures only one copy of the app runs at a time.
/// <para>
/// Two instances both auto-connecting fight over the same serial port: whichever opens
/// it first wins and the second fails, or worse, they take turns across a reconnect and
/// the operator ends up driving a window whose commands go nowhere.
/// </para>
/// <para>
/// Implemented as a lock file held open for the process lifetime with
/// <see cref="FileShare.None"/>, which behaves the same on Windows and Linux. There is
/// no staleness problem: the OS releases the handle when the process dies, however it
/// dies, so a crashed instance never locks the next one out.
/// </para>
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private FileStream? _lock;

    /// <summary>Path of the lock file, for diagnostics.</summary>
    public string LockFilePath { get; }

    private SingleInstance(string lockFilePath, FileStream held)
    {
        LockFilePath = lockFilePath;
        _lock = held;
    }

    /// <summary>
    /// Claims the single-instance lock, or returns null when another instance holds it.
    /// Keep the returned object alive for as long as the app runs.
    /// </summary>
    public static SingleInstance? TryAcquire()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GrblHAL-Sender");
        var path = Path.Combine(directory, "instance.lock");

        try
        {
            Directory.CreateDirectory(directory);

            var stream = new FileStream(
                path, FileMode.Create, FileAccess.Write, FileShare.None);

            // The pid is only here to make a stuck lock diagnosable by hand; nothing
            // reads it back, so a failure to write it must not deny the lock.
            try
            {
                using var writer = new StreamWriter(stream, leaveOpen: true);
                writer.Write(Environment.ProcessId);
                writer.Flush();
            }
            catch (Exception)
            {
                // Lock is held either way — that is what matters.
            }

            return new SingleInstance(path, stream);
        }
        catch (IOException)
        {
            // Held by another instance.
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            // Cannot create or open the lock file at all. Refusing to start over a
            // permissions problem would be worse than the race it guards against, so
            // report the lock as acquired and let the app run.
            return new SingleInstance(path, null!);
        }
    }

    public void Dispose()
    {
        try
        {
            _lock?.Dispose();
        }
        catch (Exception)
        {
            // Shutting down; a failure to release the handle changes nothing because
            // the OS releases it when the process exits.
        }
        _lock = null;
    }
}
