namespace Sraz.UI.Wpf.ViewModels;

public sealed class ChoiceItem : BindableBase
{
    private string _label;

    public ChoiceItem(string value, string label)
    {
        Value = value;
        _label = label;
    }

    public string Value { get; }

    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }
}
