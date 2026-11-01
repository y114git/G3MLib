namespace G3MLib.DataFile.Util;

/// <summary>Converts between 4 byte per pixel images and 1 bit per pixel collision masks, one pass over spans.</summary>
public static class MaskBuilder
{
    /// <summary>Builds a mask from the alpha channel (fourth byte): a bit is set where alpha is above zero. Pixels outside the image stay clear.</summary>
    public static byte[] Build(ReadOnlySpan<byte> pixels, int width, int height, int maskWidth, int maskHeight)
    {
        var stride = (maskWidth + 7) / 8;
        var mask = new byte[stride * maskHeight];
        var rows = Math.Min(height, maskHeight);
        var columns = Math.Min(width, maskWidth);
        for (var y = 0; y < rows; y++)
        {
            var row = pixels.Slice(y * width * 4, width * 4);
            for (var x = 0; x < columns; x++)
            {
                if (row[x * 4 + 3] > 0)
                {
                    mask[y * stride + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                }
            }
        }
        return mask;
    }

    /// <summary>Reads a mask from a black and white image: a bit is set where all four bytes of a pixel are 255.</summary>
    public static byte[] ReadWhite(ReadOnlySpan<byte> pixels, int width, int height)
    {
        var stride = (width + 7) / 8;
        var mask = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            var row = pixels.Slice(y * width * 4, width * 4);
            for (var x = 0; x < width; x++)
            {
                var pixel = row.Slice(x * 4, 4);
                if (pixel[0] == 255 && pixel[1] == 255 && pixel[2] == 255 && pixel[3] == 255)
                {
                    mask[y * stride + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                }
            }
        }
        return mask;
    }
}
