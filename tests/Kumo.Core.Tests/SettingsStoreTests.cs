using Kumo.Core.Diagnostics;
using Kumo.Core.Settings;

namespace Kumo.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kumo-settings-").FullName;

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Missing_file_yields_defaults()
    {
        var settings = new SettingsStore(SettingsPath, Log.Null).Load();

        Assert.Equal(GestureActivation.ShiftTap, settings.Activation);
        Assert.True(settings.PreserveOriginals);
        Assert.Equal(KumoSettings.DefaultHotkey, settings.Hotkey);
    }

    [Fact]
    public void Settings_round_trip()
    {
        var store = new SettingsStore(SettingsPath, Log.Null);
        store.Save(new KumoSettings
        {
            Theme = ThemePreference.Dark,
            Activation = GestureActivation.ShiftHold,
            MenuSize = MenuSize.Large,
            Hotkey = "Ctrl+Shift+F9",
            OutputDirectory = _directory,
            PreserveOriginals = false,
            Collisions = CollisionBehavior.ReplaceOutput,
            MaxConcurrentOperations = 3,
            DiagnosticLogging = true,
        });

        var loaded = store.Load();

        Assert.Equal(ThemePreference.Dark, loaded.Theme);
        Assert.Equal(GestureActivation.ShiftHold, loaded.Activation);
        Assert.Equal(MenuSize.Large, loaded.MenuSize);
        Assert.Equal("Ctrl+Shift+F9", loaded.Hotkey);
        Assert.Equal(_directory, loaded.OutputDirectory);
        Assert.False(loaded.PreserveOriginals);
        Assert.Equal(CollisionBehavior.ReplaceOutput, loaded.Collisions);
        Assert.Equal(3, loaded.MaxConcurrentOperations);
        Assert.True(loaded.DiagnosticLogging);
    }

    [Fact]
    public void Save_leaves_no_temporary_files()
    {
        var store = new SettingsStore(SettingsPath, Log.Null);
        store.Save(new KumoSettings());
        store.Save(new KumoSettings { Theme = ThemePreference.Light });

        Assert.Equal(["settings.json"], Directory.GetFiles(_directory).Select(Path.GetFileName));
    }

    [Fact]
    public void Enums_are_stored_as_names()
    {
        new SettingsStore(SettingsPath, Log.Null).Save(new KumoSettings { Theme = ThemePreference.Dark });

        Assert.Contains("\"Theme\": \"Dark\"", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Hotkeys_are_stored_readably()
    {
        new SettingsStore(SettingsPath, Log.Null).Save(new KumoSettings());

        Assert.Contains("\"Hotkey\": \"Ctrl+Alt+K\"", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Corrupt_file_is_set_aside_and_defaults_are_used()
    {
        File.WriteAllText(SettingsPath, "{ \"Theme\": ");

        var settings = new SettingsStore(SettingsPath, Log.Null).Load();

        Assert.Equal(ThemePreference.System, settings.Theme);
        Assert.False(File.Exists(SettingsPath));
        Assert.Single(Directory.GetFiles(_directory, "settings.json.corrupt-*"));
    }

    [Fact]
    public void Out_of_range_values_are_normalized()
    {
        File.WriteAllText(SettingsPath, """
            {
              "MaxConcurrentOperations": 64,
              "Hotkey": "K",
              "OutputDirectory": "relative\\folder",
              "Theme": 42
            }
            """);

        var settings = new SettingsStore(SettingsPath, Log.Null).Load();

        Assert.Equal(KumoSettings.MaxConcurrency, settings.MaxConcurrentOperations);
        Assert.Equal(KumoSettings.DefaultHotkey, settings.Hotkey);
        Assert.Equal(string.Empty, settings.OutputDirectory);
        Assert.Equal(ThemePreference.System, settings.Theme);
    }

    [Fact]
    public void Empty_hotkey_means_disabled_and_is_kept()
    {
        var store = new SettingsStore(SettingsPath, Log.Null);
        store.Save(new KumoSettings { Hotkey = "" });

        Assert.Equal(string.Empty, store.Load().Hotkey);
    }
}
