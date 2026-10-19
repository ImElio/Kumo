using Kumo.Core.Settings;

namespace Kumo.Core.Tests;

public class HotkeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+Alt+K", HotkeyModifiers.Control | HotkeyModifiers.Alt, 'K')]
    [InlineData("ctrl + shift + f9", HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x78)]
    [InlineData("Win+Alt+Space", HotkeyModifiers.Windows | HotkeyModifiers.Alt, 0x20)]
    [InlineData("Alt+7", HotkeyModifiers.Alt, '7')]
    public void Parses_valid_gestures(string text, HotkeyModifiers modifiers, int key)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Equal(modifiers, gesture.Modifiers);
        Assert.Equal(key, gesture.VirtualKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("K")]
    [InlineData("Shift+K")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+K+L")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+Banana")]
    public void Rejects_gestures_that_would_capture_typing_or_are_malformed(string text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out _));
    }

    [Theory]
    [InlineData("Ctrl+Alt+K")]
    [InlineData("Ctrl+Shift+F12")]
    [InlineData("Alt+Win+PageDown")]
    public void Formatting_round_trips(string text)
    {
        HotkeyGesture.TryParse(text, out var gesture);
        Assert.True(HotkeyGesture.TryParse(gesture.ToString(), out var again));
        Assert.Equal(gesture, again);
    }

    [Fact]
    public void Modifiers_are_formatted_in_canonical_order()
    {
        HotkeyGesture.TryParse("Shift+Win+Alt+Ctrl+A", out var gesture);
        Assert.Equal("Ctrl+Alt+Shift+Win+A", gesture.ToString());
    }
}
