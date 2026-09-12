using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using G3MLib.Modding.Logging;
using Xunit;

namespace G3MLib.Tests;

public sealed class ModdingLogTests
{
    [Fact]
    public async Task ConcurrentScopesKeepTheirMessagesAndProgressSeparate()
    {
        async Task Check(string name)
        {
            List<ModdingLogMessage> messages = [];
            List<ModdingProgress> progress = [];
            using (LogService.BeginScope(messages.Add, progress.Add, verbose: true, suppress: false))
            {
                LogService.SetOperation(name);
                await Task.Yield();
                LogService.Info(name);
                LogService.ProgressRange(int.MaxValue, int.MaxValue, 0, 100);
            }
            Assert.Equal(name, Assert.Single(messages).Message);
            Assert.Equal(name, Assert.Single(progress).Operation);
            Assert.Equal(100, progress[0].Current);
        }
        await Task.WhenAll(Check("first"), Check("second"));
    }

    [Fact("ModdingLogTests.cs", 9)]
    public void LogIsDeliveredThroughTheHostEvent()
    {
        ConcurrentQueue<ModdingLogMessage> messages = [];
        void Capture(ModdingLogMessage message) => messages.Enqueue(message);

        LogService.MessageLogged += Capture;
        try
        {
            LogService.Log("test message");
        }
        finally
        {
            LogService.MessageLogged -= Capture;
        }

        Assert.Contains(messages, message => message is { Level: ModdingLogLevel.Trace, Message: "test message" });
    }
}
