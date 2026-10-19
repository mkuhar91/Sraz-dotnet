using System.Windows;
using Sraz.UI.Wpf.Localization;

namespace Sraz.UI.Wpf;

public partial class RoundSummaryWindow : Window
{
	public RoundSummaryWindow(object viewModel)
	{
		InitializeComponent();
		DataContext = viewModel;
		Title = Localizer.Instance.Get("WindowSummary");
	}

	private void Close_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}
}
