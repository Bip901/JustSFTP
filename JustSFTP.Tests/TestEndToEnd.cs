using System;
using System.Threading;
using System.Threading.Tasks;
using JustSFTP.Client;
using JustSFTP.Protocol.Models;

namespace JustSFTP.Tests;

public class TestEndToEnd
{
    [Fact]
    public async Task TestFullSession()
    {
        await using DummyServer dummyServer = DummyServer.Run();
        using SFTPClient client = new(dummyServer.ClientReadStream, dummyServer.ClientWriteStream);
        CancellationTokenSource clientCancel = new();
        Task clientTask = Task.Run(() => client.RunAsync(clientCancel.Token));
        SFTPHandle handle = await client.OpenFileAsync(
            "/example.txt",
            Protocol.Enums.AccessFlags.Read,
            SFTPAttributes.DummyFile
        );
        Assert.Equal(3u, client.ProtocolVersion);
        clientCancel.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => clientTask);
    }
}
