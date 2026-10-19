using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Kumo.App.Overlay;
using Kumo.App.Updates;
using Kumo.Core.Updates;
using Kumo.App.Windows;
using Kumo.Core.Diagnostics;
using Kumo.Core.Files;
using Kumo.Core.Operations;
using Kumo.Core.Settings;
using Kumo.Windows.Input;
using Kumo.Windows.Interop;
using Kumo.Operations;
using Kumo.Windows.Shell;
using Microsoft.Win32;

namespace Kumo.App;

internal sealed class KumoApplication : Application
{
    private enum TrayCommand
    {
        Open = 1,
        Settings,
        StartWithWindows,
        CheckForUpdates,
        About,
        Exit,
    }

    private static readonly TimeSpan FirstUpdateCheckDelay = TimeSpan.FromSeconds(30);

    private readonly SingleInstance _instance;
    private readonly string[] _startupArguments;
    private readonly Log _log = new(AppPaths.Logs);
    private readonly SettingsStore _store;
    private KumoSettings _settings;
    private MessageWindow? _messageWindow;
    private TrayIcon? _tray;
    private HotkeyRegistration? _hotkey;
    private GlobalDragMonitor? _monitor;
    private OverlayController? _menu;
    private SettingsWindow? _settingsWindow;
    private AboutWindow? _aboutWindow;
    private UpdateCoordinator? _updates;
    private UpdateWindow? _updateWindow;
    private UpdateRelease? _announcedUpdate;

    public KumoApplication(SingleInstance instance, string[] startupArguments)
    {
        _instance = instance;
        _startupArguments = startupArguments;
        _store = new SettingsStore(AppPaths.Settings, _log);
        _settings = _store.Load();
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    internal KumoSettings Settings => _settings;

    internal Log Log => _log;

    private bool UpdatesUsable => _updates?.Service.Capability is UpdateCapability.NotifyOnly or UpdateCapability.InstallVerified;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => _log.Error("Unhandled exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        _log.MinimumLevel = _settings.DiagnosticLogging ? LogLevel.Debug : LogLevel.Info;
        _updates = new UpdateCoordinator(_log, AppPaths.Root, () => _settings.UpdateChannel);
        _updates.CleanUpDownloads();
        _log.Info("Kumo started", ("version", _updates.Environment.CurrentVersion.ToString()), ("pid", Environment.ProcessId),
            ("installation", _updates.Environment.Installation.ToString()), ("updates", _updates.Service.Capability.ToString()));

        _menu = new OverlayController(this, new OperationRegistry(KumoOperations.CreateAll()),
            new OperationExecutor(_log, new ShellRecycleBin()), new SignatureFileTypeResolver());

        _messageWindow = new MessageWindow();
        _messageWindow.UnhandledException += ex => _log.Error("Message window handler failed", ex);
        _tray = new TrayIcon(_messageWindow, AppPaths.TrayIcon, "Kumo");
        _tray.MenuProvider = BuildTrayMenu;
        _tray.MenuCommand += id => OnTrayCommand((TrayCommand)id);
        _tray.Invoked += OpenKumo;
        _tray.NotificationClicked += () =>
        {
            if (_announcedUpdate is { } update)
                ShowUpdates(update);
            else if (_menu?.LastOutput is { } output)
                OverlayController.Reveal(output);
        };
        _tray.Show();
        if (!_tray.IsVisible)
            _log.Warning("Tray icon could not be added");

        _hotkey = new HotkeyRegistration(_messageWindow);
        _hotkey.Pressed += () => _menu.OpenForSelection();

        StartGestureMonitor();
        ApplySettings(_settings, announceProblems: false);

        _instance.Listen(
            args => Dispatcher.BeginInvoke(() => HandleArguments(args, fromAnotherLaunch: true)),
            ex => _log.Warning("Hand-over from another launch failed", ("detail", ex.Message)));

        if (!_settings.WelcomeShown)
        {
            _tray.ShowNotification("Kumo is ready", "Drag files from File Explorer and tap Shift to open Kumo.", TrayNotificationKind.Info);
            _settings.WelcomeShown = true;
            SaveSettings();
        }

        HandleArguments(_startupArguments, fromAnotherLaunch: false);
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => _menu.Prewarm());
        ScheduleFirstUpdateCheck();
    }

    /// <summary>
    /// Runs the automatic check if it is due. Called shortly after start-up and whenever Kumo is used, so a long-running
    /// instance still checks about once a day without keeping a timer alive. UpdateService enforces the 24-hour limit.
    /// </summary>
    internal async void ConsiderUpdateCheck()
    {
        if (!_settings.AutomaticUpdateChecks || !UpdatesUsable || _updates == null)
            return;
        try
        {
            var result = await _updates.Service.CheckAsync(manual: false, CancellationToken.None);
            if (result.Release is { } release && _updates.Service.ShouldNotify(result) && Notify($"Kumo {release.Version.ToShortString()} is available", "Click to see what's new.", TrayNotificationKind.Info))
            {
                _announcedUpdate = release;
                _updates.Service.MarkNotified(release.Version);
            }
        }
        catch (Exception ex) when (ex is UpdateException or System.Net.Http.HttpRequestException or System.IO.IOException)
        {
            _log.Warning("Automatic update check failed", ("detail", ex.Message));
        }
    }

    private void ScheduleFirstUpdateCheck()
    {
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = FirstUpdateCheckDelay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            ConsiderUpdateCheck();
        };
        timer.Start();
    }

    private UpdateSummary DescribeUpdates()
    {
        var environment = _updates!.Environment;
        string source = InstallationSource.Describe(environment.Installation);
        string description = _updates.Service.Capability switch
        {
            UpdateCapability.StoreManaged => "Updates managed by Microsoft Store.",
            UpdateCapability.Unconfigured => $"{source} · update checks aren't available in this build.",
            UpdateCapability.InstallVerified => $"{source} · updates are downloaded, verified and installed by Kumo.",
            _ when environment.Installation == InstallationKind.Portable => $"{source} · new versions are announced; replace the folder to update.",
            _ => $"{source} · new versions are announced and open the download page.",
        };
        return new UpdateSummary(UpdateCoordinator.DisplayVersion(), description, UpdatesUsable, () => ShowUpdates());
    }

    internal void ShowUpdates(UpdateRelease? release = null)
    {
        if (_updates == null || !UpdatesUsable)
            return;
        if (_updateWindow == null)
        {
            _updateWindow = new UpdateWindow(_updates, () => _menu?.HasRunningJobs == true, Shutdown);
            ApplyWindowTheme(_updateWindow);
            _updateWindow.Closed += (_, _) => _updateWindow = null;
            _updateWindow.Show();
        }
        _updateWindow.Activate();
        _announcedUpdate = null;
        if (release != null)
            _updateWindow.ShowAvailable(release);
        else
            _ = _updateWindow.CheckAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _menu?.Dispose();
        _updates?.Dispose();
        _monitor?.Dispose();
        _hotkey?.Dispose();
        _tray?.Dispose();
        _messageWindow?.Dispose();
        _log.Info("Kumo stopped");
        base.OnExit(e);
    }

    internal bool Notify(string title, string text, TrayNotificationKind kind)
    {
        bool allowed = _settings.Notifications switch
        {
            NotificationLevel.All => true,
            NotificationLevel.ErrorsOnly => kind != TrayNotificationKind.Info,
            _ => false,
        };
        if (allowed)
            _tray?.ShowNotification(title, text, kind);
        return allowed && _tray?.IsVisible == true;
    }

    internal void SetPointerTracking(bool enabled)
    {
        if (_monitor != null)
            _monitor.ReportPointerDowns = enabled;
    }

    private void StartGestureMonitor()
    {
        _monitor = new GlobalDragMonitor(
            input => Dispatcher.BeginInvoke(DispatcherPriority.Input, () => _menu?.OnInput(input)),
            ex => _log.Error("Input hook fault", ex));
        try
        {
            _monitor.Start();
        }
        catch (Win32Exception ex)
        {
            _log.Error("Global drag monitor unavailable", ex);
            _monitor.Dispose();
            _monitor = null;
            _tray?.ShowNotification("Drag gesture unavailable", "Kumo can still be opened from the tray icon or the keyboard shortcut.", TrayNotificationKind.Warning);
        }
    }

    private void ApplySettings(KumoSettings settings, bool announceProblems)
    {
        _settings = settings;
        _log.MinimumLevel = settings.DiagnosticLogging ? LogLevel.Debug : LogLevel.Info;

        if (_monitor != null)
        {
            _monitor.GesturesEnabled = settings.Activation != GestureActivation.Off;
            _monitor.Mode = settings.Activation == GestureActivation.ShiftHold ? ActivationMode.ShiftHold : ActivationMode.ShiftTap;
        }

        _hotkey?.Unregister();
        if (_hotkey != null && HotkeyGesture.TryParse(settings.Hotkey, out var gesture))
        {
            if (!_hotkey.Register((uint)gesture.Modifiers, (uint)gesture.VirtualKey))
            {
                _log.Warning("Keyboard shortcut unavailable", ("hotkey", gesture.ToString()));
                if (announceProblems)
                    Notify("Shortcut unavailable", $"{gesture} is already used by another application.", TrayNotificationKind.Warning);
            }
        }
    }

    private void SaveSettings()
    {
        try
        {
            _store.Save(_settings);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            _log.Error("Settings could not be saved", ex);
            Notify("Settings not saved", "Kumo could not write its settings file.", TrayNotificationKind.Error);
        }
    }

    private void HandleArguments(string[] arguments, bool fromAnotherLaunch)
    {
        if (arguments.Contains(CommandLine.Settings))
        {
            ShowSettings();
            return;
        }
        if (arguments.Contains(CommandLine.About))
        {
            ShowAbout();
            return;
        }
        var files = CommandLine.Files(arguments);
        if (files.Length > 0)
        {
            _menu?.OpenForFiles(files, OverlayWindowInterop.GetCursorPosition(), fromKeyboard: false);
            return;
        }
        if (fromAnotherLaunch && !arguments.Contains(CommandLine.Background))
            OpenKumo();
    }

    private void OpenKumo()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose files for Kumo",
            Multiselect = true,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() == true && dialog.FileNames.Length > 0)
            _menu?.OpenForFiles(dialog.FileNames, OverlayWindowInterop.GetCursorPosition(), fromKeyboard: false);
    }

    private IReadOnlyList<TrayMenuItem> BuildTrayMenu() =>
    [
        new((int)TrayCommand.Open, "Open Kumo…", IsDefault: true),
        new((int)TrayCommand.Settings, "Settings"),
        TrayMenuItem.Separator,
        new((int)TrayCommand.StartWithWindows, "Start with Windows", Checked: AutoStart.IsEnabled(AppPaths.Executable)),
        TrayMenuItem.Separator,
        .. (UpdatesUsable ? [new TrayMenuItem((int)TrayCommand.CheckForUpdates, "Check for updates…")] : Array.Empty<TrayMenuItem>()),
        new((int)TrayCommand.About, "About Kumo"),
        new((int)TrayCommand.Exit, "Exit"),
    ];

    private void OnTrayCommand(TrayCommand command)
    {
        switch (command)
        {
            case TrayCommand.Open:
                OpenKumo();
                break;
            case TrayCommand.Settings:
                ShowSettings();
                break;
            case TrayCommand.StartWithWindows:
                SetAutoStart(!AutoStart.IsEnabled(AppPaths.Executable));
                break;
            case TrayCommand.CheckForUpdates:
                ShowUpdates();
                break;
            case TrayCommand.About:
                ShowAbout();
                break;
            case TrayCommand.Exit:
                Shutdown();
                break;
        }
    }

    private void SetAutoStart(bool enabled)
    {
        try
        {
            AutoStart.Set(AppPaths.Executable, enabled);
            _log.Info("Start with Windows changed", ("enabled", enabled));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            _log.Error("Start with Windows could not be changed", ex);
            Notify("Setting not changed", "Kumo could not update the Windows startup entry.", TrayNotificationKind.Error);
        }
    }

    private void ShowSettings()
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings, AutoStart.IsEnabled(AppPaths.Executable), AppPaths.Logs, DescribeUpdates());
        ApplyWindowTheme(_settingsWindow);
        _settingsWindow.Closed += (_, _) =>
        {
            if (_settingsWindow?.Result is { } result)
            {
                ApplySettings(result.Settings, announceProblems: true);
                SaveSettings();
                if (result.StartWithWindows != AutoStart.IsEnabled(AppPaths.Executable))
                    SetAutoStart(result.StartWithWindows);
                _log.Info("Settings saved");
            }
            _settingsWindow = null;
        };
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ShowAbout()
    {
        if (_aboutWindow != null)
        {
            _aboutWindow.Activate();
            return;
        }
        _aboutWindow = new AboutWindow();
        ApplyWindowTheme(_aboutWindow);
        _aboutWindow.Closed += (_, _) => _aboutWindow = null;
        _aboutWindow.Show();
        _aboutWindow.Activate();
    }

    private void ApplyWindowTheme(Window window)
    {
        if (!SystemParameters.HighContrast)
            window.ThemeMode = Theming.Theme.WindowThemeMode(_settings.Theme);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _log.Error("Unhandled UI exception", e.Exception);
        Notify("Something went wrong", "Kumo recovered from an unexpected error. Details are in the log.", TrayNotificationKind.Error);
        e.Handled = true;
    }
}

internal sealed class ShellRecycleBin : IRecycleBin
{
    public void Recycle(string path) => RecycleBin.Send(path);
}
