using Kumo.Windows.Shell;

namespace Kumo.Windows.Tests;

public class ShellTests
{
    [Fact]
    public void Hand_over_payload_round_trips_unicode_paths()
    {
        string[] arguments = [@"C:\Users\ëlio\Pictures\日本 photo.png", @"D:\a b\c.pdf", "--background"];

        Assert.Equal(arguments, SingleInstance.Decode(SingleInstance.Encode(arguments)));
    }

    [Fact]
    public void Empty_hand_over_decodes_to_no_arguments()
    {
        Assert.Empty(SingleInstance.Decode(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public async Task Second_instance_is_not_primary_and_hands_over_arguments()
    {
        string id = "KumoTest" + Guid.NewGuid().ToString("N");
        using var primary = new SingleInstance(id);
        var received = new TaskCompletionSource<string[]>();
        primary.Listen(args => received.TrySetResult(args));

        var secondary = new Thread(() =>
        {
            using var other = new SingleInstance(id);
            Assert.False(other.IsPrimary);
            Assert.True(other.TrySend(["--show", @"C:\x.png"], TimeSpan.FromSeconds(5)));
        });
        secondary.Start();
        secondary.Join();

        Assert.True(primary.IsPrimary);
        var arguments = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["--show", @"C:\x.png"], arguments);
    }

    [Fact]
    public void Autostart_command_quotes_the_executable_and_starts_in_background()
    {
        Assert.Equal("\"C:\\Program Files\\Kumo\\Kumo.exe\" --background", AutoStart.Command(@"C:\Program Files\Kumo\Kumo.exe"));
    }
}
