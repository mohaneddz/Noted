using Noted.Rendering;

namespace Noted.Tests;

public class RevealTrackerTests
{
    [Fact]
    public void RangeIsRevealedWhenCaretFallsInsideIt()
    {
        var tracker = new RevealTracker();
        // No editor attached: caret defaults to line 1.
        Assert.True(tracker.IsRangeRevealed(1, 5));
        Assert.False(tracker.IsRangeRevealed(2, 5));
    }

    [Fact]
    public void DisabledTrackerRevealsEverything()
    {
        var tracker = new RevealTracker { Enabled = false };
        Assert.True(tracker.IsRangeRevealed(10, 20));
    }

    [Fact]
    public void SecondarySelectionsRevealTheirLinesAndDirtyTheOldRangeWhenCleared()
    {
        var tracker = new RevealTracker();
        (int Start, int End) dirty = default;
        tracker.RevealChanged += (start, end) => dirty = (start, end);
        tracker.SetAdditionalRanges([(5, 7), (12, 12)]);
        Assert.True(tracker.IsRevealed(6));
        Assert.True(tracker.IsRangeRevealed(10, 15));
        Assert.False(tracker.IsRevealed(9));
        tracker.SetAdditionalRanges([]);
        Assert.False(tracker.IsRevealed(6));
        Assert.Equal((5, 12), dirty);
    }
}
