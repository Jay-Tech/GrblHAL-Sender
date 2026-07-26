using GrbLHALSender.Configuration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace GrbLHALSender.Gcode;

/// <summary>Which send path a line is travelling on, matched against the hook's Apply* flags.</summary>
public enum GcodeEventScope
{
    /// <summary>Operator-issued: MDI, panel buttons, macros, gamepad, web UI.</summary>
    Manual,

    /// <summary>Lines of a loaded job file.</summary>
    Job
}

/// <summary>
/// Expands outgoing G-code by wrapping user-configured trigger commands with
/// pre/post commands. Rules come from <see cref="GHalSenderConfig.GcodeEvents"/>,
/// so which events fire and what they inject is entirely user-defined.
/// <para>
/// Injected commands are never themselves scanned for triggers — a rule whose
/// pre-command contains its own trigger expands once, not forever.
/// </para>
/// </summary>
public sealed class GcodeEventInjector
{
    // Letter + number, tolerating the space some posts emit ("M 6", "T 3").
    private static readonly Regex WordRegex =
        new(@"([A-Z])\s*(-?\d*\.?\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // {LINE} or {T}-style placeholders inside pre/post command text.
    private static readonly Regex PlaceholderRegex =
        new(@"\{([A-Za-z]+)\}", RegexOptions.Compiled);

    private static readonly char[] CommandSeparators = ['|', '\n', '\r'];

    private List<GcodeEventHook> _hooks = [];

    /// <summary>
    /// Rules currently in effect, in evaluation order. Read straight from the live
    /// config object so edits saved in the config dialog take effect immediately.
    /// </summary>
    public IReadOnlyList<GcodeEventHook> Hooks => _hooks;

    public GcodeEventInjector()
    {
    }

    public GcodeEventInjector(ConfigManager configManager)
    {
        configManager.OnConfigLoaded += (_, cfg) => SetHooks(cfg.GcodeEvents);
        configManager.OnConfigSaved += (_, cfg) => SetHooks(cfg.GcodeEvents);
        if (configManager.GHalSenderConfig != null)
            SetHooks(configManager.GHalSenderConfig.GcodeEvents);
    }

    public GcodeEventInjector(IEnumerable<GcodeEventHook> hooks) => SetHooks(hooks);

    public void SetHooks(IEnumerable<GcodeEventHook>? hooks)
    {
        // Only usable rules are kept, so the hot path (every streamed line) never
        // re-checks Enabled or an empty trigger.
        _hooks = hooks?
                     .Where(h => h.Enabled && !string.IsNullOrWhiteSpace(h.Trigger))
                     .ToList()
                 ?? [];
    }

    /// <summary>True when no rule could possibly fire, letting callers skip expansion entirely.</summary>
    public bool IsEmpty => _hooks.Count == 0;

    /// <summary>
    /// Expands a single command. Returns the original line unchanged (as a one-item
    /// list) when no rule matches.
    /// </summary>
    public List<string> Expand(string line, GcodeEventScope scope)
    {
        var result = new List<string>(1);
        ExpandInto(result, line, scope);
        return result;
    }

    /// <summary>
    /// Expands text that may hold several commands (an MDI entry or a multi-line
    /// macro), splitting on newlines first so each command is matched on its own.
    /// </summary>
    public List<string> ExpandBlock(string text, GcodeEventScope scope)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        foreach (var part in text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var command = part.Trim();
            if (command.Length > 0)
                ExpandInto(result, command, scope);
        }

        return result;
    }

    /// <summary>
    /// Applies the job-scoped rules to a parsed file, returning a new list with the
    /// injected lines in place and line numbers renumbered. The input list is returned
    /// untouched when nothing matched, so files with no matching rule cost no copy.
    /// </summary>
    public List<GCodeLine> ApplyToJob(List<GCodeLine> lines)
    {
        if (IsEmpty || lines.Count == 0) return lines;
        if (!_hooks.Any(h => h.ApplyToJob)) return lines;

        var expanded = new List<GCodeLine>(lines.Count);
        var buffer = new List<string>(4);
        var injected = false;

        foreach (var source in lines)
        {
            buffer.Clear();
            ExpandInto(buffer, source.Text, GcodeEventScope.Job);

            if (buffer.Count == 1 && ReferenceEquals(buffer[0], source.Text))
            {
                source.LineNumber = expanded.Count;
                expanded.Add(source);
                continue;
            }

            injected = true;
            foreach (var text in buffer)
            {
                if (ReferenceEquals(text, source.Text))
                {
                    // The triggering line keeps its own object; only the wrapper
                    // commands are flagged, so the gcode view can mark them.
                    source.LineNumber = expanded.Count;
                    expanded.Add(source);
                }
                else
                {
                    expanded.Add(new GCodeLine(text, expanded.Count) { IsInjected = true });
                }
            }
        }

        return injected ? expanded : lines;
    }

    private void ExpandInto(List<string> output, string line, GcodeEventScope scope)
    {
        if (_hooks.Count == 0 || string.IsNullOrWhiteSpace(line))
        {
            output.Add(line);
            return;
        }

        var normalized = Normalize(line);
        List<GcodeEventHook>? matched = null;

        foreach (var hook in _hooks)
        {
            if (!AppliesTo(hook, scope)) continue;
            if (!MatchesAnyTrigger(normalized, hook.Trigger)) continue;
            (matched ??= []).Add(hook);
        }

        if (matched == null)
        {
            // Reference-equal to the input — ApplyToJob relies on this to detect "no change".
            output.Add(line);
            return;
        }

        foreach (var hook in matched)
            AppendCommands(output, hook.PreCommands, line);

        output.Add(line);

        // Reverse order on the way out so overlapping rules nest rather than interleave:
        // rule A's post lands after rule B's when A's pre landed before B's.
        for (int i = matched.Count - 1; i >= 0; i--)
            AppendCommands(output, matched[i].PostCommands, line);
    }

    private static bool AppliesTo(GcodeEventHook hook, GcodeEventScope scope) =>
        scope == GcodeEventScope.Job ? hook.ApplyToJob : hook.ApplyToManual;

    private static void AppendCommands(List<string> output, string? commands, string triggerLine)
    {
        if (string.IsNullOrWhiteSpace(commands)) return;

        foreach (var part in commands.Split(CommandSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            var command = SubstitutePlaceholders(part.Trim(), triggerLine);
            if (command.Length > 0)
                output.Add(command);
        }
    }

    /// <summary>
    /// Replaces <c>{LINE}</c> with the triggering line and <c>{T}</c>-style tokens with
    /// that word's value from the triggering line (empty when the word isn't present),
    /// so a post-command can reference e.g. the tool number of the M6 that fired it.
    /// </summary>
    private static string SubstitutePlaceholders(string command, string triggerLine)
    {
        if (command.IndexOf('{') < 0) return command;

        return PlaceholderRegex.Replace(command, match =>
        {
            var token = match.Groups[1].Value;
            if (token.Equals("LINE", StringComparison.OrdinalIgnoreCase))
                return triggerLine.Trim();
            if (token.Length != 1)
                return match.Value;

            var word = WordRegex.Matches(StripComments(triggerLine))
                .FirstOrDefault(m => char.ToUpperInvariant(m.Groups[1].Value[0]) ==
                                     char.ToUpperInvariant(token[0]));
            return word?.Groups[2].Value ?? string.Empty;
        });
    }

    /// <summary>
    /// Uppercases and drops comments and whitespace so trigger matching is not thrown
    /// off by formatting differences between posts.
    /// </summary>
    private static string Normalize(string line) =>
        StripComments(line).Replace(" ", "").Replace("\t", "").ToUpperInvariant();

    private static string StripComments(string line)
    {
        var text = Regex.Replace(line, @"\(.*?\)", "");
        int semicolon = text.IndexOf(';');
        return semicolon >= 0 ? text[..semicolon] : text;
    }

    private static bool MatchesAnyTrigger(string normalizedLine, string trigger)
    {
        foreach (var part in trigger.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var token = Normalize(part);
            if (token.Length > 0 && TriggerMatches(normalizedLine, token))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Matches one already-normalized trigger token against an already-normalized line:
    /// <list type="bullet">
    /// <item>A <c>$</c> command matches as a prefix, so <c>$H</c> also catches <c>$HX</c>.</item>
    /// <item>A single G-code word matches by numeric value, so <c>M6</c> catches
    /// <c>T3M6</c> and <c>M06</c> but not <c>M61</c>, and <c>G28</c> does not catch <c>G28.1</c>.</item>
    /// <item>Anything else (a multi-word token like <c>G65P231</c>) matches as a substring.</item>
    /// </list>
    /// </summary>
    private static bool TriggerMatches(string normalizedLine, string token)
    {
        if (token[0] == '$')
            return normalizedLine.StartsWith(token, StringComparison.Ordinal);

        var tokenWords = WordRegex.Matches(token);
        if (tokenWords.Count == 1 && tokenWords[0].Length == token.Length)
        {
            var letter = token[0];
            if (!TryParseValue(tokenWords[0].Groups[2].Value, out var wanted))
                return false;

            foreach (Match word in WordRegex.Matches(normalizedLine))
            {
                if (word.Groups[1].Value[0] != letter) continue;
                if (TryParseValue(word.Groups[2].Value, out var value) &&
                    Math.Abs(value - wanted) < 0.0001)
                    return true;
            }

            return false;
        }

        return normalizedLine.Contains(token, StringComparison.Ordinal);
    }

    private static bool TryParseValue(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
