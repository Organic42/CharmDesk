using System;
using CharmDesk.Core.Models;

namespace CharmDesk.Core;

/// <summary>
/// A polar pendulum-spring simulation for a single hanging charm.
///
/// The charm's position is tracked as (Theta, Radius) around a fixed anchor rather than as
/// raw X/Y: Theta is the swing angle from straight-down, Radius is the current string length
/// (which can stretch a little beyond its resting <see cref="CharmPhysicsSettings.StringLength"/>
/// for secondary "elastic" motion). Both the free-swinging pendulum and the cursor-grabbed
/// state integrate through the same Theta/Radius/Spin state, so momentum a drag builds up
/// carries over naturally the instant the charm is released - no separate conversion step.
/// </summary>
public sealed class PhysicsEngine
{
    private const double MinRadiusFactor = 0.55;
    private const double MaxRadiusFactor = 1.35;

    private readonly Random _rng = new();

    public CharmPhysicsSettings Settings { get; set; }

    /// <summary>Master intensity knob from Settings (1.0 = as authored, 0 = inert).</summary>
    public double Intensity { get; set; } = 1.0;

    public bool Enabled { get; set; } = true;

    public double AnchorX { get; set; }
    public double AnchorY { get; set; }

    public double Theta { get; private set; }
    public double ThetaVelocity { get; private set; }
    public double Radius { get; private set; }
    public double RadiusVelocity { get; private set; }

    /// <summary>Rotation of the charm sprite about its own center, in radians.</summary>
    public double Spin { get; private set; }
    public double SpinVelocity { get; private set; }

    public bool IsGrabbed { get; private set; }
    private double _targetX;
    private double _targetY;

    public PhysicsEngine(CharmPhysicsSettings settings)
    {
        Settings = settings;
        Radius = settings.StringLength;
    }

    public double BobX => AnchorX + Radius * Math.Sin(Theta);
    public double BobY => AnchorY + Radius * Math.Cos(Theta);

    public void BeginGrab(double cursorX, double cursorY)
    {
        IsGrabbed = true;
        _targetX = cursorX;
        _targetY = cursorY;
    }

    public void UpdateGrabTarget(double cursorX, double cursorY)
    {
        _targetX = cursorX;
        _targetY = cursorY;
    }

    public void EndGrab()
    {
        IsGrabbed = false;
    }

    /// <summary>A quick tug on the string - a small playful bounce, e.g. on single click.
    /// <paramref name="intensity"/> scales the impulse (1.0 = as authored) so a charm's
    /// per-manifest <c>ReactionStyle</c> can make it feel punchier or more subdued.</summary>
    public void Bounce(double intensity = 1.0)
    {
        var mass = Math.Max(0.15, Settings.Mass);
        RadiusVelocity -= Settings.StringLength * 2.6 * intensity / mass;
        ThetaVelocity += (_rng.NextDouble() - 0.5) * 0.6 * intensity / mass;
    }

    /// <summary>A twist about the charm's own axis, e.g. on double click.</summary>
    public void Spike(double intensity = 1.0)
    {
        var mass = Math.Max(0.15, Settings.Mass);
        SpinVelocity += Math.PI * 5.5 * intensity * (_rng.NextDouble() < 0.5 ? -1 : 1) / mass;
    }

    /// <summary>A small swing impulse, e.g. a hover greeting or an on-launch arrival flourish.</summary>
    public void Nudge(double thetaImpulse)
    {
        ThetaVelocity += thetaImpulse;
    }

    public void Step(double dt)
    {
        if (dt <= 0) return;
        dt = Math.Min(dt, 1.0 / 20.0); // clamp huge dt spikes (e.g. after the window was hidden)

        var stringLength = Math.Max(1, Settings.StringLength);
        var gravity = Settings.Gravity * Intensity;
        var stiffnessGain = Settings.Stiffness * 60.0 * Math.Max(Intensity, 0.05);

        // "Damping" in the manifest is authored as a per-frame retention factor (e.g. 0.92)
        // in the style of simple JS pendulum demos. Applied literally as Pow(damping, dt*60)
        // it compounds far harder than it looks - at 0.92 that's a 99%+ velocity loss every
        // real second, which overdamps the pendulum below its own natural frequency and kills
        // the visible swing entirely (confirmed by simulating a grab/release: the charm crept
        // back to rest over ~5s with essentially no oscillation, instead of a few visible
        // swings). Converting it to a damping *ratio* relative to the pendulum's natural
        // frequency and integrating it as a continuous force instead keeps a natural-feeling,
        // framerate-independent decay with the swing motion spec calls for.
        var omegaN = Math.Sqrt(gravity / stringLength);
        var zeta = Math.Clamp((1.0 - Settings.Damping) * 2.6, 0.05, 1.4);
        var mass = Math.Max(0.15, Settings.Mass);

        if (!Enabled)
        {
            // Physics disabled: charm can still be dragged, but snaps directly with no
            // momentum, and otherwise hangs perfectly still.
            if (IsGrabbed)
            {
                var dx0 = _targetX - AnchorX;
                var dy0 = _targetY - AnchorY;
                Theta = Math.Atan2(dx0, dy0);
                Radius = Math.Clamp(Math.Sqrt(dx0 * dx0 + dy0 * dy0), stringLength * MinRadiusFactor, stringLength * MaxRadiusFactor);
            }
            else
            {
                Theta = 0;
                Radius = stringLength;
            }
            ThetaVelocity = RadiusVelocity = SpinVelocity = 0;
            return;
        }

        double thetaAccel;
        double radiusAccel;
        double spinAccel;

        if (IsGrabbed)
        {
            var dx = _targetX - AnchorX;
            var dy = _targetY - AnchorY;
            var targetTheta = Math.Atan2(dx, dy);
            var targetRadius = Math.Clamp(Math.Sqrt(dx * dx + dy * dy), stringLength * MinRadiusFactor, stringLength * MaxRadiusFactor);

            // Drag responsiveness is its own critically-ish-damped spring toward the cursor,
            // independent of the free-swing damping above - "stiffness" is the spec's per-charm
            // knob for exactly this. Slightly underdamped (ratio < 1) so it still has a hair of
            // physical give instead of feeling rigidly glued to the cursor.
            var dragK = stiffnessGain * 6.5;
            var dragDampingRatio = 0.72;
            var dragC = 2 * dragDampingRatio * Math.Sqrt(dragK);
            thetaAccel = (dragK * AngleDelta(targetTheta, Theta) - dragC * ThetaVelocity) / mass;

            var dragKRadius = dragK * 9.0;
            var dragCRadius = 2 * 0.85 * Math.Sqrt(dragKRadius);
            radiusAccel = (dragKRadius * (targetRadius - Radius) - dragCRadius * RadiusVelocity) / mass;

            // The charm stays upright while held - actively spring back to zero rotation
            // rather than just decaying whatever spin it already had, so grabbing it mid-spin
            // (e.g. right after a double-click) straightens it out instead of dragging it
            // around tilted.
            var spinK = dragK * 0.6;
            var spinC = 2 * 0.9 * Math.Sqrt(spinK);
            spinAccel = (-spinK * Spin - spinC * SpinVelocity) / mass;
        }
        else
        {
            // Gravity's restoring torque is intentionally mass-independent (a real pendulum's
            // period doesn't depend on its bob's mass) - only the secondary, non-gravity motions
            // (string stretch, spin) get slowed down by a heavier charm.
            thetaAccel = -omegaN * omegaN * Math.Sin(Theta) - 2 * zeta * omegaN * ThetaVelocity;
            var omegaR = Math.Sqrt(stiffnessGain * 20);
            radiusAccel = (-omegaR * omegaR * (Radius - stringLength) - 2 * zeta * omegaR * RadiusVelocity) / mass;
            spinAccel = -2 * zeta * omegaN * 2.5 * SpinVelocity / mass;

            // NOTE: there used to be a permanent sinusoidal "idle sway" driving force here, to
            // keep the charm from looking frozen. It held the pendulum at a steady-state
            // amplitude well above the IsAtRest threshold, so the 60fps render loop could never
            // reach rest and shut itself off - the charm animated continuously, forever, for a
            // motion too small to notice. CharmWindow's idle-flourish timer now provides that
            // "still alive" feel with an occasional nudge every 20-40s instead, which lets the
            // render loop actually stop in between. Don't reintroduce a continuous driver here.
        }

        ThetaVelocity += thetaAccel * dt;
        RadiusVelocity += radiusAccel * dt;
        SpinVelocity += spinAccel * dt;

        Theta += ThetaVelocity * dt;
        Radius = Math.Clamp(Radius + RadiusVelocity * dt, stringLength * MinRadiusFactor, stringLength * MaxRadiusFactor);
        Spin += SpinVelocity * dt;

        // Keep Spin bounded so it doesn't grow without limit across many double-clicks.
        if (Spin > Math.PI * 4) Spin -= Math.PI * 4;
        if (Spin < -Math.PI * 4) Spin += Math.PI * 4;
    }

    /// <summary>Shortest signed angular delta from `from` to `to`, wrapped to [-pi, pi].</summary>
    private static double AngleDelta(double to, double from)
    {
        var d = to - from;
        while (d > Math.PI) d -= 2 * Math.PI;
        while (d < -Math.PI) d += 2 * Math.PI;
        return d;
    }

    /// <summary>True once the charm has essentially stopped moving, for idle CPU throttling.</summary>
    public bool IsAtRest =>
        !IsGrabbed &&
        Math.Abs(ThetaVelocity) < 0.01 &&
        Math.Abs(RadiusVelocity) < 0.5 &&
        Math.Abs(SpinVelocity) < 0.01 &&
        Math.Abs(Theta) < 0.01;
}
