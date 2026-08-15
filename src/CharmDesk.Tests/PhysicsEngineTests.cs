using CharmDesk.Core;
using CharmDesk.Core.Models;

namespace CharmDesk.Tests;

public class PhysicsEngineTests
{
    private static PhysicsEngine NewEngine(CharmPhysicsSettings? settings = null)
    {
        var engine = new PhysicsEngine(settings ?? new CharmPhysicsSettings());
        engine.AnchorX = 0;
        engine.AnchorY = 0;
        return engine;
    }

    [Fact]
    public void Step_WithNonPositiveDt_LeavesStateUnchanged()
    {
        var engine = NewEngine();
        engine.Nudge(0.4); // disturb it so Theta/ThetaVelocity aren't trivially zero either way
        var (theta, thetaVel, radius) = (engine.Theta, engine.ThetaVelocity, engine.Radius);

        engine.Step(0);
        engine.Step(-1);

        Assert.Equal(theta, engine.Theta);
        Assert.Equal(thetaVel, engine.ThetaVelocity);
        Assert.Equal(radius, engine.Radius);
    }

    [Fact]
    public void UndisturbedPendulum_StaysAtRest()
    {
        var engine = NewEngine();

        for (var i = 0; i < 120; i++)
            engine.Step(1.0 / 60.0);

        Assert.True(engine.IsAtRest);
        Assert.Equal(0, engine.Theta, precision: 3);
    }

    [Fact]
    public void ReleasedAfterGrab_EventuallySettlesBackToRest()
    {
        var engine = NewEngine();

        engine.BeginGrab(80, 40);
        for (var i = 0; i < 30; i++)
            engine.Step(1.0 / 60.0);
        engine.EndGrab();

        // A released pendulum should swing, not stay frozen at the release point.
        Assert.False(engine.IsAtRest);

        // Simulate up to 20 seconds of settling time; the damped pendulum must reach rest well
        // before that, or the fix for the overdamped-pendulum bug has regressed.
        var settled = false;
        for (var i = 0; i < 20 * 60 && !settled; i++)
        {
            engine.Step(1.0 / 60.0);
            settled = engine.IsAtRest;
        }

        Assert.True(settled, "Pendulum never settled back to rest within 20 simulated seconds.");
    }

    [Fact]
    public void Grab_PullsBobTowardCursorDirectionOverTime()
    {
        var engine = NewEngine();

        // Cursor held out to the right of the anchor - Theta should trend positive (toward it)
        // rather than staying at 0 or drifting negative.
        engine.BeginGrab(200, 50);
        for (var i = 0; i < 60; i++)
        {
            engine.UpdateGrabTarget(200, 50);
            engine.Step(1.0 / 60.0);
        }

        Assert.True(engine.Theta > 0.3, $"Expected Theta to swing toward the cursor, was {engine.Theta}");
    }

    [Fact]
    public void Bounce_AppliesInwardRadiusImpulse()
    {
        var engine = NewEngine();
        var before = engine.RadiusVelocity;

        engine.Bounce();

        Assert.True(engine.RadiusVelocity < before, "Bounce() should pull the radius inward (negative velocity impulse).");
    }

    [Fact]
    public void Spike_AppliesNonZeroSpinImpulse()
    {
        var engine = NewEngine();
        Assert.Equal(0, engine.SpinVelocity);

        engine.Spike();

        Assert.NotEqual(0, engine.SpinVelocity);
    }

    [Fact]
    public void Disabled_WhileNotGrabbed_SnapsToRestPose()
    {
        var settings = new CharmPhysicsSettings { StringLength = 120 };
        var engine = NewEngine(settings);
        engine.Nudge(0.8);
        engine.Step(1.0 / 60.0);

        engine.Enabled = false;
        engine.Step(1.0 / 60.0);

        Assert.Equal(0, engine.Theta);
        Assert.Equal(settings.StringLength, engine.Radius);
        Assert.Equal(0, engine.ThetaVelocity);
    }

    [Fact]
    public void Disabled_WhileGrabbed_TracksCursorWithNoMomentum()
    {
        var engine = NewEngine();
        engine.Enabled = false;

        engine.BeginGrab(0, 150); // straight down
        engine.Step(1.0 / 60.0);

        Assert.Equal(0, engine.Theta, precision: 6);
        Assert.Equal(0, engine.ThetaVelocity);
        Assert.Equal(0, engine.RadiusVelocity);
    }
}
