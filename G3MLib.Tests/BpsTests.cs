using System;
using System.IO;
using System.Text;
using G3MLib.DataFile.Util;
using Xunit;

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
        byte[] corruptPatch = (byte[])Patch.Clone();
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
}
