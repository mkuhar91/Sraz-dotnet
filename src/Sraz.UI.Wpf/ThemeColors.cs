using System.Windows.Media;
using Sraz.Domain.Models;
using Sraz.UI.Wpf.Localization;

namespace Sraz.UI.Wpf;

public static class ThemeColors
{
    private static readonly IReadOnlyDictionary<QuestionTheme, string> Colors = new Dictionary<QuestionTheme, string>
    {
        [QuestionTheme.Geography] = "#8BC34A",
        [QuestionTheme.History] = "#FFB74D",
        [QuestionTheme.Math] = "#64B5F6",
        [QuestionTheme.Science] = "#4DB6AC",
        [QuestionTheme.Sports] = "#F06292",
        [QuestionTheme.Art] = "#BA68C8",
        [QuestionTheme.Literature] = "#FFD54F",
        [QuestionTheme.Entertainment] = "#90A4AE"
    };

    public static Brush Color(QuestionTheme theme) => new BrushConverter().ConvertFromString(Colors[theme]) as Brush ?? Brushes.White;

    public static string Label(QuestionTheme theme) => Localizer.Instance.Get($"Theme_{theme}");

    public static IReadOnlyDictionary<QuestionTheme, Brush> All { get; } =
        Colors.Keys.ToDictionary(theme => theme, Color);
}
