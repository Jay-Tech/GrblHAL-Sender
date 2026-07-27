using System.Collections.Generic;
using System.Linq;
using GrbLHALSender.Configuration;
using GrbLHALSender.Gcode;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// Tests for the user-defined pre/post G-code event rules. The risky part is trigger
/// matching: a rule on M6 that also fires on M61 (set current tool) would drop a dust
/// shoe at the wrong moment, and a rule that never fires leaves it down through homing.
/// </summary>
public class GcodeEventInjectorTests
{
    private static GcodeEventHook DustShoeOnHoming() => new()
    {
        Name = "Dust shoe up for homing",
        Trigger = "$H,G28",
        PreCommands = "M65P0|G4P0.2",
    };

    private static GcodeEventHook DustShoeOnToolChange() => new()
    {
        Name = "Dust shoe for tool change",
        Trigger = "M6",
        PreCommands = "M65P0|G4P0.2",
        PostCommands = "M64P0",
    };

    [Fact]
    public void Expand_WrapsHomingCommand()
    {
        var injector = new GcodeEventInjector([DustShoeOnHoming()]);

        Assert.Equal(["M65P0", "G4P0.2", "$H"], injector.Expand("$H", GcodeEventScope.Manual));
    }

    [Fact]
    public void Expand_WrapsToolChangeWithPreAndPost()
    {
        var injector = new GcodeEventInjector([DustShoeOnToolChange()]);

        Assert.Equal(
            ["M65P0", "G4P0.2", "T3M6", "M64P0"],
            injector.Expand("T3M6", GcodeEventScope.Manual));
    }

    [Theory]
    [InlineData("$H")]        // exact
    [InlineData("$HX")]       // single-axis homing shares the prefix
    [InlineData("g28")]       // case-insensitive
    [InlineData("G28 ")]      // trailing whitespace
    [InlineData("G28 (go home)")] // trailing comment
    [InlineData("G53G28")]    // trigger word alongside others
    public void Expand_Matches(string line)
    {
        var injector = new GcodeEventInjector([DustShoeOnHoming()]);

        Assert.Equal(3, injector.Expand(line, GcodeEventScope.Manual).Count);
    }

    [Theory]
    [InlineData("$$")]          // settings dump, not homing
    [InlineData("$X")]          // unlock
    [InlineData("G28.1")]       // sets the G28 position, does not move to it
    [InlineData("G280")]        // no such code, must not match on digits
    [InlineData("G0X28")]       // 28 as a coordinate, not a G-word
    [InlineData("(G28 in a comment)")]
    public void Expand_DoesNotMatch(string line)
    {
        var injector = new GcodeEventInjector([DustShoeOnHoming()]);

        Assert.Equal([line], injector.Expand(line, GcodeEventScope.Manual));
    }

    [Theory]
    [InlineData("M61Q3")]   // set current tool — must not be taken for a tool change
    [InlineData("M60")]
    [InlineData("M6.1")]
    public void Expand_M6Trigger_DoesNotMatchOtherMCodes(string line)
    {
        var injector = new GcodeEventInjector([DustShoeOnToolChange()]);

        Assert.Equal([line], injector.Expand(line, GcodeEventScope.Manual));
    }

    [Fact]
    public void Expand_M6Trigger_MatchesLeadingZeroForm()
    {
        var injector = new GcodeEventInjector([DustShoeOnToolChange()]);

        Assert.Equal(
            ["M65P0", "G4P0.2", "M06 T3", "M64P0"],
            injector.Expand("M06 T3", GcodeEventScope.Manual));
    }

    [Fact]
    public void Expand_SubstitutesWordAndLinePlaceholders()
    {
        var injector = new GcodeEventInjector([new GcodeEventHook
        {
            Trigger = "M6",
            PreCommands = "(pre for tool {T})",
            PostCommands = "M64P0|(was: {LINE})|(no such word: {Z})",
        }]);

        Assert.Equal(
            ["(pre for tool 3)", "T3 M6", "M64P0", "(was: T3 M6)", "(no such word: )"],
            injector.Expand("T3 M6", GcodeEventScope.Manual));
    }

    [Fact]
    public void Expand_IgnoresDisabledAndTriggerlessRules()
    {
        var disabled = DustShoeOnHoming();
        disabled.Enabled = false;
        var triggerless = new GcodeEventHook { Trigger = "  ", PreCommands = "M65P0" };

        var injector = new GcodeEventInjector([disabled, triggerless]);

        Assert.Equal(["$H"], injector.Expand("$H", GcodeEventScope.Manual));
        Assert.True(injector.IsEmpty);
    }

    [Fact]
    public void Expand_RespectsScopeFlags()
    {
        var jobOnly = DustShoeOnHoming();
        jobOnly.ApplyToManual = false;

        var injector = new GcodeEventInjector([jobOnly]);

        Assert.Equal(["$H"], injector.Expand("$H", GcodeEventScope.Manual));
        Assert.Equal(3, injector.Expand("$H", GcodeEventScope.Job).Count);
    }

    [Fact]
    public void Expand_DoesNotRescanInjectedCommands()
    {
        // A rule whose own pre-command contains its trigger must expand once, not recurse.
        var injector = new GcodeEventInjector([new GcodeEventHook
        {
            Trigger = "M64",
            PreCommands = "M64P1",
        }]);

        Assert.Equal(["M64P1", "M64P0"], injector.Expand("M64P0", GcodeEventScope.Manual));
    }

    [Fact]
    public void Expand_NestsOverlappingRules()
    {
        var outer = new GcodeEventHook { Trigger = "M6", PreCommands = "PRE-A", PostCommands = "POST-A" };
        var inner = new GcodeEventHook { Trigger = "T3", PreCommands = "PRE-B", PostCommands = "POST-B" };

        var injector = new GcodeEventInjector([outer, inner]);

        // Pre in rule order, post in reverse, so the first rule wraps the second.
        Assert.Equal(
            ["PRE-A", "PRE-B", "T3M6", "POST-B", "POST-A"],
            injector.Expand("T3M6", GcodeEventScope.Manual));
    }

    [Fact]
    public void ExpandBlock_ExpandsEachLineOfMultiLineInput()
    {
        var injector = new GcodeEventInjector([DustShoeOnHoming()]);

        Assert.Equal(
            ["G21", "M65P0", "G4P0.2", "$H", "G0X0"],
            injector.ExpandBlock("G21\r\n$H\r\nG0X0", GcodeEventScope.Manual));
    }

    [Fact]
    public void ApplyToJob_InsertsLinesAndRenumbers()
    {
        var lines = new List<GCodeLine>
        {
            new("G21", 0),
            new("T3M6", 1),
            new("G0X10", 2),
        };

        var result = new GcodeEventInjector([DustShoeOnToolChange()]).ApplyToJob(lines);

        Assert.Equal(
            ["G21", "M65P0", "G4P0.2", "T3M6", "M64P0", "G0X10"],
            result.Select(l => l.Text));
        Assert.Equal([0, 1, 2, 3, 4, 5], result.Select(l => l.LineNumber));
        // Only the wrapper commands are flagged — the file's own lines are not.
        Assert.Equal([false, true, true, false, true, false], result.Select(l => l.IsInjected));
    }

    [Fact]
    public void ApplyToJob_ReturnsSameListWhenNothingMatches()
    {
        var lines = new List<GCodeLine> { new("G21", 0), new("G0X10", 1) };

        var result = new GcodeEventInjector([DustShoeOnToolChange()]).ApplyToJob(lines);

        Assert.Same(lines, result);
    }

    [Fact]
    public void ApplyToJob_SkipsRulesNotScopedToJobs()
    {
        var manualOnly = DustShoeOnToolChange();
        manualOnly.ApplyToJob = false;
        var lines = new List<GCodeLine> { new("T3M6", 0) };

        var result = new GcodeEventInjector([manualOnly]).ApplyToJob(lines);

        Assert.Same(lines, result);
    }

    [Fact]
    public void SetHooks_PicksUpConfigChanges()
    {
        var injector = new GcodeEventInjector();
        Assert.True(injector.IsEmpty);
        Assert.Equal(["$H"], injector.Expand("$H", GcodeEventScope.Manual));

        injector.SetHooks([DustShoeOnHoming()]);

        Assert.Equal(["M65P0", "G4P0.2", "$H"], injector.Expand("$H", GcodeEventScope.Manual));
    }

    [Fact]
    public void SynchronizedDustShoeRule_ExpandsToSeparateWellFormedLines()
    {
        // The rule as configured on the machine: synchronized aux codes (M63/M62) with a
        // dwell either side of the tool change. Pinned because a malformed or merged line
        // reaching the controller mid-job is indistinguishable, from the operator's seat,
        // from a firmware problem.
        var hook = new GcodeEventHook
        {
            Trigger = "M6",
            PreCommands = "M63P0|G4P0.2",
            PostCommands = "M62P0|G4P1",
        };

        var lines = new List<GCodeLine> { new("G1X10Y10F1000", 0), new("T2M6", 1), new("G1X20", 2) };
        var result = new GcodeEventInjector([hook]).ApplyToJob(lines);

        Assert.Equal(
            ["G1X10Y10F1000", "M63P0", "G4P0.2", "T2M6", "M62P0", "G4P1", "G1X20"],
            result.Select(l => l.Text));

        // No line carries a separator, stray whitespace, or a merged neighbour.
        Assert.All(result, l =>
        {
            Assert.DoesNotContain('|', l.Text);
            Assert.Equal(l.Text.Trim(), l.Text);
        });
    }

    [Fact]
    public void Hooks_SurviveConfigRoundTrip()
    {
        // The rules live in the app config JSON. A serialization gap would silently
        // drop them on restart, so round-trip the way ConfigManager does.
        var config = new GHalSenderConfig
        {
            GcodeEvents = [DustShoeOnHoming(), DustShoeOnToolChange()]
        };

        var json = System.Text.Json.JsonSerializer.Serialize(config);
        var restored = System.Text.Json.JsonSerializer.Deserialize<GHalSenderConfig>(json);

        Assert.NotNull(restored);
        Assert.Equal(2, restored.GcodeEvents.Count);
        var toolChange = restored.GcodeEvents[1];
        Assert.True(toolChange.Enabled);
        Assert.Equal("M6", toolChange.Trigger);
        Assert.Equal("M65P0|G4P0.2", toolChange.PreCommands);
        Assert.Equal("M64P0", toolChange.PostCommands);
        Assert.True(toolChange.ApplyToJob);
        Assert.True(toolChange.ApplyToManual);

        Assert.Equal(
            ["M65P0", "G4P0.2", "T1M6", "M64P0"],
            new GcodeEventInjector(restored.GcodeEvents).Expand("T1M6", GcodeEventScope.Manual));
    }

    [Fact]
    public void MultiWordTrigger_MatchesAsSubstring()
    {
        var injector = new GcodeEventInjector([new GcodeEventHook
        {
            Trigger = "G65P231",
            PreCommands = "M65P0",
        }]);

        Assert.Equal(["M65P0", "G65 P231"], injector.Expand("G65 P231", GcodeEventScope.Manual));
        Assert.Equal(["G65P232"], injector.Expand("G65P232", GcodeEventScope.Manual));
    }
}
