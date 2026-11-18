using System.Globalization;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Core;

/// <summary>
///     Input entered in the create dialog (F9) for creating a volume with <see cref="NavigationContext.CreateVolumeAsync" />:
///     the file name and the input of the creator's fields by name (text and numbers as entered text, check boxes as
///     booleans, choices as the chosen values).
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
    ///     text, check boxes as booleans and choices as the chosen values. Text is trimmed and numbers are parsed; fields
    ///     without input get their default value. Throws <see cref="IOException" /> when a required field that is enabled
    ///     isn't entered, a number is invalid or a choice isn't one of the field's choices.
    /// </summary>
    public static VolumeCreatorValues GetValues(IVolumeCreator creator, IReadOnlyDictionary<string, object?> input)
    {
        ArgumentNullException.ThrowIfNull(creator);
        ArgumentNullException.ThrowIfNull(input);

        object? GetInput(VolumeCreatorField field) =>
            input.TryGetValue(field.Name, out var value) ? value : field.DefaultValue;

        // check boxes first, as they enable other fields
        var values = new Dictionary<string, object?>();
        foreach (var field in creator.Fields.Where(field => field.Type == VolumeCreatorFieldType.Boolean))
        {
            values[field.Name] = GetInput(field) is true;
        }

        foreach (var field in creator.Fields.Where(field => field.Type != VolumeCreatorFieldType.Boolean))
        {
            var isEnabled = IsEnabled(field, name => values.GetValueOrDefault(name) is true);
            values[field.Name] = field.Type switch
            {
                VolumeCreatorFieldType.Text => GetText(field, GetInput(field), isEnabled),
                VolumeCreatorFieldType.Number => GetNumber(field, GetInput(field), isEnabled),
                VolumeCreatorFieldType.Choice => GetChoice(field, GetInput(field)),
                _ => throw new NotSupportedException($"Field type '{field.Type}' is not supported")
            };
        }

        return new VolumeCreatorValues(values);
    }

    /// <summary>
    ///     True when a field is enabled: it has no <see cref="VolumeCreatorField.EnabledByField" /> or that check box is
    ///     checked.
    /// </summary>
    /// <param name="field">Field to check.</param>
    /// <param name="isChecked">Returns true when the check box with a field name is checked.</param>
    public static bool IsEnabled(VolumeCreatorField field, Func<string, bool> isChecked)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(isChecked);

        return field.EnabledByField == null || isChecked(field.EnabledByField);
    }

    /// <summary>
    ///     Index of the choice shown initially in a choice field: the choice with the value, else the default value's
    ///     choice, else the first.
    /// </summary>
    public static int GetChoiceIndex(VolumeCreatorField field, object? value)
    {
        ArgumentNullException.ThrowIfNull(field);

        var choices = field.Choices ?? [];
        int IndexOf(object? choiceValue) => choiceValue is string text
            ? choices.ToList().FindIndex(choice => string.Equals(choice.Value, text, StringComparison.Ordinal))
            : -1;

        var index = IndexOf(value);
        if (index < 0)
        {
            index = IndexOf(field.DefaultValue);
        }

        return choices.Count == 0 ? -1 : Math.Max(index, 0);
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

        return field.IsRequired && field.Type is VolumeCreatorFieldType.Text or VolumeCreatorFieldType.Number
            ? field.Label + " *" : field.Label;
    }

    private static string GetText(VolumeCreatorField field, object? value, bool isEnabled)
    {
        var text = (value?.ToString() ?? string.Empty).Trim();
        if (isEnabled && field.IsRequired && text.Length == 0)
        {
            throw new IOException($"{field.Label} is required");
        }

        return text;
    }

    private static long? GetNumber(VolumeCreatorField field, object? value, bool isEnabled)
    {
        long? number = value switch
        {
            null => null,
            long longValue => longValue,
            int intValue => intValue,
            _ => ParseNumber(field, value.ToString() ?? string.Empty)
        };
        if (isEnabled && field.IsRequired && number == null)
        {
            throw new IOException($"{field.Label} is required");
        }

        return number;
    }

    private static string? GetChoice(VolumeCreatorField field, object? value)
    {
        var choices = field.Choices ?? [];
        if (value == null)
        {
            return choices.Count > 0 ? choices[GetChoiceIndex(field, null)].Value : null;
        }

        var text = value.ToString() ?? string.Empty;
        return choices.Any(choice => string.Equals(choice.Value, text, StringComparison.Ordinal))
            ? text
            : throw new IOException($"{field.Label} '{text}' is not a valid choice");
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
