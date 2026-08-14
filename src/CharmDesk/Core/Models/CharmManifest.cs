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

    [JsonIgnore]
    public string? SourceDirectory { get; set; }
}
