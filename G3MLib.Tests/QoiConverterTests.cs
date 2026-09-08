using System;
using System.IO;
using System.Text;
using G3MLib.DataFile.Util;
using Xunit;

namespace G3MLib.Tests;

public sealed class QoiConverterTests
{
    [Fact("QoiConverterTests.cs", 11)]
    public void QoiRoundTripPreservesRunsColorsAndSmallDeltas()
    {
        GMImage image = new(80, 1);
        Span<byte> pixels = image.GetRawImageData();
        SetPixel(pixels, 0, 0, 0, 0, 255);
        SetPixel(pixels, 1, 1, 1, 1, 255);
        SetPixel(pixels, 2, 17, 1, 1, 255);
        SetPixel(pixels, 3, 20, 4, 4, 255);
        SetPixel(pixels, 4, 20, 4, 4, 254);
        SetPixel(pixels, 5, 0, 0, 0, 255);
        for (int index = 6; index < 80; index++)
        {
            SetPixel(pixels, index, 0, 0, 0, 255);
        }
        byte[] expected = pixels.ToArray();

        byte[] encoded = QoiConverter.GetArrayFromImage(image);
        using MemoryStream stream = new(encoded);
        GMImage decodedFromStream = QoiConverter.GetImageFromStream(stream);
        GMImage decodedFromSpan = QoiConverter.GetImageFromSpan(encoded, out int length);

        Assert.Equal(encoded.Length, length);
        Assert.Equal(expected, decodedFromStream.GetRawImageData().ToArray());
        Assert.Equal(expected, decodedFromSpan.GetRawImageData().ToArray());
        Assert.Equal(expected, GMImage.FromQoi(encoded).ConvertToRawBgra().GetRawImageData().ToArray());
        Assert.Equal(GMImage.ImageFormat.Qoi, image.ConvertToQoi().Format);
    }

    [Fact("QoiConverterTests.cs", 42)]
    public void QoiReaderRejectsTruncatedInvalidAndInconsistentInput()
    {
        Assert.Throws<IOException>(() => QoiConverter.GetImageFromSpan(new byte[QoiConverter.HeaderSize - 1]));

        byte[] invalidMagic = new byte[QoiConverter.HeaderSize];
        Assert.Throws<InvalidDataException>(() => QoiConverter.GetImageFromSpan(invalidMagic));

        byte[] invalidLength = "fioq\x01\x00\x01\x00\x01\x00\x00\x00"u8.ToArray();
        Assert.Throws<IOException>(() => QoiConverter.GetImageFromSpan(invalidLength));
    }

    [Fact("QoiConverterTests.cs", 55)]
    public void ImageFormatConversionsPreservePixelsAndSerializedBytes()
    {
        GMImage source = new(2, 2);
        Span<byte> pixels = source.GetRawImageData();
        SetPixel(pixels, 0, 1, 2, 3, 4);
        SetPixel(pixels, 1, 5, 6, 7, 8);
        SetPixel(pixels, 2, 9, 10, 11, 12);
        SetPixel(pixels, 3, 13, 14, 15, 16);
        byte[] expected = pixels.ToArray();

        GMImage png = source.ConvertToPng();
        GMImage bz2Qoi = source.ConvertToBz2Qoi();
        Assert.Equal(GMImage.ImageFormat.Png, png.Format);
        Assert.Equal(GMImage.ImageFormat.Bz2Qoi, bz2Qoi.Format);
        Assert.Equal(expected, png.ConvertToRawBgra().GetRawImageData().ToArray());
        Assert.Equal(expected, bz2Qoi.ConvertToRawBgra().GetRawImageData().ToArray());

        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true))
        {
            bz2Qoi.WriteToBinaryWriter(writer, gm2022_5: true);
        }
        Assert.Equal(bz2Qoi.ToSpan(gm2022_5: true).ToArray(), stream.ToArray());

        using MemoryStream pngStream = new();
        png.SavePng(pngStream);
        GMImage parsedPng = GMImage.FromPng(pngStream.ToArray(), verifyHeader: true);
        Assert.Equal(expected, parsedPng.ConvertToRawBgra().GetRawImageData().ToArray());
    }

    [Fact("QoiConverterTests.cs", 86)]
    public void BinaryImageReaderRecognizesGameMakerStoredFormats()
    {
        GMImage source = new(2, 2);
        GMImage png = source.ConvertToPng();
        GMImage qoi = source.ConvertToQoi();
        GMImage bz2Qoi = source.ConvertToBz2Qoi();
        GMImage dds = source.ConvertToDds();

        Assert.Equal(GMImage.ImageFormat.Png, ReadStoredImage(png.ToSpan().ToArray()).Format);
        Assert.Equal(GMImage.ImageFormat.Qoi, ReadStoredImage(qoi.ToSpan().ToArray()).Format);
        Assert.Equal(GMImage.ImageFormat.Bz2Qoi, ReadStoredImage(bz2Qoi.ToSpan(gm2022_5: true).ToArray(), gm2022_5: true).Format);
        Assert.Equal(GMImage.ImageFormat.Dds, GMImage.FromDds(dds.GetData()).Format);
        Assert.Equal(GMImage.ImageFormat.Unknown, ReadStoredImage("unknown!"u8.ToArray()).Format);
    }

    private static GMImage ReadStoredImage(byte[] data, bool gm2022_5 = false)
    {
        using MemoryStream stream = new(data);
        using FileBinaryReader reader = new(stream);
        return GMImage.FromBinaryReader(reader, stream.Length, gm2022_5);
    }

    private static void SetPixel(Span<byte> pixels, int index, byte blue, byte green, byte red, byte alpha)
    {
        int offset = index * 4;
        pixels[offset] = blue;
        pixels[offset + 1] = green;
        pixels[offset + 2] = red;
        pixels[offset + 3] = alpha;
    }
}
