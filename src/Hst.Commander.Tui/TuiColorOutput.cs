using System.Text;
using TuiAttribute = Terminal.Gui.Drawing.Attribute;

namespace Hst.Commander.Tui;

/// <summary>Indexed output missing from Terminal.Gui 2.5; input and terminal lifecycle stay with its drivers.</summary>
public static class TuiColorOutput
{
    public static void Register()
    {
        RegisterFactory(DriverRegistry.Names.ANSI, () => new TuiAnsiComponentFactory());
        RegisterFactory(DriverRegistry.Names.DOTNET, () => new TuiNetComponentFactory());
    }

    private static void RegisterFactory(string name, Func<IComponentFactory> factory)
    {
        if (!DriverRegistry.TryGetDriver(name, out var descriptor) || descriptor == null)
        {
            throw new InvalidOperationException($"Terminal.Gui driver '{name}' is unavailable.");
        }

        DriverRegistry.Register(descriptor with { CreateFactory = factory });
    }

    public static void AppendIndexedColors(StringBuilder output, TuiAttribute attribute)
    {
        if (attribute.Foreground == Color.None)
        {
            EscSeqUtils.CSI_AppendResetForegroundColor(output);
        }
        else
        {
            output.Append(EscSeqUtils.CSI_SetForegroundColor256(TuiColors.GetIndex(attribute.Foreground)));
        }

        if (attribute.Background == Color.None)
        {
            EscSeqUtils.CSI_AppendResetBackgroundColor(output);
        }
        else
        {
            output.Append(EscSeqUtils.CSI_SetBackgroundColor256(TuiColors.GetIndex(attribute.Background)));
        }
    }
}

public class TuiAnsiOutput(AppModel appModel = AppModel.FullScreen) : AnsiOutput(appModel)
{
    protected override void AppendOrWriteAttribute(StringBuilder output, TuiAttribute attr, TextStyle redrawTextStyle)
    {
        if (!Force16Colors && !IsLegacyConsole && TuiColors.EffectiveMode == TuiColorMode.Colors256)
        {
            // Let the framework transition text styles without emitting RGB; then set the indexed colors.
            base.AppendOrWriteAttribute(output, new TuiAttribute(Color.None, Color.None, attr.Style), redrawTextStyle);
            TuiColorOutput.AppendIndexedColors(output, attr);
        }
        else
        {
            base.AppendOrWriteAttribute(output, attr, redrawTextStyle);
        }
    }
}

public class TuiNetOutput : NetOutput
{
    protected override void AppendOrWriteAttribute(StringBuilder output, TuiAttribute attr, TextStyle redrawTextStyle)
    {
        if (!Force16Colors && !IsLegacyConsole && TuiColors.EffectiveMode == TuiColorMode.Colors256)
        {
            base.AppendOrWriteAttribute(output, new TuiAttribute(Color.None, Color.None, attr.Style), redrawTextStyle);
            TuiColorOutput.AppendIndexedColors(output, attr);
        }
        else
        {
            base.AppendOrWriteAttribute(output, attr, redrawTextStyle);
        }
    }
}

internal sealed class TuiAnsiComponentFactory : AnsiComponentFactory
{
    public override IOutput CreateOutput() =>
        new AnsiComponentFactory(output: new TuiAnsiOutput(AppModel)).CreateOutput();
}

internal sealed class TuiNetComponentFactory : NetComponentFactory
{
    public override IOutput CreateOutput() => new TuiNetOutput();
}
