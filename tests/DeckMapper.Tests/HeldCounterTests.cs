using DeckMapper;
using Xunit;

namespace DeckMapper.Tests;

public class HeldCounterTests
{
    static readonly KeyDef W = new(0x11, false);

    [Fact]
    public void SameKeyOnTwoControls_ReleasedOnlyByTheLast()
    {
        // RT = W trzymany, w trakcie lewa gałka ↑ (też W) wciśnięta i puszczona
        var held = new HeldCounter<KeyDef>();
        Assert.True(held.Press(W));    // RT: W↓
        Assert.False(held.Press(W));   // gałka ↑: bez drugiego W↓
        Assert.False(held.Release(W)); // gałka wraca: W NIE może się puścić
        Assert.Equal(1, held.CountOf(W));
        Assert.True(held.Release(W));  // puszczenie RT: teraz W↑
        Assert.True(held.IsEmpty);
    }

    [Fact]
    public void ReleaseWithoutPress_DoesNothing()
    {
        var held = new HeldCounter<KeyDef>();
        Assert.False(held.Release(W));
        Assert.True(held.Press(W));
        Assert.True(held.Release(W));
        Assert.False(held.Release(W));
    }

    [Fact]
    public void DifferentKeys_AreIndependent()
    {
        var held = new HeldCounter<KeyDef>();
        var s = new KeyDef(0x1F, false);
        Assert.True(held.Press(W));
        Assert.True(held.Press(s));
        Assert.True(held.Release(W));
        Assert.Equal(1, held.CountOf(s));
    }
}
