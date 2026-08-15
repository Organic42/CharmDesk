using System.Text.Json.Serialization;

namespace CharmDesk.Core.Models;

/// <summary>Physics tuning for a single charm, all values overridable per-package.</summary>
public sealed class CharmPhysicsSettings
{
    public double StringLength { get; set; } = 120;
    public double Gravity { get; set; } = 980;
    public double Damping { get; set; } = 0.92;
    public double Stiffness { get; set; } = 0.15;
    public double Mass { get; set; } = 1.0;
}

/// <summary>How a charm reacts to being clicked/double-clicked/hovered - a per-charm
/// "personality" knob, data-driven rather than a bespoke behavior class per charm.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReactionStyle
{
    Default,
    Playful,
    Gentle,
    Dramatic,
}

/// <summary>1:1 model of a charm package's manifest.json.</summary>
public sealed class CharmManifest
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "Uncategorized";
    public string Image { get; set; } = "charm.png";
    public string Thumbnail { get; set; } = "thumbnail.png";
    public bool Enabled { get; set; } = true;
    public double DisplayScale { get; set; } = 1.0;
    public ReactionStyle ReactionStyle { get; set; } = ReactionStyle.Default;
    public CharmPhysicsSettings Physics { get; set; } = new();

    /// <summary>Present only for charms whose art is a blank display face with live content
    /// rendered on top - e.g. Timekeeper. Absent for every ordinary static-image charm.</summary>
    public ClockFaceSettings? ClockFace { get; set; }

    [JsonIgnore]
    public string? SourceDirectory { get; set; }
}

/// <summary>Optional live-rendered overlay for a charm - currently just a digital time readout,
/// with room to add an analog hands layer later without touching charms that don't use it.</summary>
public sealed class ClockFaceSettings
{
    public ClockDigitalSettings? Digital { get; set; }
}

/// <summary>Placement and styling for a live digital time readout, in the charm's own source
/// image pixel coordinates (top-left origin, unscaled). Scaled at render time by the same
/// factor the charm image itself is scaled by, so it stays correctly positioned at any
/// DisplayScale or Charm Scale setting.</summary>
public sealed class ClockDigitalSettings
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool ShowDate { get; set; } = true;
    public string TimeColor { get; set; } = "#5FD4FF";
    public string DateColor { get; set; } = "#3E8FB0";
}
