using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Garethp.ModsOfMistriaInstallerLib.Utils;

public static class LutMerger
{
    public static Image<Rgba32>? Merge(Image<Rgba32>? existing, Image<Rgba32> incoming)
    {
        if (existing is not null && existing.Height != incoming.Height)
            return null;

        int height = incoming.Height;

        var columns = new List<Rgba32[]>();
        var seen    = new HashSet<string>();

        if (existing is not null)
            AppendUniqueColumns(existing, height, columns, seen);
        AppendUniqueColumns(incoming, height, columns, seen);

        var result = new Image<Rgba32>(columns.Count, height);
        for (int x = 0; x < columns.Count; x++)
        {
            var col = columns[x];
            for (int y = 0; y < height; y++)
                result[x, y] = col[y];
        }

        return result;
    }

    /// <summary>
    /// Lays the incoming LUT over the installed one column for column: column <c>x</c> of the result
    /// is the incoming column <c>x</c> where the incoming LUT has one, and the installed column
    /// otherwise. For a recolour, which has to change the palettes the game already indexes rather
    /// than add new ones; columns another mod appended past the incoming width survive.
    /// </summary>
    public static Image<Rgba32>? Overwrite(Image<Rgba32>? existing, Image<Rgba32> incoming)
    {
        if (existing is not null && existing.Height != incoming.Height)
            return null;

        int height = incoming.Height;
        int width  = Math.Max(existing?.Width ?? 0, incoming.Width);

        var result = new Image<Rgba32>(width, height);
        for (int x = 0; x < width; x++)
        {
            var source = x < incoming.Width ? incoming : existing!;
            for (int y = 0; y < height; y++)
                result[x, y] = source[x, y];
        }

        return result;
    }

    private static void AppendUniqueColumns(
        Image<Rgba32> image, int height, List<Rgba32[]> columns, HashSet<string> seen)
    {
        for (int x = 0; x < image.Width; x++)
        {
            var col = new Rgba32[height];
            for (int y = 0; y < height; y++)
                col[y] = image[x, y];

            if (seen.Add(ColumnKey(col)))
                columns.Add(col);
        }
    }

    private static string ColumnKey(Rgba32[] column) =>
        Convert.ToBase64String(MemoryMarshal.AsBytes(column.AsSpan()));
}
