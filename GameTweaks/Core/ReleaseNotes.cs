using System.Text.RegularExpressions;

namespace GameTweaks.Core;

/// <summary>One line of the "What's new" pop-up.</summary>
public sealed record ReleaseNoteLine(string Text, bool IsHeading, bool IsBullet);

/// <summary>Turns GitHub release notes (simple Markdown) into lines for the "What's new" pop-up.</summary>
public static partial class ReleaseNotes
{
    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)")] private static partial Regex Link();
    [GeneratedRegex(@"\*\*|__|`")] private static partial Regex Emphasis();

    /// <summary>Skips the "Download" section (not useful once installed) and blockquote warnings.</summary>
    public static List<ReleaseNoteLine> Parse(string markdown)
    {
        var lines = new List<ReleaseNoteLine>();
        var skipping = false;
        foreach (var raw in markdown.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('#'))
            {
                var heading = Clean(line.TrimStart('#'));
                skipping = heading.Equals("Download", StringComparison.OrdinalIgnoreCase);
                if (!skipping) lines.Add(new ReleaseNoteLine(heading, true, false));
                continue;
            }
            if (skipping || line.Length == 0 || line.StartsWith('>')) continue;

            if (line.StartsWith("- ") || line.StartsWith("* "))
                lines.Add(new ReleaseNoteLine(Clean(line[2..]), false, true));
            else if (line.StartsWith("**") && line.EndsWith("**"))
                lines.Add(new ReleaseNoteLine(Clean(line), true, false));
            else
                lines.Add(new ReleaseNoteLine(Clean(line), false, false));
        }
        return lines;
    }

    static string Clean(string text) => Emphasis().Replace(Link().Replace(text, "$1"), "").Trim();
}
