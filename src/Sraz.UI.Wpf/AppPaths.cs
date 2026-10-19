using System.IO;
using Sraz.UI.Wpf.Localization;

namespace Sraz.UI.Wpf;

internal static class AppPaths
{
	private static string AppDataDirectory =>
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sraz");

	public static string LeaderboardFile => Path.Combine(AppDataDirectory, "leaderboard.json");

	public static string SettingsFile => Path.Combine(AppDataDirectory, "settings.json");

	public static string QuestionsFile
	{
		get
		{
			var dataDirectory = Path.Combine(AppContext.BaseDirectory, "data");
			var localized = Path.Combine(dataDirectory, $"questions.{Localizer.Instance.Language}.json");
			return File.Exists(localized)
				? localized
				: Path.Combine(dataDirectory, "questions.seed.json");
		}
	}
}
