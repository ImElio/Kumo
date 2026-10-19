namespace Kumo.Core.Settings;

public enum ThemePreference
{
    System,
    Light,
    Dark,
}

public enum NotificationLevel
{
    All,
    ErrorsOnly,
    None,
}

public enum GestureActivation
{
    ShiftTap,
    ShiftHold,
    Off,
}

public enum MenuSize
{
    Compact,
    Standard,
    Large,
}

public enum MotionPreference
{
    System,
    Reduced,
    Full,
}

public enum CollisionBehavior
{
    AddNumber,
    ReplaceOutput,
}

/// <summary>
/// User preferences persisted as JSON. "Start with Windows" is intentionally absent: the registry Run entry is
/// the single source of truth so the setting cannot drift from what Windows actually does.
/// </summary>
public sealed class KumoSettings
{
    public const int CurrentVersion = 1;
    public const int MinConcurrency = 1;
    public const int MaxConcurrency = 4;
    public const string DefaultHotkey = "Ctrl+Alt+K";

    public int Version { get; set; } = CurrentVersion;

    public string Language { get; set; } = "en";

    public ThemePreference Theme { get; set; } = ThemePreference.System;

    public NotificationLevel Notifications { get; set; } = NotificationLevel.All;

    public GestureActivation Activation { get; set; } = GestureActivation.ShiftTap;

    public MenuSize MenuSize { get; set; } = MenuSize.Standard;

    public MotionPreference Motion { get; set; } = MotionPreference.System;

    /// <summary>Empty disables the shortcut.</summary>
    public string Hotkey { get; set; } = DefaultHotkey;

    /// <summary>Empty saves results beside the originals.</summary>
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>When false, originals are moved to the Recycle Bin after a successful per-file operation.</summary>
    public bool PreserveOriginals { get; set; } = true;

    public CollisionBehavior Collisions { get; set; } = CollisionBehavior.AddNumber;

    public int MaxConcurrentOperations { get; set; } = 2;

    public bool DiagnosticLogging { get; set; }

    public bool WelcomeShown { get; set; }

    public KumoSettings Normalize()
    {
        Version = CurrentVersion;
        Language = string.IsNullOrWhiteSpace(Language) ? "en" : Language.Trim();
        Theme = Enum.IsDefined(Theme) ? Theme : ThemePreference.System;
        Notifications = Enum.IsDefined(Notifications) ? Notifications : NotificationLevel.All;
        Activation = Enum.IsDefined(Activation) ? Activation : GestureActivation.ShiftTap;
        MenuSize = Enum.IsDefined(MenuSize) ? MenuSize : MenuSize.Standard;
        Motion = Enum.IsDefined(Motion) ? Motion : MotionPreference.System;
        Collisions = Enum.IsDefined(Collisions) ? Collisions : CollisionBehavior.AddNumber;
        Hotkey = Hotkey?.Trim() ?? string.Empty;
        if (Hotkey.Length > 0 && !HotkeyGesture.TryParse(Hotkey, out _))
            Hotkey = DefaultHotkey;
        OutputDirectory = OutputDirectory?.Trim() ?? string.Empty;
        if (OutputDirectory.Length > 0 && !Path.IsPathFullyQualified(OutputDirectory))
            OutputDirectory = string.Empty;
        MaxConcurrentOperations = Math.Clamp(MaxConcurrentOperations, MinConcurrency, MaxConcurrency);
        return this;
    }

    public KumoSettings Clone() => (KumoSettings)MemberwiseClone();
}
