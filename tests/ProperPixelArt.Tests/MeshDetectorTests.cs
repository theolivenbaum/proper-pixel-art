using ProperPixelArt;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace ProperPixelArt.Tests;

public class MeshDetectorTests
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

    [Fact]
    public void ComputeMesh_BlobImage_ReturnsNonTrivialMesh()
    {
        string blobPath = Path.Combine(AssetsPath(), "blob", "blob.png");
        using var img = Image.Load<Rgba32>(blobPath);

        var mesh = MeshDetector.ComputeMesh(img);

        Assert.True(mesh.LinesX.Count > 2,
            $"Expected >2 X lines, got {mesh.LinesX.Count}");
        Assert.True(mesh.LinesY.Count > 2,
            $"Expected >2 Y lines, got {mesh.LinesY.Count}");
    }

    [Fact]
    public void ClusterLines_RemovesNearDuplicates()
    {
        var lines = new List<int> { 0, 2, 3, 100, 101, 200 };
        var clustered = MeshDetector.ClusterLines(lines, threshold: 4);

        // 0,2,3 → one cluster; 100,101 → one cluster; 200 → one cluster
        Assert.Equal(3, clustered.Count);
    }

    [Fact]
    public void HomogenizeLines_FillsEvenSpacing()
    {
        // Two lines 40 px apart, pixel width 10 → should produce 5 intermediate sections
        var lines = new Lines { 0, 40 };
        var result = MeshDetector.HomogenizeLines(lines, pixelWidth: 10);

        // 40 px / 10 px = 4 cells → 5 boundary lines
        Assert.Equal(4, result.Count - 1);
    }

    [Fact]
    public void GetPixelWidth_ReturnsMedianGap()
    {
        // All gaps are 10 → pixel width should be 10
        var mesh = new Mesh(
            new Lines { 0, 10, 20, 30, 40 },
            new Lines { 0, 10, 20, 30 });

        int pw = MeshDetector.GetPixelWidth(mesh);

        Assert.Equal(10, pw);
    }
}
