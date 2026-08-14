using System;

namespace CharmDesk.Core.Interaction;

/// <summary>
/// Turns raw pointer input (already translated into the physics engine's local coordinate
/// space) into grab/pull/release/click/hover events against a <see cref="PhysicsEngine"/> and
/// its <see cref="ICharmBehavior"/>. Kept independent of WPF/Win32 so the same logic could
/// later drive a different rendering surface.
/// </summary>
public sealed class InteractionSystem
{
    private readonly PhysicsEngine _engine;
    private ICharmBehavior _behavior;

    public double HitRadius { get; set; } = 40;

    public bool IsHovering { get; private set; }
    public bool IsGrabbing { get; private set; }

    public event Action<bool>? HoverChanged;

    public InteractionSystem(PhysicsEngine engine, ICharmBehavior? behavior = null)
    {
        _engine = engine;
        _behavior = behavior ?? new DefaultCharmBehavior();
    }

    /// <summary>Swaps the active behavior - used by the Charm Manager to reflect a chosen
    /// ReactionStyle in its live preview as soon as the user changes it.</summary>
    public void SetBehavior(ICharmBehavior behavior) => _behavior = behavior;

    public bool HitTest(double localX, double localY)
    {
        var dx = localX - _engine.BobX;
        var dy = localY - _engine.BobY;
        return dx * dx + dy * dy <= HitRadius * HitRadius;
    }

    public void OnMouseMove(double localX, double localY)
    {
        if (IsGrabbing)
        {
            _engine.UpdateGrabTarget(localX, localY);
            return;
        }

        var hovering = HitTest(localX, localY);
        if (hovering != IsHovering)
        {
            IsHovering = hovering;
            if (hovering) _behavior.OnHoverEnter(_engine); else _behavior.OnHoverExit(_engine);
            HoverChanged?.Invoke(hovering);
        }
    }

    public bool TryBeginGrab(double localX, double localY)
    {
        if (!HitTest(localX, localY))
            return false;

        IsGrabbing = true;
        _engine.BeginGrab(localX, localY);
        _behavior.OnGrab(_engine);
        return true;
    }

    public void EndGrab()
    {
        if (!IsGrabbing) return;
        IsGrabbing = false;
        _engine.EndGrab();
        _behavior.OnRelease(_engine);
    }

    public void RegisterClick(int clickCount)
    {
        if (clickCount >= 2) _behavior.OnDoubleClick(_engine);
        else _behavior.OnClick(_engine);
    }
}
