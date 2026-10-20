using System.Windows;
using System.Windows.Controls;

namespace GameTweaks.Games;

/// <summary>Picks the slider, switch or dropdown row for each game setting.</summary>
public sealed class GameSettingTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Slider { get; set; }
    public DataTemplate? Toggle { get; set; }
    public DataTemplate? Choice { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        (item as GameSetting)?.Kind switch
        {
            SettingKind.Toggle => Toggle,
            SettingKind.Choice => Choice,
            _ => Slider,
        };
}
