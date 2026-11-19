namespace Hst.Commander.Abstractions;

/// <summary>
/// Type of an input field of a <see cref="IVolumeCreator" />, deciding how it is entered and the type of its value in
/// <see cref="VolumeCreatorValues" />.
/// </summary>
public enum VolumeCreatorFieldType
{
    /// <summary>Text entered in a text field, a <see cref="string" /> value.</summary>
    Text,

    /// <summary>Whole number entered in a text field, a <see cref="long" /> value.</summary>
    Number,

    /// <summary>Check box, a <see cref="bool" /> value.</summary>
    Boolean,

    /// <summary>
    /// Size entered as a number with a unit chosen next to it (B, KB, MB or GB, multiples of 1024), a
    /// <see cref="long" /> value in bytes. The default value is in bytes too and is shown in the largest unit it is a
    /// whole number of (GB when there is no default value).
    /// </summary>
    Size,

    /// <summary>
    /// Drop-down list of <see cref="VolumeCreatorField.Choices" />, the <see cref="string" />
    /// <see cref="VolumeCreatorChoice.Value" /> of the chosen one.
    /// </summary>
    Choice
}

/// <summary>
/// Choice of a <see cref="VolumeCreatorFieldType.Choice" /> field.
/// </summary>
/// <param name="Value">Value of the field when chosen, e.g. "DOS3".</param>
/// <param name="Label">Label shown to the user, e.g. "DOS3 (FFS, international)".</param>
public sealed record VolumeCreatorChoice(string Value, string Label);

/// <summary>
/// Input field the user enters when creating a volume with a <see cref="IVolumeCreator" />.
/// </summary>
/// <param name="Name">Name the value is looked up by in <see cref="VolumeCreatorValues" />, e.g. "VolumeName".</param>
/// <param name="Label">Label shown to the user, e.g. "Volume name".</param>
/// <param name="Type">Type of the field.</param>
/// <param name="IsRequired">
/// True when the user must enter a value: text that isn't empty, a number or a size. Check boxes and choices always have a
/// value.
/// </param>
/// <param name="DefaultValue">
/// Value shown initially: a <see cref="string" /> for text, a <see cref="long" /> for numbers and sizes (in bytes), a <see cref="bool" />
/// for check boxes and the <see cref="VolumeCreatorChoice.Value" /> for choices. Null for an empty field, an unchecked
/// check box or the first choice.
/// </param>
/// <param name="Choices">Choices of a <see cref="VolumeCreatorFieldType.Choice" /> field, in the order shown.</param>
/// <param name="EnabledByField">
/// Name of a <see cref="VolumeCreatorFieldType.Boolean" /> field enabling this field when checked, e.g. a file system
/// only chosen when formatting. Null when the field is always enabled. A disabled required field doesn't have to be
/// entered.
/// </param>
public sealed record VolumeCreatorField(
    string Name,
    string Label,
    VolumeCreatorFieldType Type,
    bool IsRequired = false,
    object? DefaultValue = null,
    IReadOnlyList<VolumeCreatorChoice>? Choices = null,
    string? EnabledByField = null);
