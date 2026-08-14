namespace CharmDesk.Core.Interaction;

/// <summary>
/// Reaction hooks a charm can plug into. The engine ships one <see cref="DefaultCharmBehavior"/>
/// today, but keeping this as an interface (rather than hardcoding reactions into the window)
/// is what lets a future charm package register its own behavior - e.g. driven by
/// <c>CharmManifest.Future.CustomBehaviorId</c> - without touching engine code.
/// </summary>
public interface ICharmBehavior
{
    void OnHoverEnter(PhysicsEngine engine);
    void OnHoverExit(PhysicsEngine engine);
    void OnGrab(PhysicsEngine engine);
    void OnRelease(PhysicsEngine engine);
    void OnClick(PhysicsEngine engine);
    void OnDoubleClick(PhysicsEngine engine);
}
