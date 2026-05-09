namespace ProperPixelArt;

/// <summary>Sorted pixel-coordinate grid lines along one axis.</summary>
public sealed class Lines : List<int>
{
    public Lines() { }
    public Lines(IEnumerable<int> items) : base(items) { }
}

/// <summary>A 2-D pixel grid: X-axis (vertical) and Y-axis (horizontal) line positions.</summary>
public sealed record Mesh(Lines LinesX, Lines LinesY);

/// <summary>A detected line segment returned by the Hough transform.</summary>
public readonly record struct LineSegment(int X1, int Y1, int X2, int Y2);
