namespace CharmDesk.Core.Interaction;

/// <summary>The stock reaction set every charm gets unless it opts into something custom.</summary>
public sealed class DefaultCharmBehavior : ICharmBehavior
{
    public void OnHoverEnter(PhysicsEngine engine) => engine.Nudge(0.045);

    public void OnHoverExit(PhysicsEngine engine) { }

    public void OnGrab(PhysicsEngine engine) { }

    public void OnRelease(PhysicsEngine engine) { }

    public void OnClick(PhysicsEngine engine) => engine.Bounce();

    public void OnDoubleClick(PhysicsEngine engine) => engine.Spike();
}
