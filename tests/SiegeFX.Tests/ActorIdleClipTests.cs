using SiegeFX.Core.Actors;

namespace SiegeFX.Tests;

public class ActorIdleClipTests
{
    // Krug-like clip table: 0 = chore_default (one-frame stance),
    // 3 = chore_fidget (moving idle), 5 = an attack.
    private const int Clips = 8;
    private const int Fidget = 3;

    [Fact]
    public void Idle_on_the_default_slot_shows_the_fidget()
        => Assert.Equal(Fidget, Actor.SelectClip(0, overrideActive: false, Fidget, Clips));

    [Fact]
    public void A_timed_override_on_slot_zero_is_kept()
        => Assert.Equal(0, Actor.SelectClip(0, overrideActive: true, Fidget, Clips));

    [Theory]
    [InlineData(1)] // walk
    [InlineData(5)] // attack
    public void Any_other_selection_wins_over_idle(int selected)
        => Assert.Equal(selected, Actor.SelectClip(selected, overrideActive: false, Fidget, Clips));

    [Theory]
    [InlineData(-1)]
    [InlineData(Clips)]
    public void No_or_bad_selection_falls_back_to_idle(int selected)
        => Assert.Equal(Fidget, Actor.SelectClip(selected, overrideActive: false, Fidget, Clips));

    [Fact]
    public void Without_an_idle_clip_the_default_stance_shows()
    {
        Assert.Equal(0, Actor.SelectClip(0, overrideActive: false, idleClip: 0, Clips));
        Assert.Equal(0, Actor.SelectClip(-1, overrideActive: false, idleClip: 99, Clips));
    }
}
