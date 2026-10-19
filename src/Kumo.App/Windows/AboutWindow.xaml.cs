using System.Windows;

namespace Kumo.App.Windows;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Version {Updates.UpdateCoordinator.DisplayVersion()}";
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
