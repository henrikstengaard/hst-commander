using System.Globalization;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Core;

/// <summary>
///     Input entered in the create dialog (F9) for creating a volume with <see cref="NavigationContext.CreateVolumeAsync" />:
///     the file name and the input of the creator's fields by name (text and numbers as entered text, check boxes as
///     booleans).
/// </summary>
public sealed record VolumeCreatorInput(string FileName, IReadOnlyDictionary<string, object?> Values);

/// <summary>
///     Checks the input entered in the create dialog (F9) before a volume is created with an
///     <see cref="IVolumeCreator" />, see <see cref="NavigationContext.CreateVolumeAsync" />.
/// </summary>
public static class VolumeCreation
{
    /// <summary>
    ///     File name of the volume created: the trimmed name with the creator's file extension added when it doesn't end
    ///     with it (case-insensitive), e.g. "disk" becomes "disk.adf".
    /// </summary>
    public static string GetFileName(IVolumeCreator creator, string name)
    {
        ArgumentNullException.ThrowIfNull(creator);
        ArgumentNullException.ThrowIfNull(name);

        name = name.Trim();
        return name.Length == 0 || string.IsNullOrEmpty(creator.FileExtension) ||
               name.EndsWith(creator.FileExtension, StringComparison.OrdinalIgnoreCase)
            ? name
            : name + creator.FileExtension;
    }

    /// <summary>
    ///     Values of the creator's fields from the input entered by the user, by field name: text and numbers as entered
    ///     text, check boxes as booleans. Text is trimmed and numbers are parsed; fields without input get their default
    ///     value. Throws <see cref="IOException" /> when a required field isn't entered or a number is invalid.
    /// </summary>
    public static VolumeCreatorValues GetValues(IVolumeCreator creator, IReadOnlyDictionary<string, object?> input)
    {
        ArgumentNullException.ThrowIfNull(creator);
        ArgumentNullException.ThrowIfNull(input);

        var values = new Dictionary<string, object?>();
        foreach (var field in creator.Fields)
        {
            var hasInput = input.TryGetValue(field.Name, out var value);
            values[field.Name] = field.Type switch
            {
                VolumeCreatorFieldType.Text => GetText(field, hasInput ? value : field.DefaultValue),
                VolumeCreatorFieldType.Number => GetNumber(field, hasInput ? value : field.DefaultValue),
                VolumeCreatorFieldType.Boolean => (hasInput ? value : field.DefaultValue) is true,
                _ => throw new NotSupportedException($"Field type '{field.Type}' is not supported")
            };
        }

        return new VolumeCreatorValues(values);
    }

    /// <summary>Text shown initially in a text or number field: its default value, empty when none.</summary>
    public static string GetDefaultText(VolumeCreatorField field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return field.DefaultValue switch
        {
            null => string.Empty,
            IFormattable formattable => formattable.ToString(null, CultureInfo.CurrentCulture),
            var value => value.ToString() ?? string.Empty
        };
    }

    /// <summary>Label shown for a field, marking required fields with "*", e.g. "Volume name *".</summary>
    public static string GetLabel(VolumeCreatorField field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return field.IsRequired && field.Type != VolumeCreatorFieldType.Boolean ? field.Label + " *" : field.Label;
    }

    private static string GetText(VolumeCreatorField field, object? value)
    {
        var text = (value?.ToString() ?? string.Empty).Trim();
        if (field.IsRequired && text.Length == 0)
        {
            throw new IOException($"{field.Label} is required");
        }

        return text;
    }

    private static long? GetNumber(VolumeCreatorField field, object? value)
    {
        long? number = value switch
        {
            null => null,
            long longValue => longValue,
            int intValue => intValue,
            _ => ParseNumber(field, value.ToString() ?? string.Empty)
        };
        if (field.IsRequired && number == null)
        {
            throw new IOException($"{field.Label} is required");
        }

        return number;
    }

    private static long? ParseNumber(VolumeCreatorField field, string text)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            return null;
        }

        return long.TryParse(text, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.CurrentCulture,
            out var number)
            ? number
            : throw new IOException($"{field.Label} '{text}' is not a valid number");
    }
}
