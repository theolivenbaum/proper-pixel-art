using ProperPixelArt;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace ProperPixelArt.Tests;

// Allow tuple-style deconstruction of Rgba32 in tests.
file static class Rgba32TestExtensions
{
    public static void Deconstruct(this Rgba32 p, out byte r, out byte g, out byte b, out byte a)
    {
        r = p.R; g = p.G; b = p.B; a = p.A;
    }
}

/// <summary>
/// Tests for <see cref="ColorProcessor"/> — ported from the Python test suite.
/// </summary>
public class ColorProcessorTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>Build a flat RGB byte array for a solid-colour cell.</summary>
    private static byte[] SolidRgb(int width, int height, byte r, byte g, byte b)
    {
        var data = new byte[width * height * 3];
        for (int i = 0; i < width * height; i++)
        {
            data[i * 3]     = r;
            data[i * 3 + 1] = g;
            data[i * 3 + 2] = b;
        }
        return data;
    }

    /// <summary>Build a flat RGBA byte array with uniform colour and alpha.</summary>
    private static byte[] SolidRgba(int width, int height, byte r, byte g, byte b, byte a = 255)
    {
        var data = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            data[i * 4]     = r;
            data[i * 4 + 1] = g;
            data[i * 4 + 2] = b;
            data[i * 4 + 3] = a;
        }
        return data;
    }

    /// <summary>Build a flat RGBA byte array with a given per-pixel delegate.</summary>
    private static byte[] BuildRgba(int width, int height,
        Func<int, int, (byte r, byte g, byte b, byte a)> fn)
    {
        var data = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var (r, g, b, a) = fn(x, y);
                int i = (y * width + x) * 4;
                data[i] = r; data[i + 1] = g; data[i + 2] = b; data[i + 3] = a;
            }
        return data;
    }

    // ══ GetOpaqueCellColor ═════════════════════════════════════════════════════

    [Fact]
    public void GetOpaqueCellColor_ReturnsMostCommonColor()
    {
        // 60 red + 40 blue pixels (10×10)
        var cell = new byte[100 * 3];
        for (int i = 0;  i < 60; i++) { cell[i * 3] = 255; }
        for (int i = 60; i < 100; i++) { cell[i * 3 + 2] = 255; }

        var result = ColorProcessor.GetOpaqueCellColor(cell, 100);

        Assert.Equal(new Rgba32(255, 0, 0, 255), result);
    }

    [Fact]
    public void GetOpaqueCellColor_SingleColor()
    {
        var cell = SolidRgb(5, 5, 42, 84, 126);
        var result = ColorProcessor.GetOpaqueCellColor(cell, 25);
        Assert.Equal(new Rgba32(42, 84, 126, 255), result);
    }

    // ══ GetCellColorWithAlpha ══════════════════════════════════════════════════

    [Fact]
    public void GetCellColorWithAlpha_MajorityTransparent_ReturnsTransparent()
    {
        var rgb   = SolidRgb(10, 10, 200, 100, 50);
        var alpha = new byte[100];
        // Only 30 % opaque
        for (int i = 0; i < 30; i++) alpha[i] = 255;

        var result = ColorProcessor.GetCellColorWithAlpha(rgb, alpha, 100);

        Assert.Equal(new Rgba32(0, 0, 0, 0), result);
    }

    [Fact]
    public void GetCellColorWithAlpha_MajorityOpaque_ReturnsMostCommonColor()
    {
        // 70 % red, 30 % blue
        var rgb   = new byte[100 * 3];
        var alpha = new byte[100];
        for (int i = 0;  i < 70; i++) { rgb[i * 3] = 255; alpha[i] = 255; }
        for (int i = 70; i < 100; i++) { rgb[i * 3 + 2] = 255; }

        var result = ColorProcessor.GetCellColorWithAlpha(rgb, alpha, 100);

        Assert.Equal(new Rgba32(255, 0, 0, 255), result);
    }

    [Fact]
    public void GetCellColorWithAlpha_Exactly50PercentTransparent_ReturnsTransparent()
    {
        var rgb   = SolidRgb(10, 10, 100, 100, 100);
        var alpha = new byte[100];
        for (int i = 0; i < 50; i++) alpha[i] = 255;

        var result = ColorProcessor.GetCellColorWithAlpha(rgb, alpha, 100);

        Assert.Equal(new Rgba32(0, 0, 0, 0), result);
    }

    [Fact]
    public void GetCellColorWithAlpha_AllOpaque_ReturnsThatColor()
    {
        var rgb   = SolidRgb(5, 5, 42, 84, 126);
        var alpha = new byte[25];
        Array.Fill(alpha, (byte)255);

        var result = ColorProcessor.GetCellColorWithAlpha(rgb, alpha, 25);

        Assert.Equal(new Rgba32(42, 84, 126, 255), result);
    }

    [Fact]
    public void GetCellColorWithAlpha_ReturnsFrequentColor()
    {
        // 80 % one colour, 20 % another, all opaque
        var rgb   = new byte[100 * 3];
        var alpha = new byte[100];
        Array.Fill(alpha, (byte)255);
        for (int i = 0;  i < 80; i++) { rgb[i * 3] = 200; rgb[i * 3 + 1] = 50; rgb[i * 3 + 2] = 25; }
        for (int i = 80; i < 100; i++) { rgb[i * 3] = 100; rgb[i * 3 + 1] = 150; rgb[i * 3 + 2] = 75; }

        var result = ColorProcessor.GetCellColorWithAlpha(rgb, alpha, 100);

        Assert.Equal(new Rgba32(200, 50, 25, 255), result);
    }

    // ══ GetCellColorSkipQuantization ══════════════════════════════════════════

    [Fact]
    public void SkipQ_SingleColor()
    {
        var cell = SolidRgba(10, 10, 255, 0, 0);
        var r = ColorProcessor.GetCellColorSkipQuantization(cell, 100);
        Assert.Equal(new Rgba32(255, 0, 0, 255), r);
    }

    [Fact]
    public void SkipQ_UniformWithOutliers_FiltersOutliers()
    {
        var cell = BuildRgba(10, 10, (x, y) =>
        {
            // Two outlier pixels, rest uniform (200, 100, 50)
            if (x == 0 && y == 0) return (0,   0,   255, 255);
            if (x == 1 && y == 0) return (0,   255, 0,   255);
            return (200, 100, 50, 255);
        });

        var (r, g, b, a) = ColorProcessor.GetCellColorSkipQuantization(cell, 100);

        Assert.InRange(r, 190, 210);
        Assert.InRange(g,  90, 110);
        Assert.InRange(b,  40,  60);
        Assert.Equal(255, a);
    }

    [Fact]
    public void SkipQ_TwoColorGroups_ReturnsDominant()
    {
        var cell = new byte[100 * 4];
        for (int i = 0;  i < 70; i++) { cell[i * 4] = 255; cell[i * 4 + 3] = 255; } // red
        for (int i = 70; i < 100; i++) { cell[i * 4 + 2] = 255; cell[i * 4 + 3] = 255; } // blue

        var (r, g, b, a) = ColorProcessor.GetCellColorSkipQuantization(cell, 100);

        Assert.True(r > 200,  $"Expected red dominant, got ({r},{g},{b},{a})");
        Assert.True(b < 50,   $"Expected blue filtered, got ({r},{g},{b},{a})");
        Assert.Equal(255, a);
    }

    [Fact]
    public void SkipQ_NoisyImage_ReturnsStableColor()
    {
        var cell = BuildRgba(10, 10, (x, y) =>
        {
            if (y < 7) return ((byte)(250 + (x * y % 6)), (byte)((x + y) % 5), (byte)((x - y + 10) % 5), (byte)255);
            return ((byte)((x + y) % 5), (byte)((x - y + 10) % 5), (byte)(250 + (x * y % 6)), (byte)255);
        });

        var (r, g, b, a) = ColorProcessor.GetCellColorSkipQuantization(cell, 100);

        Assert.True(r > 200, $"Expected red dominant, got ({r},{g},{b},{a})");
        Assert.Equal(255, a);
    }

    [Fact]
    public void SkipQ_GradientReturnsValueInRange()
    {
        var cell = BuildRgba(10, 10, (x, y) =>
        {
            byte rv = (byte)Math.Clamp(50 + y * 15 + x, 0, 255);
            return (rv, (byte)0, (byte)0, (byte)255);
        });

        var (r, g, b, a) = ColorProcessor.GetCellColorSkipQuantization(cell, 100);

        Assert.InRange(r, 50, 200);
        Assert.Equal(255, a);
    }

    [Fact]
    public void SkipQ_SmallCell2x2_ReturnsValidResult()
    {
        var cell = new byte[]
        {
            255, 0, 0, 255,   255, 0, 0, 255,
            0,   0, 255, 255, 0,   255, 0, 255,
        };
        var (r, g, b, a) = ColorProcessor.GetCellColorSkipQuantization(cell, 4);

        Assert.InRange(r, 0, 255);
        Assert.InRange(g, 0, 255);
        Assert.InRange(b, 0, 255);
        Assert.Equal(255, a);
    }

    [Fact]
    public void SkipQ_SinglePixel_ReturnsThatPixel()
    {
        var cell = new byte[] { 128, 64, 32, 255 };
        var r = ColorProcessor.GetCellColorSkipQuantization(cell, 1);
        Assert.Equal(new Rgba32(128, 64, 32, 255), r);
    }

    [Fact]
    public void SkipQ_EmptyCell_ReturnsTransparent()
    {
        var r = ColorProcessor.GetCellColorSkipQuantization(Array.Empty<byte>(), 0);
        Assert.Equal(new Rgba32(0, 0, 0, 0), r);
    }

    [Fact]
    public void SkipQ_BinBoundaryHandled()
    {
        // Values 27–37, spanning the bin boundary at ~26 (grid2 offset)
        var cell = BuildRgba(10, 10, (x, y) =>
        {
            byte v = (byte)(27 + (x + y) % 11);
            return (v, v, v, (byte)255);
        });

        var (r, g, b, a) = ColorProcessor.GetCellColorSkipQuantization(cell, 100);

        Assert.InRange(r, 27, 37);
        Assert.Equal(r, g);
        Assert.Equal(r, b);
        Assert.Equal(255, a);
    }

    [Fact]
    public void SkipQ_DarkToGreenGradient_ReturnsGreenIsh()
    {
        var cell = BuildRgba(10, 10, (x, y) =>
        {
            int intensity = y * 10 + x;
            return (
                (byte)(20 + intensity / 10),
                (byte)(20 + intensity),
                (byte)(20 + intensity / 10),
                (byte)255
            );
        });

        var (r, g, b, a) = ColorProcessor.GetCellColorSkipQuantization(cell, 100);

        Assert.True(g > r + 10, $"Expected green tint, got ({r},{g},{b},{a})");
        Assert.True(g > b + 10, $"Expected green tint, got ({r},{g},{b},{a})");
        Assert.Equal(255, a);
    }

    [Fact]
    public void SkipQ_MajorityTransparent_ReturnsTransparent()
    {
        var cell = new byte[100 * 4]; // all zeroes = transparent
        for (int i = 0; i < 40; i++)
        {
            cell[i * 4]     = 100;
            cell[i * 4 + 1] = 100;
            cell[i * 4 + 2] = 100;
            cell[i * 4 + 3] = 255; // 40 % opaque
        }

        var r = ColorProcessor.GetCellColorSkipQuantization(cell, 100);

        Assert.Equal(new Rgba32(0, 0, 0, 0), r);
    }

    [Fact]
    public void SkipQ_MajorityOpaque_ReturnsOpaqueColor()
    {
        var cell = new byte[100 * 4];
        for (int i = 0; i < 60; i++)
        {
            cell[i * 4]     = 100;
            cell[i * 4 + 1] = 100;
            cell[i * 4 + 2] = 100;
            cell[i * 4 + 3] = 255;
        }

        var (r, g, b, a) = ColorProcessor.GetCellColorSkipQuantization(cell, 100);

        Assert.Equal(255, a);
        Assert.InRange(r, 90, 110);
    }

    [Fact]
    public void SkipQ_Exactly50PercentTransparent_ReturnsTransparent()
    {
        var cell = new byte[100 * 4];
        for (int i = 0; i < 50; i++)
        {
            cell[i * 4]     = 100;
            cell[i * 4 + 1] = 100;
            cell[i * 4 + 2] = 100;
            cell[i * 4 + 3] = 255;
        }

        var r = ColorProcessor.GetCellColorSkipQuantization(cell, 100);

        Assert.Equal(new Rgba32(0, 0, 0, 0), r);
    }

    // ══ DominantRgbByBinning ══════════════════════════════════════════════════

    [Fact]
    public void DominantRgbByBinning_SinglePixel()
    {
        var pixels = new List<(byte r, byte g, byte b)> { (200, 100, 50) };
        var (r, g, b) = ColorProcessor.DominantRgbByBinning(pixels);
        Assert.Equal((200, 100, 50), (r, g, b));
    }

    [Fact]
    public void DominantRgbByBinning_ClearDominant()
    {
        var pixels = new List<(byte r, byte g, byte b)>();
        // 90 red-ish pixels
        for (int i = 0; i < 90; i++) pixels.Add((200, 10, 10));
        // 10 blue pixels
        for (int i = 0; i < 10; i++) pixels.Add((10, 10, 200));

        var (r, g, b) = ColorProcessor.DominantRgbByBinning(pixels);
        Assert.True(r > 150, $"Expected red dominant, got ({r},{g},{b})");
    }
}
