using GameTweaks.Tweaks;

namespace GameTweaks.Core;

public sealed record SpudScoreResult(int Score, string Rank, string Emoji, string NextStep, Tweak? NextTweak);

/// <summary>
/// A 0–100 rating of how game-ready this PC is: the tweaks that are on (weighted by impact),
/// the problems found by the health check, and whether Game Boost is running.
/// </summary>
public static class SpudScore
{
    static readonly (int Min, string Rank, string Emoji)[] Ranks =
    [
        (95, "Golden Fries", "🏆"), (85, "Crispy Fries", "🍟"), (70, "Hash Browns", "🔥"),
        (50, "Mashed Potato", "🥣"), (30, "Baked Potato", "🥔"), (0, "Raw Potato", "🌱"),
    ];

    /// <summary>Bigger FPS / latency impact counts for more, and is suggested first.</summary>
    static readonly Dictionary<string, int> Weights = new()
    {
        ["power-plan"] = 4, ["game-mode"] = 3, ["game-dvr"] = 4, ["hags"] = 3, ["game-priority"] = 3, ["windowed-optimizations"] = 3,
        ["mouse-accel"] = 3, ["power-throttling"] = 2, ["nvidia-profiles"] = 3, ["store-auto-updates"] = 2, ["nagle"] = 2,
        ["net-throttle"] = 2, ["nic-power"] = 2, ["foreground-boost"] = 2, ["fortnite-gpu"] = 2, ["core-parking"] = 3,
        ["timer-resolution"] = 2, ["usb-suspend"] = 2, ["fullscreen-optimizations-global"] = 2,
    };

    /// <summary>Tweaks that are a trade-off (security, personal choice) never count against the score.</summary>
    static bool Counts(Tweak t) => t.ConfirmMessage == null && t.Id != "dns" && t.Category != "Privacy";

    public static SpudScoreResult Compute(IEnumerable<Tweak> tweaks, IReadOnlyList<HealthItem> health, bool boostOn)
    {
        var counted = tweaks.Where(t => t.IsChecked && Counts(t)).ToList();
        var total = counted.Sum(Weight);
        var on = counted.Where(t => t.IsOn).Sum(Weight);
        var tweakPoints = total == 0 ? 60 : 60.0 * on / total;
        var healthPoints = Math.Max(0, 25 - 7 * health.Count);
        var score = (int)Math.Round(Math.Clamp(tweakPoints + healthPoints + (boostOn ? 15 : 0), 0, 100));
        var (_, rank, emoji) = Ranks.First(r => score >= r.Min);

        // Suggest the single most useful next step.
        var next = counted.Where(t => !t.IsOn).OrderByDescending(Weight).FirstOrDefault();
        string step;
        Tweak? fix = null;
        if (next != null)
        {
            step = $"Turn on \"{next.Name}\"" + (next.RequiresAdmin && !Shell.IsAdmin ? " (needs admin)" : "");
            fix = next;
        }
        else if (!boostOn)
            step = "Switch on Game Boost";
        else if (health.Count > 0)
            step = health[0].Title + " (see the yellow card below)";
        else
            step = "Nothing left. This PC is fully boosted!";
        return new SpudScoreResult(score, rank, emoji, step, fix);
    }

    static int Weight(Tweak t) => Weights.GetValueOrDefault(t.Id, 1);
}
