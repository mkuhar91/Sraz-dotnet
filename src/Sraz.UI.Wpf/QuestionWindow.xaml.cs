using System.Windows;
using Sraz.UI.Wpf.ViewModels;

namespace Sraz.UI.Wpf;

public partial class QuestionWindow : Window
{
    public QuestionWindow(QuestionViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += (_, _) => vm.StartTimer();
        Closed += (_, _) => vm.Dispose();
    }

    private async void Answer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: string selectedOption } && DataContext is QuestionViewModel vm)
        {
            await vm.SelectAnswerAsync(selectedOption);
            if (!vm.HasQuestion)
            {
                DialogResult = true;
                Close();
            }
        }
    }
}
