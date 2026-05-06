using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ProperPixelArt;

public static class ImageUtils
{
    /// <summary>
    /// Crop <paramref name="numPixels"/> pixels from every edge.
    /// Removes border artefacts sometimes added by AI image generators.
    /// </summary>
    public static Image<Rgba32> CropBorder(Image<Rgba32> image, int numPixels = 1)
    {
        var rect = new Rectangle(
            numPixels, numPixels,
            image.Width  - 2 * numPixels,
            image.Height - 2 * numPixels);
        return image.Clone(ctx => ctx.Crop(rect));
    }

    /// <summary>Nearest-neighbour scale-up preserving hard pixel edges.</summary>
    public static Image<Rgba32> ScaleImage(Image<Rgba32> image, int scale)
    {
        if (scale == 1) return image.Clone();
        return image.Clone(ctx =>
            ctx.Resize(image.Width * scale, image.Height * scale,
                KnownResamplers.NearestNeighbor));
    }

    /// <summary>Draw red mesh grid lines over the image for debugging.</summary>
    public static Image<Rgba32> OverlayGridLines(
        Image<Rgba32> image, Mesh mesh,
        Color? lineColor = null, float lineWidth = 1f)
    {
        var color = lineColor ?? Color.Red;
        var pen = Pens.Solid(color, lineWidth);
        int w = image.Width, h = image.Height;

        return image.Clone(ctx =>
        {
            foreach (int x in mesh.LinesX)
                ctx.DrawLine(pen, new PointF(x, 0), new PointF(x, h));
            foreach (int y in mesh.LinesY)
                ctx.DrawLine(pen, new PointF(0, y), new PointF(w, y));
        });
    }

    /// <summary>Convert an Rgba32 image to a flat grayscale byte array (luminance).</summary>
    public static byte[] ToGrayscaleBytes(Image<Rgba32> image)
    {
        int w = image.Width, h = image.Height;
        var result = new byte[w * h];
        image.ProcessPixelRows(acc =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = acc.GetRowSpan(y);
                int offset = y * w;
                for (int x = 0; x < w; x++)
                {
                    ref readonly var p = ref row[x];
                    result[offset + x] = (byte)(0.299f * p.R + 0.587f * p.G + 0.114f * p.B);
                }
            }
        });
        return result;
    }

    /// <summary>
    /// Return an Rgba32 image built from a flat RGBA byte buffer
    /// (R,G,B,A per pixel, row-major).
    /// </summary>
    public static Image<Rgba32> FromRgbaBytes(byte[] rgba, int width, int height)
    {
        var img = new Image<Rgba32>(width, height);
        img.ProcessPixelRows(acc =>
        {
            for (int y = 0; y < height; y++)
            {
                var row = acc.GetRowSpan(y);
                int rowOffset = y * width * 4;
                for (int x = 0; x < width; x++)
                {
                    int px = rowOffset + x * 4;
                    row[x] = new Rgba32(rgba[px], rgba[px + 1], rgba[px + 2], rgba[px + 3]);
                }
            }
        });
        return img;
    }

    /// <summary>
    /// Copy the RGBA pixel data of an image into a flat byte array
    /// (R,G,B,A per pixel, row-major).
    /// </summary>
    public static byte[] ToRgbaBytes(Image<Rgba32> image)
    {
        int w = image.Width, h = image.Height;
        var result = new byte[w * h * 4];
        image.ProcessPixelRows(acc =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = acc.GetRowSpan(y);
                int offset = y * w * 4;
                for (int x = 0; x < w; x++)
                {
                    ref readonly var p = ref row[x];
                    result[offset + x * 4]     = p.R;
                    result[offset + x * 4 + 1] = p.G;
                    result[offset + x * 4 + 2] = p.B;
                    result[offset + x * 4 + 3] = p.A;
                }
            }
        });
        return result;
    }

    /// <summary>
    /// Copy the RGB pixel data of an image into a flat byte array
    /// (R,G,B per pixel, row-major), ignoring alpha.
    /// </summary>
    public static byte[] ToRgbBytes(Image<Rgba32> image)
    {
        int w = image.Width, h = image.Height;
        var result = new byte[w * h * 3];
        image.ProcessPixelRows(acc =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = acc.GetRowSpan(y);
                int offset = y * w * 3;
                for (int x = 0; x < w; x++)
                {
                    ref readonly var p = ref row[x];
                    result[offset + x * 3]     = p.R;
                    result[offset + x * 3 + 1] = p.G;
                    result[offset + x * 3 + 2] = p.B;
                }
            }
        });
        return result;
    }
}
