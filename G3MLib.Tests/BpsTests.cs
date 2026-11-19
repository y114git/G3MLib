using System.Text;
using G3MLib.DataFile.Util;

namespace G3MLib.Tests;

public sealed class BpsTests
{
    private static readonly byte[] Patch =
    [
        66, 80, 83, 49, 133, 136, 128, 132, 129, 88,
        134, 134, 139, 128, 101, 216, 135, 133, 82, 15,
        179, 158, 135, 226, 242, 8
    ];

    [Fact("BpsTests.cs", 15)]
    public void ApplyPatchSupportsEveryBpsCommand()
    {
        using MemoryStream source = new(Encoding.ASCII.GetBytes("abcde"));
        using MemoryStream patch = new(Patch);
        using MemoryStream output = new();
        BPS.ApplyPatch(source, patch, output);
        Assert.Equal("abXdeabX", Encoding.ASCII.GetString(output.ToArray()));
    }

    [Fact("BpsTests.cs", 27)]
    public void ApplyPatchRejectsCorruptPatch()
    {
        var corruptPatch = (byte[])Patch.Clone();
        corruptPatch[^1] ^= 1;
        MemoryStream source = new(Encoding.ASCII.GetBytes("abcde"));
        try
        {
            MemoryStream patch = new(corruptPatch);
            try
            {
                MemoryStream output = new();
                try
                {
                    Assert.Throws<IOException>(delegate
                    {
                        BPS.ApplyPatch(source, patch, output);
                    });
                }
                finally
                {
                    if (output != null)
                    {
                        ((IDisposable)output).Dispose();
                    }
                }
            }
            finally
            {
                if (patch != null)
                {
                    ((IDisposable)patch).Dispose();
                }
            }
        }
        finally
        {
            if (source != null)
            {
                ((IDisposable)source).Dispose();
            }
        }
    }

    [Fact]
    public void ApplyPatchRejectsTargetCopyFromTheWritePosition()
    {
        // Empty base, target "abab": read "ab", then copy 2 bytes starting at offset 2 (the current write position).
        byte[] body = [66, 80, 83, 49, 0x80, 0x84, 0x80, 0x85, 97, 98, 0x87, 0x84, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        var crc = 0xFFFFFFFFu;
        foreach (var b in body.AsSpan(0, 20))
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
                crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1));
        }
        BitConverter.GetBytes(~crc).CopyTo(body, 20);
        using MemoryStream source = new();
        using MemoryStream patch = new(body);
        using MemoryStream output = new();

        Assert.Throws<IOException>(() => BPS.ApplyPatch(source, patch, output));
    }
}
