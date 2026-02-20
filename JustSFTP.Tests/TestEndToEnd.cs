using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JustSFTP.Client;
using JustSFTP.Protocol.Models;

namespace JustSFTP.Tests;

public class TestEndToEnd
{
    private static readonly string[] expectedDirectoryListing = ["file1.txt", "file2.txt"];

    [Fact]
    public async Task TestFullSession()
    {
        TraceSource clientTraceSource = new(nameof(SFTPClient), SourceLevels.All);
        clientTraceSource.Listeners.Add(new ConsoleTraceListener(useErrorStream: true));
        await using DummyServer dummyServer = DummyServer.Run();
        using SFTPClient client = new(
            dummyServer.ClientReadStream,
            dummyServer.ClientWriteStream,
            traceSource: clientTraceSource
        );
        CancellationTokenSource clientCancel = new();
        Task clientTask = Task.Run(() => client.RunAsync(clientCancel.Token));
        await using (
            Stream fileStream = await client.OpenFileAsync(
                "/example.txt",
                Protocol.Enums.AccessFlags.Read,
                SFTPAttributes.DummyFile
            )
        )
        {
            Assert.Equal(3u, client.ProtocolVersion);
            using StreamReader reader = new(fileStream, leaveOpen: true);
            string fileContents = await reader.ReadToEndAsync();
            Assert.Equal("This is an example file for testing.\n", fileContents);
        }
        HashSet<string> names = [];
        await foreach (SFTPName child in client.IterDirAsync("/test-dir"))
        {
            names.Add(child.Name);
        }
        Assert.Equivalent(expectedDirectoryListing, names);
        clientCancel.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => clientTask);
    }
}
