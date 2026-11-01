using G3MLib.DataFile.Util;

namespace G3MLib.Tests;

public sealed class MaskBuilderTests
{
    [Fact]
    public void BuildSetsBitsFromAlphaMostSignificantBitFirst()
    {
        var pixels = new byte[10 * 2 * 4];
        foreach (var index in new[] { 0, 3, 9, 12 })
            pixels[index * 4 + 3] = 1;

        Assert.Equal([0b1001_0000, 0b0100_0000, 0b0010_0000, 0], MaskBuilder.Build(pixels, 10, 2, 10, 2));
    }

    [Fact]
    public void BuildLeavesAreaOutsideTheImageClear()
    {
        byte[] pixels = [0, 0, 0, 255];

        Assert.Equal([0b1000_0000, 0, 0, 0], MaskBuilder.Build(pixels, 1, 1, 9, 2));
    }

    [Fact]
    public void ReadWhiteSetsBitsOnlyForOpaqueWhite()
    {
        var pixels = new byte[3 * 4];
        pixels.AsSpan(0, 4).Fill(255);
        pixels.AsSpan(8, 4).Fill(255);
        pixels[11] = 128;

        Assert.Equal([0b1000_0000], MaskBuilder.ReadWhite(pixels, 3, 1));
    }
}
