using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Kumo.App.Mascot;
using Kumo.Core.Updates;

namespace Kumo.App.Updates;

/// <summary>
/// The window for everything update-related: checking, offering, downloading and handing over. When a release is
/// offered it grows into a resizable What's new window with the full changelog and its screenshots.
/// </summary>
public partial class UpdateWindow : Window
{
    private readonly UpdateCoordinator _updates;
    private readonly Func<bool> _isBusy;
    private CancellationTokenSource? _download;

    internal UpdateWindow(UpdateCoordinator updates, Func<bool> isBusy)
    {
        InitializeComponent();
        _updates = updates;
        _isBusy = isBusy;
        Closed += (_, _) => _download?.Cancel();
    }

    /// <summary>Runs a manual check and shows its outcome.</summary>
    internal async Task CheckAsync()
    {
        NotesCard.Visibility = Visibility.Collapsed;
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
        actions.Insert(0, Button("View on GitHub", () => Open(release.PageUrl)));
        actions.Insert(0, Button("Later", () =>
        {
            _updates.Service.RemindLater(release.Version);
            Close();
        }));

        string published = release.PublishedAt is { } date
            ? string.Create(CultureInfo.GetCultureInfo("en-US"), $"Published {date.LocalDateTime:MMMM d, yyyy} · ")
            : string.Empty;
        ShowState(MascotState.Hovering, $"What's new in Kumo {version}",
            published + $"You're using {_updates.Environment.CurrentVersion.ToShortString()}." + (release.Version.IsPrerelease ? " This is a beta release." : string.Empty),
            [.. actions]);
        Notes.Content = ReleaseNotesView.Build(release.Notes);
        NotesCard.Visibility = Visibility.Visible;
        ExpandForNotes();
    }

    private async Task DownloadAsync(UpdateRelease release)
    {
        _download = new CancellationTokenSource();
        ShowProgress("Starting the download…");
        ShowState(MascotState.Processing, $"Downloading Kumo {release.Version.ToShortString()}", "The installer is checked before it runs.",
            Button("Cancel", () => _download.Cancel()));
        var progress = new Progress<DownloadProgress>(p =>
        {
            Progress.IsIndeterminate = false;
            Progress.Value = p.Fraction;
            ProgressText.Text = string.Create(CultureInfo.GetCultureInfo("en-US"),
                $"{p.Fraction:0%} · {p.Received / 1048576.0:0.0} of {p.Total / 1048576.0:0.0} MB");
        });

        try
        {
            string installer = await _updates.DownloadVerifiedInstallerAsync(release, progress, _download.Token);
            if (_isBusy())
                ShowReady(installer, release);
            else
                Install(installer, release);
        }
        catch (OperationCanceledException)
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
            ShowState(MascotState.Cancelled, "Download cancelled", "Nothing was installed.",
                Button("Try again", async () => await DownloadAsync(release)), Button("Close", Close, primary: true));
        }
        catch (UpdateException ex)
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
            ShowState(MascotState.Error, "The update wasn't installed", ex.Message,
                Button("Try again", async () => await DownloadAsync(release)), Button("Close", Close, primary: true));
        }
        catch (System.IO.IOException)
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
            ShowState(MascotState.Error, "The update wasn't installed", "The installer couldn't be saved. Check the free disk space and try again.",
                Button("Try again", async () => await DownloadAsync(release)), Button("Close", Close, primary: true));
        }
    }

    private void ShowReady(string installer, UpdateRelease release)
    {
        ProgressPanel.Visibility = Visibility.Collapsed;
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

    /// <summary>
    /// Starts the installer and stays open showing progress: the installer itself asks Kumo to exit before it replaces
    /// the files, then starts the new version. If it ends while Kumo is still running, it did not install anything.
    /// </summary>
    private async void Install(string installer, UpdateRelease release)
    {
        ShowProgress("Installing…");
        ShowState(MascotState.Converting, $"Installing Kumo {release.Version.ToShortString()}", "Kumo will close and open again by itself in a moment.");
        int exitCode;
        try
        {
            using var process = _updates.LaunchInstaller(installer);
            await process.WaitForExitAsync();
            exitCode = process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            exitCode = -1;
        }
        ProgressPanel.Visibility = Visibility.Collapsed;
        ShowState(MascotState.Error, "The update wasn't installed",
            exitCode == -1 ? "The installer couldn't be started." : $"The installer stopped before finishing (code {exitCode}). Kumo wasn't changed.",
            Button("Try again", () => Install(installer, release)), Button("Close", Close, primary: true));
    }

    private void ShowProgress(string text)
    {
        ProgressPanel.Visibility = Visibility.Visible;
        Progress.IsIndeterminate = true;
        Progress.Value = 0;
        ProgressText.Text = text;
    }

    private void ShowState(MascotState mascot, string headline, string detail, params Button[] actions)
    {
        Mascot.State = mascot;
        Headline.Text = headline;
        Detail.Text = detail;
        Detail.Visibility = detail.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
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

    /// <summary>Turns the compact status window into a resizable reading window sized to the screen it is on.</summary>
    private void ExpandForNotes()
    {
        if (SizeToContent == SizeToContent.Manual)
            return;
        var work = SystemParameters.WorkArea;
        SizeToContent = SizeToContent.Manual;
        ResizeMode = ResizeMode.CanResize;
        Chrome.ResizeBorderThickness = new Thickness(6);
        MinWidth = 520;
        MinHeight = 420;
        Width = Math.Min(780, work.Width - 80);
        Height = Math.Min(820, work.Height - 80);
        Left = work.Left + (work.Width - Width) / 2;
        Top = work.Top + (work.Height - Height) / 2;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
