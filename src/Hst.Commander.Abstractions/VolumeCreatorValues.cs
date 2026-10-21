namespace Hst.Commander.Abstractions;

/// <summary>
/// Values entered for the <see cref="IVolumeCreator.Fields" /> when creating a volume, looked up by field name:
/// text fields are strings (trimmed, empty when not entered), numbers are longs (null when not entered) and check boxes
/// are booleans.
/// </summary>
public sealed class VolumeCreatorValues(IReadOnlyDictionary<string, object?> values)
{
    /// <summary>Values by field name.</summary>
    public IReadOnlyDictionary<string, object?> Values { get; } = values ?? throw new ArgumentNullException(nameof(values));

    /// <summary>Text of a text field, empty when not entered or not a field.</summary>
    public string GetText(string name) => Values.GetValueOrDefault(name) as string ?? string.Empty;

    /// <summary>Number of a number field, null when not entered or not a field.</summary>
    public long? GetNumber(string name) => Values.GetValueOrDefault(name) as long?;

    /// <summary>True when a check box is checked, false when unchecked or not a field.</summary>
    public bool GetBoolean(string name) => Values.GetValueOrDefault(name) is true;
}
