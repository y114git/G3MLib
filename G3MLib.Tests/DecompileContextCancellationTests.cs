using System;
using System.Threading;
using G3MLib.Analyzer.Decompiler;
using G3MLib.Analyzer.Mock;
using Xunit;

namespace G3MLib.Tests;

public class DecompileContextCancellationTests
{
    [Fact]
    public void DecompileStopsBeforeStartingWhenCancelled()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        DecompileContext context = new(new GameContextMock(), new GMCode("test", []), cancellationToken: cancellation.Token);

        Assert.Throws<OperationCanceledException>(context.DecompileToString);
    }
}
