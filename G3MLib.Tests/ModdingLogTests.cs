using System.Collections.Generic;
using G3MLib.Modding.Logging;
using Xunit;

namespace G3MLib.Tests;

public sealed class ModdingLogTests
{
    [Fact("ModdingLogTests.cs", 9)]
    public void LogIsDeliveredThroughTheHostEvent()
    {
        List<ModdingLogMessage> messages = [];
        void Capture(ModdingLogMessage message) => messages.Add(message);

        LogService.MessageLogged += Capture;
        try
        {
            LogService.Log("test message");
        }
        finally
        {
            LogService.MessageLogged -= Capture;
        }

        ModdingLogMessage message = Assert.Single(messages);
        Assert.Equal(ModdingLogLevel.Trace, message.Level);
        Assert.Equal("test message", message.Message);
    }
}
