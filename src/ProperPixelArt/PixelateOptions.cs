namespace ProperPixelArt;

/// <summary>Options for the <see cref="Pixelator.Pixelate"/> pipeline.</summary>
public sealed record PixelateOptions
{
    /// <summary>
    /// Number of palette colors. <c>null</c> skips quantisation and preserves original colours.
    /// Tune this: too high → noisy colours; too low → lost detail.
    /// </summary>
    public int? NumColors { get; init; } = null;

    /// <summary>Up-scale factor applied to the source image before mesh detection.</summary>
    public int InitialUpscaleFactor { get; init; } = 2;

    /// <summary>Optional nearest-neighbour up-scale applied to the final result.</summary>
    public int? ScaleResult { get; init; } = null;

    /// <summary>
    /// When <c>true</c>, pixels matching the most common boundary colour are made transparent.
    /// </summary>
    public bool TransparentBackground { get; init; } = false;

    /// <summary>Optional directory for saving intermediate visualisation images.</summary>
    public string? IntermediateDir { get; init; } = null;

    /// <summary>
    /// Override auto-detected pixel width. <c>null</c> uses the auto-detection heuristic.
    /// </summary>
    public int? PixelWidth { get; init; } = null;
}
