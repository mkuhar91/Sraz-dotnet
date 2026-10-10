using System.Windows;

namespace Sraz.UI.Wpf;

public partial class LeaderboardWindow : Window
{
    public LeaderboardWindow(object viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
