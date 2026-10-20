using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Kumo.App.Mascot;
using Kumo.Core.Updates;

namespace Kumo.App.Updates;

/// <summary>The one small window for everything update-related: checking, offering, downloading and handing over.</summary>
public partial class UpdateWindow : Window
{
    private readonly UpdateCoordinator _updates;
    private readonly Func<bool> _isBusy;
    private readonly Action _exitForInstall;
    private CancellationTokenSource? _download;

    internal UpdateWindow(UpdateCoordinator updates, Func<bool> isBusy, Action exitForInstall)
    {
        InitializeComponent();
        _updates = updates;
        _isBusy = isBusy;
        _exitForInstall = exitForInstall;
        Closed += (_, _) => _download?.Cancel();
    }

    /// <summary>Runs a manual check and shows its outcome.</summary>
    internal async Task CheckAsync()
    {
        ShowState(MascotState.Processing, "Checking for updates…", string.Empty);
        var result = await _updates.Service.CheckAsync(manual: true, CancellationToken.None);
        if (result.Status == UpdateStatus.Available && result.Release is { } release)
            ShowAvailable(release);
        else if (result.Status == UpdateStatus.Failed)
            ShowState(MascotState.Error, "Couldn't check for updates", result.Message, Button("Try again", async () => await CheckAsync()), Button("Close", Close, primary: true));
        else
            ShowState(MascotState.Success, result.Message, $"You're using Kumo {_updates.Environment.CurrentVersion.ToShortString()}.", Button("Close", Close, primary: true));
    }

    internal void ShowAvailable(UpdateRelease release)
    {
        string version = release.Version.ToShortString();
        var actions = new List<Button>();
        if (_updates.Service.Capability.InstallsUpdates())
            actions.Add(Button("Update", async () => await DownloadAsync(release), primary: true));
        else
            actions.Add(Button("Download", () => Open(release.PageUrl), primary: true));
        actions.Insert(0, Button("What's new", () => Open(release.PageUrl)));
        actions.Insert(0, Button("Later", () =>
        {
            _updates.Service.RemindLater(release.Version);
            Close();
        }));

        ShowState(MascotState.Hovering, $"Kumo {version} is available",
            $"You're currently using {_updates.Environment.CurrentVersion.ToShortString()}." + (release.Version.IsPrerelease ? " This is a beta release." : string.Empty),
            [.. actions]);
        Notes.Content = ReleaseNotesView.Build(release.Notes);
        NotesCard.Visibility = Visibility.Visible;
    }

    private async Task DownloadAsync(UpdateRelease release)
    {
        _download = new CancellationTokenSource();
        NotesCard.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;
        Progress.Value = 0;
        ProgressText.Text = string.Empty;
        ShowState(MascotState.Processing, $"Downloading Kumo {release.Version.ToShortString()}", "The installer is checked before it runs.",
            Button("Cancel", () => _download.Cancel()));
        var progress = new Progress<DownloadProgress>(p =>
        {
            Progress.Value = p.Fraction;
            ProgressText.Text = string.Create(CultureInfo.CurrentCulture, $"{p.Received / 1048576.0:0.0} of {p.Total / 1048576.0:0.0} MB");
        });

        try
        {
            string installer = await _updates.DownloadVerifiedInstallerAsync(release, progress, _download.Token);
            ProgressPanel.Visibility = Visibility.Collapsed;
            if (_isBusy())
                ShowReady(installer, release);
            else
                Install(installer, release);
        }
        catch (OperationCanceledException)
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
            ShowState(MascotState.Cancelled, "Download cancelled", "Nothing was installed.", Button("Close", Close, primary: true));
        }
        catch (UpdateException ex)
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
            ShowState(MascotState.Error, "The update wasn't installed", ex.Message,
                Button("What's new", () => Open(release.PageUrl)), Button("Close", Close, primary: true));
        }
        catch (System.IO.IOException)
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
            ShowState(MascotState.Error, "The update wasn't installed", "The installer couldn't be saved. Check the free disk space and try again.",
                Button("Close", Close, primary: true));
        }
    }

    private void ShowReady(string installer, UpdateRelease release)
    {
        ShowState(MascotState.Success, "Ready to install",
            $"Kumo {release.Version.ToShortString()} is verified. Kumo will restart by itself; your settings are kept.",
            Button("Later", Close),
            Button("Install now", () =>
            {
                if (_isBusy())
                {
                    Detail.Text = "Kumo is still working on files. Install once it has finished.";
                    return;
                }
                Install(installer, release);
            }, primary: true));
    }

    private void Install(string installer, UpdateRelease release)
    {
        ShowState(MascotState.Processing, $"Installing Kumo {release.Version.ToShortString()}", "Kumo will restart in a moment.");
        _updates.LaunchInstaller(installer);
        _exitForInstall();
    }

    private void ShowState(MascotState mascot, string headline, string detail, params Button[] actions)
    {
        Mascot.State = mascot;
        Headline.Text = headline;
        Detail.Text = detail;
        Detail.Visibility = detail.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        NotesCard.Visibility = Visibility.Collapsed;
        Actions.Children.Clear();
        foreach (var action in actions)
            Actions.Children.Add(action);
    }

    private Button Button(string label, Action action, bool primary = false)
    {
        var button = new Button { Content = label, MinWidth = 96, Margin = new Thickness(8, 0, 0, 0), IsDefault = primary };
        if (primary && TryFindResource("AccentButtonStyle") is Style accent)
            button.Style = accent;
        button.Click += (_, _) => action();
        return button;
    }

    private static void Open(Uri page)
    {
        if (page.Scheme == Uri.UriSchemeHttps && page.Host == "github.com")
            Process.Start(new ProcessStartInfo(page.AbsoluteUri) { UseShellExecute = true });
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
