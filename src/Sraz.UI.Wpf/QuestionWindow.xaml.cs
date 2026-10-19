using System.Windows;
using System.Windows.Input;
using Sraz.UI.Wpf.Localization;
using Sraz.UI.Wpf.ViewModels;

namespace Sraz.UI.Wpf;

public partial class QuestionWindow : Window
{
	private readonly QuestionViewModel _vm;

	public QuestionWindow(QuestionViewModel vm)
	{
		InitializeComponent();
		_vm = vm;
		DataContext = vm;
		Title = Localizer.Instance.Get("WindowQuestion");
		Loaded += (_, _) => vm.StartTimer();
		Closed += (_, _) => vm.Dispose();
		vm.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName == nameof(QuestionViewModel.HasQuestion) && !vm.HasQuestion)
			{
				DialogResult = true;
			}
		};
	}

	private async void Answer_Click(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: OptionItem option })
		{
			await _vm.SelectAnswerAsync(option);
		}
	}

	private async void Submit_Click(object sender, RoutedEventArgs e) => await _vm.SubmitTextAsync();

	private async void TextAnswer_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Enter)
		{
			await _vm.SubmitTextAsync();
		}
	}

	private void FiftyFifty_Click(object sender, RoutedEventArgs e) => _vm.UseFiftyFifty();

	private void ExtraTime_Click(object sender, RoutedEventArgs e) => _vm.UseExtraTime();

	private async void Skip_Click(object sender, RoutedEventArgs e) => await _vm.SkipAsync();
}
