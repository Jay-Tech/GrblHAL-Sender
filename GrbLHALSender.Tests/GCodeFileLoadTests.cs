using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GrbLHALSender.Gcode;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// Tests for the G-code file load pipeline. The load runs on a fire-and-forget
/// background task; before the error callback existed, any exception (bad path,
/// unreadable file, parse crash) vanished silently — FileLoaded stayed false and
/// the Start button never enabled with no feedback at all.
/// </summary>
public class GCodeFileLoadTests
{
    [Fact]
    public async Task ParseGCodeFile_ReportsError_WhenFileDoesNotExist()
    {
        var parser = new GCodeParser();
        var errorReceived = new TaskCompletionSource<Exception>();

        parser.ParseGCodeFile(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.nc"),
            _ => errorReceived.TrySetException(new Exception("Callback should not fire for a missing file")),
            ex => errorReceived.TrySetResult(ex));

        var completed = await Task.WhenAny(errorReceived.Task, Task.Delay(5000));
        Assert.Same(errorReceived.Task, completed); // silent swallow = timeout = fail
        Assert.IsAssignableFrom<IOException>(await errorReceived.Task);
    }

    [Fact]
    public async Task ParseGCodeFile_ReportsError_WhenCallbackThrows()
    {
        var file = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(file, "G0 X1\nG1 X2 F100\n");
            var parser = new GCodeParser();
            var errorReceived = new TaskCompletionSource<Exception>();

            // Simulates FileComplete (toolpath build / UI update) crashing after parse
            parser.ParseGCodeFile(
                file,
                _ => throw new InvalidOperationException("boom in FileComplete"),
                ex => errorReceived.TrySetResult(ex));

            var completed = await Task.WhenAny(errorReceived.Task, Task.Delay(5000));
            Assert.Same(errorReceived.Task, completed);
            Assert.Equal("boom in FileComplete", (await errorReceived.Task).Message);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task ParseGCodeFile_DeliversLines_ForValidFile()
    {
        var file = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(file, "(header comment)\nG21\nG90\nG1 X12.345 Y-0.5 F1500\n\n");
            var parser = new GCodeParser();
            var linesReceived = new TaskCompletionSource<List<GCodeLine>>();

            parser.ParseGCodeFile(
                file,
                lines => linesReceived.TrySetResult(lines),
                ex => linesReceived.TrySetException(ex));

            var completed = await Task.WhenAny(linesReceived.Task, Task.Delay(5000));
            Assert.Same(linesReceived.Task, completed);

            var lines = await linesReceived.Task;
            Assert.Equal(3, lines.Count); // comment and blank line filtered out
            Assert.Equal("G1 X12.345 Y-0.5 F1500", lines[2].Text);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
