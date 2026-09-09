using CharmDesk.Core.Models;

namespace CharmDesk.Core.Interaction;

/// <summary>
/// The stock reaction set every charm gets, parameterized by its manifest's
/// <see cref="ReactionStyle"/> - a "personality" knob rather than a bespoke behavior class per
/// charm, so any charm package can opt into a punchier or gentler feel just by data.
/// </summary>
public sealed class DefaultCharmBehavior : ICharmBehavior
{
    private readonly ReactionStyle _style;

    public DefaultCharmBehavior(ReactionStyle style = ReactionStyle.Default)
    {
        _style = style;
    }

    private double Intensity => _style switch
    {
        ReactionStyle.Playful => 1.4,
        ReactionStyle.Gentle => 0.6,
        ReactionStyle.Dramatic => 1.8,
        _ => 1.0,
    };

    public void OnHoverEnter(PhysicsEngine engine) =>
        engine.Nudge(0.045 * (_style == ReactionStyle.Playful ? 1.6 : 1.0));

    public void OnHoverExit(PhysicsEngine engine) { }

    public void OnGrab(PhysicsEngine engine) { }

    public void OnRelease(PhysicsEngine engine) { }

    public void OnClick(PhysicsEngine engine) => engine.Bounce(Intensity);

    public void OnDoubleClick(PhysicsEngine engine)
    {
        engine.Spike(Intensity);
        if (_style == ReactionStyle.Dramatic) engine.Nudge(0.3); // extra swing kick, for flair
    }
}
