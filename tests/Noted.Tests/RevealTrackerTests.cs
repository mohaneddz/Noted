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
}
