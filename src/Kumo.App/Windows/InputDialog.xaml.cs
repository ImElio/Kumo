using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using Kumo.Core.Operations;

namespace Kumo.App.Windows;

/// <summary>The small dialog used only by operations that cannot run without extra input.</summary>
public partial class InputDialog : Window
{
    private readonly InputRequirement _requirement;
    private readonly int _minimumPasswordLength;

    private InputDialog(InputRequirement requirement, string title, string prompt, string confirm, int minimumPasswordLength)
    {
        InitializeComponent();
        _requirement = requirement;
        _minimumPasswordLength = minimumPasswordLength;
        Title = title;
        Prompt.Text = prompt;
        ConfirmButton.Content = confirm;

        if (requirement == InputRequirement.Password)
        {
            PasswordInputs.Visibility = Visibility.Visible;
            Hint.Text = $"At least {minimumPasswordLength} characters. Kumo cannot recover a forgotten password.";
            Loaded += (_, _) => Password.Focus();
        }
        else
        {
            PageInput.Visibility = Visibility.Visible;
            AutomationProperties.SetName(PageInput, prompt);
            Hint.Text = "Separate pages with commas. A range like 8- runs to the last page.";
            Loaded += (_, _) => PageInput.Focus();
        }
    }

    public string? Value { get; private set; }

    public static string? AskPageRange(Window owner, ThemeMode theme, int fileCount)
    {
        string prompt = fileCount == 1 ? "Which pages do you want to keep?" : $"Which pages do you want to keep from each of the {fileCount} PDFs?";
        return Show(new InputDialog(InputRequirement.PageRange, "Extract pages", prompt, "Extract", 0), owner, theme);
    }

    public static string? AskPassword(Window owner, ThemeMode theme, int minimumLength) =>
        Show(new InputDialog(InputRequirement.Password, "Protect PDF", "Choose a password for the protected copy.", "Protect", minimumLength), owner, theme);

    private static string? Show(InputDialog dialog, Window owner, ThemeMode theme)
    {
        if (!SystemParameters.HighContrast)
            dialog.ThemeMode = theme;
        dialog.Owner = owner;
        dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        dialog.Loaded += (_, _) => dialog.Activate();
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        string? error = null;
        if (_requirement == InputRequirement.Password)
        {
            if (Password.Password.Length < _minimumPasswordLength)
                error = $"Use at least {_minimumPasswordLength} characters.";
            else if (Password.Password != Confirmation.Password)
                error = "The passwords don't match.";
            else
                Value = Password.Password;
        }
        else if (PageRange.IsWellFormed(PageInput.Text, out string problem))
        {
            Value = PageInput.Text.Trim();
        }
        else
        {
            error = problem;
        }

        if (error != null)
        {
            Error.Text = error;
            Error.Visibility = Visibility.Visible;
            UIElementAutomationPeer.FromElement(Error)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            return;
        }
        DialogResult = true;
    }
}
