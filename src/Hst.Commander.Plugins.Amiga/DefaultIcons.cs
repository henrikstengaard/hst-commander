using Hst.Amiga.DataTypes.DiskObjects;
using Hst.Amiga.DataTypes.DiskObjects.ColorIcons;
using Hst.Imaging;

namespace Hst.Commander.Plugins.Amiga;

/// <summary>
/// Default icons of entries without a .info file in the style of the Workbench default icons, as Hst.Amiga's default
/// disk objects only have an empty 1x1 image. Each icon has a planar image drawn with the four Workbench 3.1 pens (0
/// background (transparent), 1 black, 2 white and 3 blue) and a color icon (GlowIcon) of the same shape drawn with
/// shades, whose selected image has a glow around it, so the icons look right on any Workbench.
/// </summary>
internal static class DefaultIcons
{
    /// <summary>Pixels of glow around the color images, drawn in the selected image.</summary>
    private const int GlowMargin = 1;

    /// <summary>Flag of a color icon drawn without a frame around it.</summary>
    private const int FramelessFlag = 1;

    /// <summary>The pens of the planar image, the Workbench 3.1 pens.</summary>
    private static readonly Pens PlanarPens = new(Transparent: 0, Outline: 1, Light: 2, Dark: 1, DrawerTop: 2,
        DrawerFront: 0, Accent: 3, Page: 2, PageShade: 2, Body: 2, DiskBody: 3, Light2: 2);

    /// <summary>The pens of the color image, indexes in <see cref="ColorPalette" />.</summary>
    private static readonly Pens ColorPens = new(Transparent: 0, Outline: 1, Light: 2, Dark: 3, DrawerTop: 4,
        DrawerFront: 5, Accent: 6, Page: 2, PageShade: 7, Body: 8, DiskBody: 9, Light2: 10);

    private const byte GlowPen = 11;

    private static readonly Color[] ColorPalette =
    [
        new(0, 0, 0, 0), // transparent
        new(0, 0, 0), // outline
        new(255, 255, 255), // light
        new(85, 85, 85), // dark
        new(255, 229, 153), // drawer top
        new(232, 163, 60), // drawer front
        new(79, 127, 201), // accent
        new(200, 200, 200), // page shade
        new(221, 221, 221), // tool body
        new(154, 154, 154), // disk body
        new(51, 204, 51), // activity light
        new(255, 238, 119) // glow
    ];

    /// <summary>Default drawer icon for directories.</summary>
    public static AmigaIcon CreateDrawer() => Create(DiskObjectHelper.CreateDrawerInfo(), 44, 22, DrawDrawer);

    /// <summary>Default project icon for files.</summary>
    public static AmigaIcon CreateProject() => Create(DiskObjectHelper.CreateProjectInfo(), 24, 28, DrawProject);

    /// <summary>Default disk icon for volumes, e.g. the partitions of an RDB disk.</summary>
    public static AmigaIcon CreateDisk() => Create(DiskObjectHelper.CreateDiskInfo(), 40, 18, DrawDisk);

    /// <summary>Default tool icon for Amiga executables.</summary>
    public static AmigaIcon CreateTool() => Create(DiskObjectHelper.CreateToolInfo(), 32, 24, DrawTool);

    private static AmigaIcon Create(DiskObject diskObject, int width, int height, Action<PixelCanvas, Pens> draw)
    {
        var planarCanvas = new PixelCanvas(width, height, 0);
        draw(planarCanvas, PlanarPens);
        DiskObjectHelper.SetFirstImage(diskObject,
            ImageDataEncoder.Encode(planarCanvas.ToImage(AmigaOsPalette.FourColors()), 2));

        // the color images have room for the glow of the selected image
        var colorCanvas = new PixelCanvas(width + 2 * GlowMargin, height + 2 * GlowMargin, GlowMargin);
        draw(colorCanvas, ColorPens);
        var palette = new Palette(ColorPalette, 0);
        var image = colorCanvas.ToImage(palette);
        colorCanvas.AddGlow(GlowPen);
        var colorIcon = new ColorIcon { Flags = FramelessFlag };
        ColorIconHelper.SetFirstImage(colorIcon, image);
        ColorIconHelper.SetSecondImage(colorIcon, colorCanvas.ToImage(palette));
        return new AmigaIcon { Kind = AmigaIcon.IconKind.Normal, DiskObject = diskObject, ColorIcon = colorIcon };
    }

    /// <summary>A drawer seen from the front, with its top and a handle.</summary>
    private static void DrawDrawer(PixelCanvas canvas, Pens pens)
    {
        // top, widening towards the front
        for (var row = 0; row < 5; row++)
        {
            var left = 4 - row;
            var right = 39 + row;
            canvas.Line(left, row, right, row, row == 0 ? pens.Outline : pens.DrawerTop);
            canvas.Set(left, row, pens.Outline);
            canvas.Set(right, row, pens.Outline);
        }

        // front with a bevel, light top and left, dark bottom and right
        canvas.Fill(1, 6, 42, 20, pens.DrawerFront);
        canvas.Rectangle(0, 5, 43, 21, pens.Outline);
        canvas.Line(1, 6, 42, 6, pens.Light);
        canvas.Line(1, 6, 1, 20, pens.Light);
        canvas.Line(2, 20, 42, 20, pens.Dark);
        canvas.Line(42, 7, 42, 20, pens.Dark);
        // handle
        canvas.Rectangle(16, 10, 27, 14, pens.Outline);
        canvas.Fill(17, 11, 26, 13, pens.Accent);
    }

    /// <summary>A page with a folded corner and lines of text.</summary>
    private static void DrawProject(PixelCanvas canvas, Pens pens)
    {
        const int width = 24;
        const int height = 28;
        const int fold = 6;
        const int foldLeft = width - 1 - fold;
        canvas.Fill(1, 1, width - 2, height - 2, pens.Page);
        // the folded corner is cut off above the diagonal and shaded below it
        for (var row = 0; row < fold; row++)
        {
            canvas.Line(foldLeft + row + 1, row, width - 1, row, pens.Transparent);
            canvas.Line(foldLeft + 1, row + 1, foldLeft + row, row + 1, pens.PageShade);
        }

        canvas.Line(0, 0, foldLeft, 0, pens.Outline);
        canvas.Line(0, 0, 0, height - 1, pens.Outline);
        canvas.Line(0, height - 1, width - 1, height - 1, pens.Outline);
        canvas.Line(width - 1, fold, width - 1, height - 1, pens.Outline);
        canvas.Line(foldLeft, 0, width - 1, fold, pens.Outline);
        canvas.Line(foldLeft, 0, foldLeft, fold, pens.Outline);
        canvas.Line(foldLeft, fold, width - 1, fold, pens.Outline);
        for (var row = 9; row < height - 4; row += 4)
        {
            canvas.Line(4, row, row % 8 == 1 ? width - 9 : width - 5, row, pens.Accent);
        }
    }

    /// <summary>A hard disk drive seen from the front, with a bevel, a slot and an activity light.</summary>
    private static void DrawDisk(PixelCanvas canvas, Pens pens)
    {
        canvas.Rectangle(0, 0, 39, 17, pens.Outline);
        canvas.Fill(2, 2, 37, 15, pens.DiskBody);
        canvas.Line(1, 1, 38, 1, pens.Light);
        canvas.Line(1, 1, 1, 16, pens.Light);
        canvas.Line(2, 16, 38, 16, pens.Dark);
        canvas.Line(38, 2, 38, 16, pens.Dark);
        // slot and activity light
        canvas.Line(6, 8, 25, 8, pens.Outline);
        canvas.Line(6, 9, 25, 9, pens.Light);
        canvas.Fill(31, 11, 34, 12, pens.Light2);
    }

    /// <summary>A window with a title bar and a close gadget.</summary>
    private static void DrawTool(PixelCanvas canvas, Pens pens)
    {
        canvas.Rectangle(0, 0, 31, 23, pens.Outline);
        canvas.Fill(1, 1, 30, 5, pens.Accent);
        canvas.Line(1, 6, 30, 6, pens.Outline);
        canvas.Fill(1, 7, 30, 22, pens.Body);
        canvas.Rectangle(2, 2, 5, 4, pens.Outline);
        canvas.Fill(3, 3, 4, 3, pens.Light);
    }

    /// <summary>Pens drawing the parts of the icons, in the planar or the color image.</summary>
    private sealed record Pens(byte Transparent, byte Outline, byte Light, byte Dark, byte DrawerTop, byte DrawerFront,
        byte Accent, byte Page, byte PageShade, byte Body, byte DiskBody, byte Light2);

    /// <summary>Pixels of an image drawn with pens, offset by a margin.</summary>
    private sealed class PixelCanvas(int width, int height, int margin)
    {
        private readonly byte[] _pixels = new byte[width * height];

        public void Set(int x, int y, byte pen)
        {
            x += margin;
            y += margin;
            if (x >= 0 && x < width && y >= 0 && y < height)
            {
                _pixels[y * width + x] = pen;
            }
        }

        /// <summary>Horizontal, vertical or diagonal line.</summary>
        public void Line(int x1, int y1, int x2, int y2, byte pen)
        {
            var steps = Math.Max(Math.Abs(x2 - x1), Math.Abs(y2 - y1));
            for (var step = 0; step <= steps; step++)
            {
                Set(x1 + (steps == 0 ? 0 : (x2 - x1) * step / steps), y1 + (steps == 0 ? 0 : (y2 - y1) * step / steps),
                    pen);
            }
        }

        public void Rectangle(int x1, int y1, int x2, int y2, byte pen)
        {
            Line(x1, y1, x2, y1, pen);
            Line(x1, y2, x2, y2, pen);
            Line(x1, y1, x1, y2, pen);
            Line(x2, y1, x2, y2, pen);
        }

        public void Fill(int x1, int y1, int x2, int y2, byte pen)
        {
            for (var y = y1; y <= y2; y++)
            {
                Line(x1, y, x2, y, pen);
            }
        }

        /// <summary>Draws a glow on the transparent pixels next to the drawn pixels, including diagonally.</summary>
        public void AddGlow(byte pen)
        {
            var drawn = _pixels.Select(pixel => pixel != 0).ToArray();
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (!drawn[y * width + x] && IsNextToDrawn(drawn, x, y))
                    {
                        _pixels[y * width + x] = pen;
                    }
                }
            }
        }

        private bool IsNextToDrawn(bool[] drawn, int x, int y)
        {
            for (var offsetY = -1; offsetY <= 1; offsetY++)
            {
                for (var offsetX = -1; offsetX <= 1; offsetX++)
                {
                    var otherX = x + offsetX;
                    var otherY = y + offsetY;
                    if (otherX >= 0 && otherX < width && otherY >= 0 && otherY < height &&
                        drawn[otherY * width + otherX])
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public Image ToImage(Palette palette) => new(width, height, 8, palette, _pixels.ToArray());
    }
}
