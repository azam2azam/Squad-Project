using System.Globalization;
using Domain.Enums;

namespace Application.Telegram;

/// <summary>
/// Turns what somebody typed on a phone into a board update.
///
/// Deliberately forgiving. The alternative — a strict format people have to get exactly
/// right — is the reason status updates stop arriving: somebody mistypes a colon at 7pm,
/// gets an error, and goes back to not updating the board. So this accepts "status: at
/// risk", "Status = At Risk", "status  atrisk", 65 or 65%, and reports what it did not
/// understand rather than rejecting the whole message.
///
/// It is pure: no database, no Telegram, no clock. Everything it decides can be tested by
/// passing a string in and reading the result out, which is what makes a parser this
/// lenient safe to keep lenient.
/// </summary>
public static class TelegramMessageParser
{
    /// <summary>
    /// The template people are given. Kept next to the parser so the two cannot drift —
    /// the settings screen, the bot's own /help and the manual all print this.
    /// </summary>
    public const string Template =
        """
        #update <board code>
        status: On Track
        progress: 65
        sprint: Sprint 12
        blocker: waiting on MOH sign-off
        risk: Medium
        note: UAT starts Monday
        """;

    private static readonly char[] Separators = [':', '=', '-', '–', '—'];

    /// <summary>Reads one message. Never throws: a garbled message is a result, not an error.</summary>
    public static ParsedTelegramMessage Parse(string? text)
    {
        var raw = (text ?? string.Empty).Trim();

        if (raw.Length == 0)
        {
            return ParsedTelegramMessage.Empty;
        }

        var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

        // A slash command occupies the whole message; "/help please" is still /help.
        if (lines[0].StartsWith('/'))
        {
            var word = lines[0].Split([' ', '@'], StringSplitOptions.RemoveEmptyEntries)[0];
            var argument = lines[0][word.Length..].Trim().TrimStart('@').Trim();

            return ParsedTelegramMessage.ForCommand(word.ToLowerInvariant(), argument);
        }

        var result = new ParsedTelegramMessage();

        foreach (var line in lines)
        {
            ReadLine(line, result);
        }

        return result;
    }

    private static void ReadLine(string line, ParsedTelegramMessage result)
    {
        // "#update DIS" or "board: DIS" or a bare "DIS" on the first line all name a board.
        if (line.StartsWith('#'))
        {
            var afterHash = line[1..].Trim();
            var parts = afterHash.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

            // "#update DIS" — the tag is a keyword, the rest is the board.
            if (parts.Length == 2 && IsUpdateTag(parts[0]))
            {
                result.BoardReference ??= parts[1].Trim();
                return;
            }

            // "#DIS" — the tag itself is the board.
            if (parts.Length >= 1 && !IsUpdateTag(parts[0]))
            {
                result.BoardReference ??= afterHash;
                return;
            }

            // A bare "#update" with the board on a later line is fine; nothing to record.
            return;
        }

        var split = SplitKeyValue(line);

        if (split is null)
        {
            // A first line with no key is the board name: people write "Discharge Revamp"
            // and then the fields underneath.
            if (result.BoardReference is null && result.Fields.Count == 0)
            {
                result.BoardReference = line;
                return;
            }

            result.Unrecognised.Add(line);
            return;
        }

        var (key, value) = split.Value;

        switch (Normalise(key))
        {
            case "board" or "project" or "code":
                result.BoardReference ??= value;
                break;

            case "status" or "state":
                if (ReadStatus(value) is { } status) result.Status = status;
                else result.Unrecognised.Add(line);
                break;

            case "progress" or "complete" or "completion" or "percent" or "done":
                if (ReadPercent(value) is { } percent) result.ProgressPercent = percent;
                else result.Unrecognised.Add(line);
                break;

            case "sprint" or "iteration":
                result.Sprint = value;
                break;

            case "blocker" or "blockers" or "blocked" or "blockedby" or "issue":
                result.BlockerNote = value;
                break;

            case "risk" or "risklevel":
                if (ReadRisk(value) is { } risk) result.RiskLevel = risk;
                else result.Unrecognised.Add(line);
                break;

            case "risknote" or "riskdetail" or "riskreason" or "why":
                result.RiskNote = value;
                break;

            case "note" or "notes" or "comment" or "update" or "summary":
                result.Note = value;
                break;

            default:
                result.Unrecognised.Add(line);
                break;
        }
    }

    private static bool IsUpdateTag(string word) =>
        Normalise(word) is "update" or "status" or "board" or "boardupdate" or "statusupdate";

    /// <summary>
    /// Splits "status: On Track" into its two halves. Hyphens only count as a separator
    /// when the left side is a single word, so "blocker - waiting on the vendor" splits but
    /// "Discharge Revamp - Gaps for MOH" does not lose its tail.
    /// </summary>
    private static (string Key, string Value)? SplitKeyValue(string line)
    {
        var index = line.IndexOfAny(Separators);
        if (index <= 0 || index == line.Length - 1) return null;

        var key = line[..index].Trim();
        var value = line[(index + 1)..].Trim();

        if (key.Length == 0 || value.Length == 0) return null;
        if (key.Contains(' ') && line[index] is '-' or '–' or '—') return null;

        return (key, value);
    }

    private static string Normalise(string value) =>
        new(value.Where(char.IsAsciiLetter).Select(char.ToLowerInvariant).ToArray());

    /// <summary>
    /// Board statuses as people actually write them. "Blocked" and "at risk" are the two
    /// that matter most and the two most often abbreviated.
    /// </summary>
    private static BoardStatus? ReadStatus(string value) => Normalise(value) switch
    {
        "ontrack" or "track" or "green" or "ok" or "good" or "fine" or "normal"
            => BoardStatus.OnTrack,
        "atrisk" or "risk" or "risky" or "amber" or "yellow" or "warning" or "slipping"
            => BoardStatus.AtRisk,
        "blocked" or "block" or "stuck" or "red" or "halted" or "onhold" or "hold"
            => BoardStatus.Blocked,
        "inreview" or "review" or "reviewing" or "uat" or "testing" or "qa"
            => BoardStatus.InReview,
        "delivered" or "deliver" or "done" or "complete" or "completed" or "live" or "released"
            => BoardStatus.Delivered,
        _ => null
    };

    private static RiskLevel? ReadRisk(string value) => Normalise(value) switch
    {
        "none" or "norisk" or "nil" or "no" or "clear" => RiskLevel.None,
        "low" or "minor" or "small" => RiskLevel.Low,
        "medium" or "med" or "moderate" or "mid" => RiskLevel.Medium,
        "high" or "major" or "serious" or "big" => RiskLevel.High,
        "critical" or "crit" or "severe" or "blockerrisk" => RiskLevel.Critical,
        _ => null
    };

    /// <summary>Accepts 65, 65%, "65 percent", and 0.65 as a fraction of one.</summary>
    private static int? ReadPercent(string value)
    {
        var digits = new string(value.Where(c => char.IsAsciiDigit(c) || c is '.' or ',').ToArray())
            .Replace(',', '.');

        if (digits.Length == 0) return null;

        if (!double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return null;
        }

        // "0.65" means 65%, but a literal "1" means one per cent, not everything done —
        // somebody reporting the first day of a project would otherwise mark it complete.
        if (number > 0 && number < 1) number *= 100;

        var rounded = (int)Math.Round(number, MidpointRounding.AwayFromZero);

        return rounded is >= 0 and <= 100 ? rounded : null;
    }
}

/// <summary>What one message asked for. Every field is optional; absent means "leave it".</summary>
public sealed class ParsedTelegramMessage
{
    public static ParsedTelegramMessage Empty { get; } = new();

    /// <summary>A code, a title, or part of one — resolved against the boards later.</summary>
    public string? BoardReference { get; set; }

    public BoardStatus? Status { get; set; }
    public int? ProgressPercent { get; set; }
    public string? Sprint { get; set; }
    public string? BlockerNote { get; set; }
    public RiskLevel? RiskLevel { get; set; }
    public string? RiskNote { get; set; }

    /// <summary>Free commentary. Becomes the risk note only when a risk is being raised.</summary>
    public string? Note { get; set; }

    /// <summary>Lines that looked like they meant something but did not parse.</summary>
    public List<string> Unrecognised { get; } = [];

    public string? Command { get; private set; }
    public string CommandArgument { get; private set; } = string.Empty;

    public bool IsCommand => Command is not null;

    /// <summary>True when at least one field would change something on a board.</summary>
    public bool HasFields =>
        Status is not null
        || ProgressPercent is not null
        || Sprint is not null
        || BlockerNote is not null
        || RiskLevel is not null
        || RiskNote is not null
        || Note is not null;

    /// <summary>The fields that were understood, for the reply and the log.</summary>
    public IReadOnlyList<string> Fields
    {
        get
        {
            var fields = new List<string>();
            if (Status is not null) fields.Add("status");
            if (ProgressPercent is not null) fields.Add("progress");
            if (Sprint is not null) fields.Add("sprint");
            if (BlockerNote is not null) fields.Add("blocker");
            if (RiskLevel is not null) fields.Add("risk");
            if (RiskNote is not null || Note is not null) fields.Add("note");
            return fields;
        }
    }

    internal static ParsedTelegramMessage ForCommand(string command, string argument) =>
        new() { Command = command, CommandArgument = argument };
}
