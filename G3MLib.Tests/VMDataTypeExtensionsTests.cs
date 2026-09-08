using System;
using G3MLib.Analyzer;
using Xunit;

namespace G3MLib.Tests;

public sealed class VMDataTypeExtensionsTests
{
    [Fact("VMDataTypeExtensionsTests.cs", 8)]
    public void BinaryResultRejectsUnknownDataTypes()
    {
        Assert.Throws<InvalidOperationException>(() => ((IGMInstruction.DataType)255).BinaryResultWith(IGMInstruction.Opcode.Add, (IGMInstruction.DataType)255));
    }
}
