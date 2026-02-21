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
    private const string exampleFileContents = "This is an example file for testing.\n";

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

        // Test file reading
        await using (
            Stream fileStream = await client.OpenFileAsync(
                "/example.txt",
                Protocol.Enums.AccessFlags.Read,
                SFTPAttributes.DummyFile
            )
        )
        {
            Assert.Equal(3u, client.ProtocolVersion); // Test init handshake. Must happen after at least one request to avoid race with RunAsync
            using StreamReader reader = new(fileStream, leaveOpen: true);
            string fileContents = await reader.ReadToEndAsync();
            Assert.Equal(exampleFileContents, fileContents);
        }

        // Test ReadDir
        HashSet<string> names = [];
        await foreach (SFTPName child in client.IterDirAsync("/test-dir"))
        {
            names.Add(child.Name);
        }
        Assert.Equivalent(expectedDirectoryListing, names);

        // Test Stat
        SFTPAttributes exampleAttributes = await client.StatAsync("/example.txt");
        Assert.Equal((ulong)exampleFileContents.Length, exampleAttributes.FileSize);

        clientCancel.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => clientTask);
    }
}
