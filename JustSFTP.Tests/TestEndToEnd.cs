using System;
using System.Threading.Tasks;
using JustSFTP.Client;

namespace JustSFTP.Tests;

public class TestEndToEnd
{
    [Fact]
    public async Task TestInit()
    {
        await using DummyServer dummyServer = DummyServer.Run();
        using SFTPClient client = new(dummyServer.ClientReadStream, dummyServer.ClientWriteStream);
        Task clientTask = Task.Run(() => client.RunAsync());
        // TODO: await client.someRequest
        Assert.Equal(3u, client.ProtocolVersion);
        try
        {
            await clientTask;
        }
        catch (OperationCanceledException) { }
    }
}
