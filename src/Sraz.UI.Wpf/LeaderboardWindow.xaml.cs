using System.Windows;
using Sraz.UI.Wpf.Localization;

namespace Sraz.UI.Wpf;

public partial class LeaderboardWindow : Window
{
	public LeaderboardWindow(object viewModel)
	{
		InitializeComponent();
		DataContext = viewModel;
		Title = Localizer.Instance.Get("WindowLeaderboard");
	}

	private void Close_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}
}
