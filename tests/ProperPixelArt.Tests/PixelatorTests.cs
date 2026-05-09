using ProperPixelArt;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace ProperPixelArt.Tests;

public class PixelatorTests
{
    private static string AssetsPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "assets")))
            dir = dir.Parent;
        return dir is not null
            ? Path.Combine(dir.FullName, "assets")
            : throw new DirectoryNotFoundException("Could not locate 'assets' directory");
    }

    public static IEnumerable<object[]> TestCases()
    {
        string assets = AssetsPath();
        yield return ["anchor",   16,  5, true,  Path.Combine(assets, "anchor",   "anchor.png")];
        yield return ["ash",      16,  5, false, Path.Combine(assets, "ash",      "ash.png")];
        yield return ["bat",      16,  5, true,  Path.Combine(assets, "bat",      "bat.png")];
        yield return ["blob",     16, 25, false, Path.Combine(assets, "blob",     "blob.png")];
        yield return ["demon",    64,  5, true,  Path.Combine(assets, "demon",    "demon.png")];
        yield return ["mountain", 64,  5, false, Path.Combine(assets, "mountain", "mountain.png")];
        // pumpkin: skip quantisation (numColors = null represented as 0 → handled below)
        yield return ["pumpkin",   0,  5, false, Path.Combine(assets, "pumpkin",  "pumpkin.png")];
    }

    [Theory]
    [MemberData(nameof(TestCases))]
    public void Pixelate_ProducesValidOutput(
        string name, int numColors, int scaleResult,
        bool transparent, string imagePath)
    {
        string outputDir = Path.Combine(
            Path.GetTempPath(), "ppa-tests", name);
        Directory.CreateDirectory(outputDir);

        using var img = Image.Load<Rgba32>(imagePath);

        var opts = new PixelateOptions
        {
            NumColors             = numColors == 0 ? null : numColors,
            ScaleResult           = scaleResult,
            TransparentBackground = transparent,
            IntermediateDir       = outputDir,
        };

        using var result = Pixelator.Pixelate(img, opts);

        string outPath = Path.Combine(outputDir, "result.png");
        result.SaveAsPng(outPath);

        Assert.True(File.Exists(outPath),  $"Output file not created for {name}");
        Assert.True(result.Width  > 0,     $"Invalid width for {name}");
        Assert.True(result.Height > 0,     $"Invalid height for {name}");
    }
}
