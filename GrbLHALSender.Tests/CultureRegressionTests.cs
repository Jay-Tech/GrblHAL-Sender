using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using GrbLHALSender.Gcode;
using GrbLHALSender.Probe;
using GrbLHALSender.Settings;
using GrbLHALSender.Utility;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// Regression tests for region/culture number-format bugs.
/// grblHAL always talks dot-decimal ("12.345"); on machines whose OS region uses
/// comma decimals (de-DE, fr-FR, etc.) any culture-sensitive Parse/ToString
/// silently corrupts values: de-DE reads "12.345" as 12345 (dot = thousands
/// separator), fr-FR fails the parse entirely and values collapse to 0.
/// Every test runs the production code under a forced comma-decimal culture.
/// </summary>
public class CultureRegressionTests
{
    /// <summary>Runs an action with the current thread forced to the given culture, then restores.</summary>
    private static void RunInCulture(string cultureName, Action action)
    {
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo(cultureName);
            action();
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    // --- DRO / position report parsing (the "0,000 DRO" and wrong-position symptom) ---

    [Theory]
    [InlineData("de-DE")] // comma decimal, dot thousands: "12.345" -> 12345 if culture-parsed
    [InlineData("fr-FR")] // comma decimal, space thousands: "12.345" fails -> 0 if culture-parsed
    [InlineData("tr-TR")]
    public void StringToDouble_ParsesGrblDotDecimal_InCommaCultures(string culture)
    {
        RunInCulture(culture, () =>
        {
            // Same string grblHAL sends in <...|MPos:12.345,...> status reports
            Assert.Equal(12.345, "12.345".StringToDouble(), 3);
            Assert.Equal(-0.5, "-0.5".StringToDouble(), 3);
            Assert.Equal(0.0, "0.000".StringToDouble(), 3);
        });
    }

    // --- Machine settings ($130 travel, $110 rapid) parsing ---

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public void MachineSettings_ParseDotDecimal_InCommaCultures(string culture)
    {
        RunInCulture(culture, () =>
        {
            var settings = new MachineSettings();
            settings.SetXBoundaries("812.800"); // typical $130 value
            settings.SetXRapid("5000.000");     // typical $110 value

            Assert.Equal(812.8, settings.XSize, 3);   // de-DE culture-parse gives 812800!
            Assert.Equal(5000.0, settings.XRapid, 3); // de-DE culture-parse gives 5000000!
        });
    }

    // --- Probe math (touch plate thickness, probe diameter) ---

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public void ProbeJobBuilder_Offsets_ParseDotDecimal_InCommaCultures(string culture)
    {
        RunInCulture(culture, () =>
        {
            var builder = new ProbeJobBuilder
            {
                ToolType = ProbeToolType.TouchPlate,
                TouchPlateThickness = "19.05", // 3/4" plate stored invariantly
            };

            Assert.Equal(19.05, builder.CalculateZOffset(), 3);

            // A 3D probe needs no Z compensation at all: the ball meets the surface with its
            // underside, directly below its centre, so the trigger is the surface. Returning
            // the stylus radius here put work Z a radius low — measured on hardware as 0.039in
            // with a 0.0787in stylus. Radius belongs to a side touch only.
            builder.ToolType = ProbeToolType.Probe3D;
            Assert.Equal(0, builder.CalculateZOffset(), 3);
        });
    }

    [Theory]
    [InlineData("de-DE")]
    public void ProbeJobBuilder_BossCenter_EmitsDotDecimalGcode(string culture)
    {
        RunInCulture(culture, () =>
        {
            var builder = new ProbeJobBuilder
            {
                ProbeSearchRate = "100",
                ProbeLatchRate = "25",
                ProbeDistance = "10.5",
                LatchDistance = "1.5",
                ClearanceHeight = "5.0",
            };

            var phases = builder.ProbeBossCenter("50.8");
            var allCommands = phases.SelectMany(p => p).ToList();

            // halfSize = 50.8/2 + 10.5 = 35.9 -> first phase moves G0X35.9
            Assert.Contains(allCommands, c => c.StartsWith("G0X") && c.Contains("35.9"));
            // No emitted command may contain a comma-decimal — grblHAL would reject/misread it
            Assert.All(allCommands, c => Assert.DoesNotContain(",", c));
        });
    }

    // --- G-code file load pipeline (the "Start button never enables" symptom) ---

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public void GCodeCommand_ParsesCoordinates_InCommaCultures(string culture)
    {
        RunInCulture(culture, () =>
        {
            var cmd = new GCodeCommand("G1 X12.345 Y-0.5 Z0.125 F1500.0");
            Assert.Equal(1, cmd.GCode);
            Assert.Equal(12.345f, cmd.GetParam('X'), 3);
            Assert.Equal(-0.5f, cmd.GetParam('Y'), 3);
            Assert.Equal(0.125f, cmd.GetParam('Z'), 3);
            Assert.Equal(1500f, cmd.GetParam('F'), 1);
        });
    }

    [Theory]
    [InlineData("de-DE")]
    public void ToolpathBuilder_BuildsCorrectBounds_InCommaCultures(string culture)
    {
        RunInCulture(culture, () =>
        {
            var lines = new List<GCodeLine>
            {
                new("G21", 0),
                new("G90", 1),
                new("G0 X0 Y0 Z5.0", 2),
                new("G1 X10.5 Y20.25 Z-1.5 F1000", 3),
                new("G1 X-5.125 Y0", 4),
            };

            var toolpath = new ToolpathBuilder().BuildToolpath(lines);

            Assert.NotEmpty(toolpath.Segments);
            Assert.Equal(-5.125f, toolpath.MinBounds.X, 3);
            Assert.Equal(10.5f, toolpath.MaxBounds.X, 3);
            Assert.Equal(20.25f, toolpath.MaxBounds.Y, 3);
        });
    }

    // --- Outbound formatting (jog/probe/surfacing commands sent to grblHAL) ---

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public void JogCommand_Interpolation_EmitsDotDecimal(string culture)
    {
        RunInCulture(culture, () =>
        {
            // Same shape GamepadService/MainViewModel build for $J= jogs.
            // Plain $"X{jogStep}" would render "X0,1" in de-DE — grblHAL reads X0 and
            // rejects the rest, so fine-step jog (0.1/0.01) silently failed while
            // whole-number steps appeared to work.
            double jogStep = 0.1, jogRate = 1000;
            var cmd = $"$J=G91G21X{jogStep.ToInvariantString()}F{jogRate.ToInvariantString()}";
            Assert.Equal("$J=G91G21X0.1F1000", cmd);
        });
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public void ToInvariantString_AlwaysEmitsDotDecimal(string culture)
    {
        RunInCulture(culture, () =>
        {
            Assert.Equal("12.345", 12.345.ToInvariantString());
            Assert.Equal("12.345", 12.345.ToInvariantString("F3"));
            Assert.Equal("0.000", 0.0.ToInvariantString("F3"));
        });
    }
}
