namespace Kumo.Core.Radial;

public enum DropOutcome
{
    /// <summary>Not Kumo's drop: report no effect and let the overlay close when the drag ends.</summary>
    Reject,

    /// <summary>Accept and run or open the segment under the pointer.</summary>
    InvokeSegment,

    /// <summary>Accept and keep the menu open for clicks, for example while the dropped files are still being read.</summary>
    KeepOpen,
}

/// <summary>What a drop on the open overlay means, decided from the menu state at the moment of the drop.</summary>
public static class DropDecision
{
    /// <param name="menuOpen">The overlay is showing the menu for the current drag.</param>
    /// <param name="filesReady">The dragged files have been inspected and the menu's actions are known.</param>
    /// <param name="hasActions">Inspection found at least one action; ignored while files are not ready.</param>
    public static DropOutcome Decide(bool menuOpen, bool filesReady, bool hasActions, RadialHitKind hit)
    {
        if (!menuOpen || hit == RadialHitKind.Outside)
            return DropOutcome.Reject;
        if (!filesReady)
            return DropOutcome.KeepOpen;
        if (!hasActions)
            return DropOutcome.Reject;
        return hit == RadialHitKind.Segment ? DropOutcome.InvokeSegment : DropOutcome.KeepOpen;
    }
}
