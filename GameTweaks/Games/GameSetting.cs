using System.Globalization;
using GameTweaks.Core;

namespace GameTweaks.Games;

public enum SettingKind { Choice, Toggle, Slider }

/// <summary>A setting SpudBoost knows how to show for a game. Only shown if the key exists in the game's file.</summary>
public sealed class GameSettingDef
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public string? Hint { get; init; }
    public SettingKind Kind { get; init; } = SettingKind.Slider;
    /// <summary>For Choice: (label, raw value) pairs.</summary>
    public (string Label, string Value)[] Choices { get; init; } = [];
    public double Min { get; init; }
    public double Max { get; init; } = 10;
}

/// <summary>One setting as found in the file, editable from the UI.</summary>
public sealed class GameSetting : ObservableObject
{
    public GameSettingDef Def { get; }
    public string Label => Def.Label;
    public string? Hint => Def.Hint;
    public SettingKind Kind => Def.Kind;
    public double Min => Def.Min;
    public double Max => Def.Max;

    /// <summary>Was the value written as true/false (vs 1/0)? Toggles keep the file's own style.</summary>
    readonly bool _boolStyle;

    string _raw;
    public string Raw
    {
        get => _raw;
        private set
        {
            if (!SetField(ref _raw, value)) return;
            OnPropertyChanged(nameof(IsOn));
            OnPropertyChanged(nameof(Number));
            OnPropertyChanged(nameof(SelectedIndex));
            OnPropertyChanged(nameof(DisplayValue));
        }
    }

    public List<string> ChoiceLabels { get; }

    public GameSetting(GameSettingDef def, string raw)
    {
        Def = def;
        _raw = raw;
        _boolStyle = raw.Equals("true", StringComparison.OrdinalIgnoreCase) || raw.Equals("false", StringComparison.OrdinalIgnoreCase);
        ChoiceLabels = def.Choices.Select(c => c.Label).ToList();
        // A value the game set that isn't one of our choices stays selectable.
        if (def.Kind == SettingKind.Choice && def.Choices.All(c => c.Value != raw)) ChoiceLabels.Add($"Current ({raw})");
    }

    public bool IsOn
    {
        get => _raw.Equals("true", StringComparison.OrdinalIgnoreCase) || (double.TryParse(_raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && n > 0);
        set => Raw = _boolStyle ? (value ? "true" : "false") : (value ? "1" : "0");
    }

    public double Number
    {
        get => double.TryParse(_raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : Def.Min;
        set => Raw = Math.Round(Math.Clamp(value, Def.Min, Def.Max)).ToString(CultureInfo.InvariantCulture);
    }

    public int SelectedIndex
    {
        get
        {
            var i = Array.FindIndex(Def.Choices, c => c.Value == _raw);
            return i >= 0 ? i : ChoiceLabels.Count - 1;
        }
        set
        {
            if (value >= 0 && value < Def.Choices.Length) Raw = Def.Choices[value].Value;
        }
    }

    public string DisplayValue => Kind == SettingKind.Slider ? Number.ToString("0", CultureInfo.CurrentCulture) : _raw;

    /// <summary>
    /// Sets a preset or profile value: "on"/"off" for toggles, a number for sliders (clamped to range), one of the listed
    /// values for choices. Anything else is ignored, so a config file from someone else can't write arbitrary text into a game's file.
    /// </summary>
    public void Set(string value)
    {
        switch (Kind)
        {
            case SettingKind.Toggle:
                IsOn = value is "on" or "true" or "1";
                break;
            case SettingKind.Slider when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n):
                Number = n;
                break;
            case SettingKind.Choice when Def.Choices.Any(c => c.Value == value):
                Raw = value;
                break;
        }
    }
}
