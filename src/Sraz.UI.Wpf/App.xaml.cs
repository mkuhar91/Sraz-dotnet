using System.Windows;
using Sraz.UI.Wpf.Localization;

namespace Sraz.UI.Wpf;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        Localizer.Instance.SetLanguage(AppSettings.Load().Language);
        base.OnStartup(e);
    }
}
