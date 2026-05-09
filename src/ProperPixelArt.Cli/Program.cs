using System.CommandLine;
using ProperPixelArt;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

// ── Arguments & options ───────────────────────────────────────────────────────

var inputArg = new Argument<FileInfo>(
    "input",
    "Input image file (PNG, JPEG, …)");

var outputOpt = new Option<string?>(
    new[] { "--output", "-o" },
    "Output file path or directory (defaults to <input-stem>_pixelated.png)");

var colorsOpt = new Option<int?>(
    new[] { "--colors", "-c" },
    "Number of palette colours (omit to skip quantisation)");

var scaleResultOpt = new Option<int?>(
    new[] { "--scale-result", "-s" },
    "Nearest-neighbour up-scale factor applied to the final result");

var transparentOpt = new Option<bool>(
    new[] { "--transparent", "-t" },
    "Make the background transparent");

var pixelWidthOpt = new Option<int?>(
    new[] { "--pixel-width", "-w" },
    "Override auto-detected pixel width");

var upscaleOpt = new Option<int>(
    new[] { "--initial-upscale", "-u" },
    () => 2,
    "Initial up-scale factor for mesh detection (default: 2)");

var intermediateDirOpt = new Option<string?>(
    "--intermediate-dir",
    "Directory to save intermediate debug images");

// ── Root command ──────────────────────────────────────────────────────────────

var root = new RootCommand(
    "proper-pixel-art — Convert noisy AI-generated pixel art to true resolution.")
{
    inputArg,
    outputOpt,
    colorsOpt,
    scaleResultOpt,
    transparentOpt,
    pixelWidthOpt,
    upscaleOpt,
    intermediateDirOpt,
};

root.SetHandler(
    (input, output, colors, scaleResult, transparent, pixelWidth, upscale, intermediateDir) =>
    {
        if (!input.Exists)
        {
            Console.Error.WriteLine($"Input file not found: {input.FullName}");
            return;
        }

        Console.WriteLine($"Loading {input.FullName} …");
        using var image = Image.Load<Rgba32>(input.FullName);

        var opts = new PixelateOptions
        {
            NumColors            = colors,
            InitialUpscaleFactor = upscale,
            ScaleResult          = scaleResult,
            TransparentBackground = transparent,
            PixelWidth           = pixelWidth,
            IntermediateDir      = intermediateDir,
        };

        Console.WriteLine("Pixelating …");
        using var result = Pixelator.Pixelate(image, opts);

        string outPath = ResolveOutputPath(input.FullName, output);
        string? outDir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(outDir))
            Directory.CreateDirectory(outDir);

        result.SaveAsPng(outPath);
        Console.WriteLine($"Saved → {outPath}  ({result.Width}×{result.Height} px)");
    },
    inputArg, outputOpt, colorsOpt, scaleResultOpt, transparentOpt,
    pixelWidthOpt, upscaleOpt, intermediateDirOpt);

await root.InvokeAsync(args);

// ── Helpers ───────────────────────────────────────────────────────────────────

static string ResolveOutputPath(string inputPath, string? userOutput)
{
    if (userOutput is null)
    {
        string dir  = Path.GetDirectoryName(inputPath) ?? ".";
        string stem = Path.GetFileNameWithoutExtension(inputPath);
        return Path.Combine(dir, $"{stem}_pixelated.png");
    }

    if (Directory.Exists(userOutput) ||
        userOutput.EndsWith(Path.DirectorySeparatorChar) ||
        userOutput.EndsWith(Path.AltDirectorySeparatorChar))
    {
        string stem = Path.GetFileNameWithoutExtension(inputPath);
        return Path.Combine(userOutput, $"{stem}_pixelated.png");
    }

    return userOutput;
}
