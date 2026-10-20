using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Kumo.Core.Settings;
using Kumo.Core.Updates;
using Microsoft.Win32;

namespace Kumo.App.Windows;

internal sealed record Choice<T>(string Label, T Value)
{
    public override string ToString() => Label;
}

internal sealed record SettingsResult(KumoSettings Settings, bool StartWithWindows);

/// <summary>What the Updates page needs from the application; the page holds no update logic of its own.</summary>
internal sealed record UpdatePanel(
    string Version,
    string InstallationSource,
    UpdateService Service,
    Func<UpdateChannel, Task> ChangeChannel,
    Action<UpdateRelease> ShowRelease);

public partial class SettingsWindow : Window
{
    private readonly KumoSettings _settings;
    private readonly string _logsDirectory;
    private string _hotkey;

    internal SettingsWindow(KumoSettings settings, bool startWithWindows, string logsDirectory, UpdatePanel updates)
    {
        InitializeComponent();
        _settings = settings.Clone();
        _logsDirectory = logsDirectory;
        _hotkey = _settings.Hotkey.Length > 0 ? _settings.Hotkey : KumoSettings.DefaultHotkey;

        StartWithWindows.IsChecked = startWithWindows;
        Fill(LanguageBox, [new Choice<string>("English", "en")], _settings.Language);
        Fill(ThemeBox, [new("Match Windows", ThemePreference.System), new("Light", ThemePreference.Light), new Choice<ThemePreference>("Dark", ThemePreference.Dark)], _settings.Theme);
        Fill(NotificationsBox, [new("All results", NotificationLevel.All), new("Only problems", NotificationLevel.ErrorsOnly), new Choice<NotificationLevel>("Off", NotificationLevel.None)], _settings.Notifications);
        Fill(ActivationBox, [new("Tap Shift", GestureActivation.ShiftTap), new("Hold Shift", GestureActivation.ShiftHold), new Choice<GestureActivation>("Off", GestureActivation.Off)], _settings.Activation);
        Fill(MenuSizeBox, [new("Compact", MenuSize.Compact), new("Standard", MenuSize.Standard), new Choice<MenuSize>("Large", MenuSize.Large)], _settings.MenuSize);
        Fill(MotionBox, [new("Match Windows", MotionPreference.System), new("Reduced", MotionPreference.Reduced), new Choice<MotionPreference>("Full", MotionPreference.Full)], _settings.Motion);
        Fill(CollisionBox, [new("Add a number to the new name", CollisionBehavior.AddNumber), new Choice<CollisionBehavior>("Replace the existing file", CollisionBehavior.ReplaceOutput)], _settings.Collisions);
        Fill(ConcurrencyBox, Enumerable.Range(KumoSettings.MinConcurrency, KumoSettings.MaxConcurrency).Select(n => new Choice<int>(n.ToString(System.Globalization.CultureInfo.CurrentCulture), n)).ToList(), _settings.MaxConcurrentOperations);

        ActivationBox.SelectionChanged += (_, _) => UpdateActivationHint();
        UpdateActivationHint();

        HotkeyBox.Text = _hotkey;
        HotkeyEnabled.IsChecked = _settings.Hotkey.Length > 0;
        OutputBeside.IsChecked = _settings.OutputDirectory.Length == 0;
        OutputFolder.IsChecked = _settings.OutputDirectory.Length > 0;
        OutputPath.Text = _settings.OutputDirectory;
        PreserveOriginals.IsChecked = _settings.PreserveOriginals;
        DiagnosticLogging.IsChecked = _settings.DiagnosticLogging;

        SidebarVersion.Text = $"Version {Updates.UpdateCoordinator.DisplayVersion()}";
        Navigation.SelectedIndex = 0;

        _updates = updates;
        Fill(UpdateChannelBox,
            [new("Release (Recommended)", UpdateChannel.Release), new("Beta (Early access)", UpdateChannel.Beta), new Choice<UpdateChannel>("Development (Experimental)", UpdateChannel.Development)],
            _settings.UpdateChannel);
        AutomaticUpdates.IsChecked = _settings.AutomaticUpdateChecks;
        CurrentVersionText.Text = $"Kumo {updates.Version}";
        InstallationText.Text = $"Installation source: {updates.InstallationSource}";

        bool usable = updates.Service.Capability is UpdateCapability.NotifyOnly or UpdateCapability.InstallVerified;
        UpdateChannelBox.IsEnabled = usable;
        AutomaticUpdates.IsEnabled = usable;
        ShowChannel(_settings.UpdateChannel);
        UpdateChannelBox.SelectionChanged += OnChannelSelected;

        if (!usable)
            ShowStatus(new UpdateCheckResult(UpdateStatus.Disabled, updates.Service.Capability == UpdateCapability.StoreManaged
                ? "Updates managed by Microsoft Store."
                : "Update checks aren't available in this build."));
        else
            ShowStatus(updates.Service.Status.Status == UpdateStatus.Idle && updates.Service.LastResult is { } last ? last : updates.Service.Status);
        updates.Service.StatusChanged += OnStatusChanged;
        Closed += (_, _) => updates.Service.StatusChanged -= OnStatusChanged;
    }

    private readonly UpdatePanel _updates;
    private UpdateRelease? _offered;

    private void OnStatusChanged(UpdateCheckResult status) => Dispatcher.BeginInvoke(() => ShowStatus(status));

    /// <summary>Opens a section by its tag, for example "Updates" when coming from an update notification.</summary>
    internal void ShowPage(string page)
    {
        Navigation.SelectedItem = Navigation.Items.OfType<ListBoxItem>().FirstOrDefault(i => (string)i.Tag == page) ?? Navigation.SelectedItem;
    }

    private void ShowStatus(UpdateCheckResult status)
    {
        _offered = status.Release;
        ShowWhatsNew(status.Status == UpdateStatus.Available ? status.Release : null);
        UpdateStatusDetail.Visibility = Visibility.Collapsed;
        UpdateActionButton.Visibility = Visibility.Visible;
        UpdateActionButton.IsEnabled = true;
        switch (status.Status)
        {
            case UpdateStatus.Disabled:
                UpdateStatusText.Text = status.Message;
                UpdateActionButton.Visibility = Visibility.Collapsed;
                break;
            case UpdateStatus.Checking:
                UpdateStatusText.Text = "Checking for updates…";
                UpdateActionButton.Content = "Checking…";
                UpdateActionButton.IsEnabled = false;
                break;
            case UpdateStatus.Available:
                UpdateStatusText.Text = status.Message;
                UpdateStatusDetail.Text = $"You're using {_updates.Version}.";
                UpdateStatusDetail.Visibility = Visibility.Visible;
                UpdateActionButton.Content = "View update";
                break;
            case UpdateStatus.UpToDate or UpdateStatus.Failed:
                UpdateStatusText.Text = status.Message;
                UpdateActionButton.Content = "Check for updates";
                break;
            default:
                UpdateStatusText.Text = "Kumo hasn't checked for updates yet.";
                UpdateActionButton.Content = "Check for updates";
                break;
        }
    }

    private void ShowWhatsNew(UpdateRelease? release)
    {
        WhatsNewCard.Visibility = release == null ? Visibility.Collapsed : Visibility.Visible;
        if (release == null)
            return;
        WhatsNewTitle.Text = $"What's new in Kumo {release.Version.ToShortString()}";
        WhatsNewDate.Text = (release.PublishedAt is { } published ? $"Published {published.LocalDateTime:D}" : "Published on GitHub")
            + (release.Version.IsPrerelease ? " · pre-release" : string.Empty);
        WhatsNewNotes.Content = Updates.ReleaseNotesView.Build(release.Notes);
    }

    private void OnGetUpdate(object sender, RoutedEventArgs e)
    {
        if (_offered is { } release)
            _updates.ShowRelease(release);
    }

    private void OnViewOnGitHub(object sender, RoutedEventArgs e)
    {
        if (_offered is { PageUrl: { Scheme: "https", Host: "github.com" } page })
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(page.AbsoluteUri) { UseShellExecute = true });
    }

    private void OnUpdateAction(object sender, RoutedEventArgs e)
    {
        if (_offered is { } release)
            _updates.ShowRelease(release);
        else
            _ = _updates.Service.CheckAsync(manual: true, CancellationToken.None);
    }

    private async void OnChannelSelected(object sender, SelectionChangedEventArgs e)
    {
        var channel = Selected<UpdateChannel>(UpdateChannelBox);
        _settings.UpdateChannel = channel;
        ShowChannel(channel);
        await _updates.ChangeChannel(channel);
    }

    private void ShowChannel(UpdateChannel channel)
    {
        ChannelDescription.Text = channel switch
        {
            UpdateChannel.Beta => "Try upcoming features before they're publicly released. Some issues may occur.",
            UpdateChannel.Development => "Receive experimental builds intended for developers and testers.",
            _ => "Receive stable updates recommended for everyone.",
        };
        DevelopmentWarning.Visibility = channel == UpdateChannel.Development ? Visibility.Visible : Visibility.Collapsed;
    }

    internal SettingsResult? Result { get; private set; }

    private static void Fill<T>(ComboBox box, IReadOnlyList<Choice<T>> choices, T selected)
    {
        box.ItemsSource = choices;
        box.SelectedItem = choices.FirstOrDefault(c => EqualityComparer<T>.Default.Equals(c.Value, selected)) ?? choices[0];
    }

    private static T Selected<T>(ComboBox box) => ((Choice<T>)box.SelectedItem).Value;

    private void UpdateActivationHint()
    {
        ActivationHint.Text = Selected<GestureActivation>(ActivationBox) switch
        {
            GestureActivation.ShiftTap => "Drag files from File Explorer or the desktop and tap Shift once. Holding Shift still moves files as usual.",
            GestureActivation.ShiftHold => "The menu opens as soon as Shift is held during a drag. This replaces Shift's usual \"move\" behaviour while the menu is open.",
            _ => "Use the keyboard shortcut or the tray icon to open Kumo.",
        };
    }

    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.Tab)
        {
            if (key == Key.Tab)
                e.Handled = false;
            return;
        }

        var modifiers = HotkeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            modifiers |= HotkeyModifiers.Control;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            modifiers |= HotkeyModifiers.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            modifiers |= HotkeyModifiers.Shift;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin))
            modifiers |= HotkeyModifiers.Windows;

        int virtualKey = KeyInterop.VirtualKeyFromKey(key);
        var candidate = new HotkeyGesture(modifiers, virtualKey).ToString();
        if (HotkeyGesture.TryParse(candidate, out var gesture))
        {
            _hotkey = gesture.ToString();
            HotkeyBox.Text = _hotkey;
            HotkeyEnabled.IsChecked = true;
        }
    }

    private void OnBrowseOutput(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose where Kumo saves results" };
        if (Directory.Exists(OutputPath.Text))
            dialog.InitialDirectory = OutputPath.Text;
        if (dialog.ShowDialog(this) == true)
        {
            OutputPath.Text = dialog.FolderName;
            OutputFolder.IsChecked = true;
        }
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_logsDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_logsDirectory}\"") { UseShellExecute = true });
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        string folder = OutputPath.Text.Trim();
        if (OutputFolder.IsChecked == true && (!Path.IsPathFullyQualified(folder) || !Directory.Exists(folder)))
        {
            MessageBox.Show(this, "Choose an existing folder for results, or save them next to the originals.", "Kumo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _settings.Language = Selected<string>(LanguageBox);
        _settings.Theme = Selected<ThemePreference>(ThemeBox);
        _settings.Notifications = Selected<NotificationLevel>(NotificationsBox);
        _settings.Activation = Selected<GestureActivation>(ActivationBox);
        _settings.MenuSize = Selected<MenuSize>(MenuSizeBox);
        _settings.Motion = Selected<MotionPreference>(MotionBox);
        _settings.Hotkey = HotkeyEnabled.IsChecked == true ? _hotkey : string.Empty;
        _settings.OutputDirectory = OutputFolder.IsChecked == true ? folder : string.Empty;
        _settings.PreserveOriginals = PreserveOriginals.IsChecked == true;
        _settings.Collisions = Selected<CollisionBehavior>(CollisionBox);
        _settings.MaxConcurrentOperations = Selected<int>(ConcurrencyBox);
        _settings.DiagnosticLogging = DiagnosticLogging.IsChecked == true;
        _settings.UpdateChannel = Selected<UpdateChannel>(UpdateChannelBox);
        _settings.AutomaticUpdateChecks = AutomaticUpdates.IsChecked == true;

        Result = new SettingsResult(_settings.Normalize(), StartWithWindows.IsChecked == true);
        Close();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnNavigate(object sender, SelectionChangedEventArgs e)
    {
        string page = (Navigation.SelectedItem as ListBoxItem)?.Tag as string ?? "General";
        GeneralPage.Visibility = page == "General" ? Visibility.Visible : Visibility.Collapsed;
        InteractionPage.Visibility = page == "Interaction" ? Visibility.Visible : Visibility.Collapsed;
        FilesPage.Visibility = page == "Files" ? Visibility.Visible : Visibility.Collapsed;
        AdvancedPage.Visibility = page == "Advanced" ? Visibility.Visible : Visibility.Collapsed;
        UpdatesPage.Visibility = page == "Updates" ? Visibility.Visible : Visibility.Collapsed;
    }
}
