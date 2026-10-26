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
    Boolean
}

/// <summary>
/// Input field the user enters when creating a volume with a <see cref="IVolumeCreator" />.
/// </summary>
/// <param name="Name">Name the value is looked up by in <see cref="VolumeCreatorValues" />, e.g. "VolumeName".</param>
/// <param name="Label">Label shown to the user, e.g. "Volume name".</param>
/// <param name="Type">Type of the field.</param>
/// <param name="IsRequired">
/// True when the user must enter a value: text that isn't empty or a number. Check boxes always have a value.
/// </param>
/// <param name="DefaultValue">
/// Value shown initially: a <see cref="string" /> for text, a <see cref="long" /> for numbers and a <see cref="bool" />
/// for check boxes. Null for an empty field or an unchecked check box.
/// </param>
public sealed record VolumeCreatorField(
    string Name,
    string Label,
    VolumeCreatorFieldType Type,
    bool IsRequired = false,
    object? DefaultValue = null);
