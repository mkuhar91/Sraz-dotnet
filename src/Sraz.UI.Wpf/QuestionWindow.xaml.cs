using System.Windows;
using Sraz.UI.Wpf.ViewModels;

namespace Sraz.UI.Wpf;

public partial class QuestionWindow : Window
{
    public QuestionWindow(QuestionViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void Answer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: string selectedOption } && DataContext is QuestionViewModel vm)
        {
            vm.SelectAnswer(selectedOption);
            if (!vm.HasQuestion)
            {
                MessageBox.Show($"Round finished! Final score: {vm.Score}", "Sraz", MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
            }
        }
    }
}
