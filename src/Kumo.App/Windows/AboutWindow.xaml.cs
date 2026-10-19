using System.Reflection;
using System.Windows;

namespace Kumo.App.Windows;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is null ? string.Empty : $"Version {version.Major}.{version.Minor}.{version.Build}";
    }
}
