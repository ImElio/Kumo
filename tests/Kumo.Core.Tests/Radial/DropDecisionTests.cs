using Kumo.Core.Radial;

namespace Kumo.Core.Tests.Radial;

public class DropDecisionTests
{
    [Theory]
    [InlineData(RadialHitKind.Hub)]
    [InlineData(RadialHitKind.Segment)]
    public void A_drop_while_files_are_still_being_read_keeps_the_menu_open(RadialHitKind hit)
    {
        Assert.Equal(DropOutcome.KeepOpen, DropDecision.Decide(menuOpen: true, filesReady: false, hasActions: false, hit));
    }

    [Fact]
    public void A_drop_on_a_segment_of_a_ready_menu_invokes_it()
    {
        Assert.Equal(DropOutcome.InvokeSegment, DropDecision.Decide(true, true, true, RadialHitKind.Segment));
    }

    [Fact]
    public void A_drop_on_the_hub_of_a_ready_menu_keeps_it_open()
    {
        Assert.Equal(DropOutcome.KeepOpen, DropDecision.Decide(true, true, true, RadialHitKind.Hub));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    public void A_drop_outside_the_ring_is_never_taken(bool menuOpen, bool filesReady, bool hasActions)
    {
        Assert.Equal(DropOutcome.Reject, DropDecision.Decide(menuOpen, filesReady, hasActions, RadialHitKind.Outside));
    }

    [Theory]
    [InlineData(RadialHitKind.Hub)]
    [InlineData(RadialHitKind.Segment)]
    public void Nothing_is_accepted_when_the_menu_is_not_open(RadialHitKind hit)
    {
        Assert.Equal(DropOutcome.Reject, DropDecision.Decide(menuOpen: false, filesReady: false, hasActions: false, hit));
        Assert.Equal(DropOutcome.Reject, DropDecision.Decide(menuOpen: false, filesReady: true, hasActions: true, hit));
    }

    [Theory]
    [InlineData(RadialHitKind.Hub)]
    [InlineData(RadialHitKind.Segment)]
    public void Files_without_actions_are_not_accepted(RadialHitKind hit)
    {
        Assert.Equal(DropOutcome.Reject, DropDecision.Decide(true, filesReady: true, hasActions: false, hit));
    }
}
