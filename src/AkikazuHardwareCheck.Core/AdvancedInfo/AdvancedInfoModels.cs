using System.Text.Json.Serialization;

namespace AkikazuHardwareCheck.Core.AdvancedInfo;

/// <summary>Why a field has no value. Kept separate so a missing sensor is never mistaken for a hardware fault.</summary>
public enum InfoState
{
    Available,
    Unsupported,
    PermissionDenied,
    InterfaceUnavailable,
    ReadFailed,
    NotCollected,
    NotImplemented,
}

/// <summary>Where a value came from. A value read from firmware is not the same as one calculated by this tool.</summary>
public enum InfoOrigin
{
    Firmware,
    Driver,
    Windows,
    Calculated,
    Inferred,
    Manual,
}

/// <summary>The interface and access requirements behind one field.</summary>
public sealed record InfoProvenance(
    string Source,
    string Api,
    bool RequiresAdministrator = false,
    bool RequiresDriver = false,
    bool IsRefreshable = false);

/// <summary>One property in the advanced view. RawValue keeps the unformatted value for export and diagnosis.</summary>
public sealed record InfoField(
    string Key,
    string Label,
    string? Value = null,
    string? RawValue = null,
    string? Unit = null,
    InfoState State = InfoState.Available,
    InfoOrigin Origin = InfoOrigin.Windows,
    InfoProvenance? Provenance = null,
    string? Note = null)
{
    [JsonIgnore]
    public bool HasValue => State == InfoState.Available && !string.IsNullOrWhiteSpace(Value);
}

public sealed record InfoGroup(string Key, string Label, IReadOnlyList<InfoField> Fields);

public sealed record InfoNode(string Key, string Label, IReadOnlyList<InfoGroup> Groups, IReadOnlyList<InfoNode>? Children = null)
{
    public IEnumerable<InfoNode> SelfAndDescendants()
    {
        yield return this;
        foreach (var child in Children ?? [])
        {
            foreach (var descendant in child.SelfAndDescendants())
            {
                yield return descendant;
            }
        }
    }

    public IEnumerable<InfoField> AllFields() => SelfAndDescendants().SelectMany(node => node.Groups).SelectMany(group => group.Fields);
}

/// <summary>One immutable snapshot. The advanced window never builds a second hardware inventory.</summary>
public sealed record AdvancedSnapshot(DateTimeOffset CapturedAt, string ToolVersion, IReadOnlyList<InfoNode> Roots)
{
    public IEnumerable<InfoField> AllFields() => Roots.SelectMany(root => root.AllFields());
}
