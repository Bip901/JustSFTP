using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JustSFTP.Client;
using JustSFTP.Protocol;
using JustSFTP.Protocol.Enums;
using JustSFTP.Protocol.Models;
using JustSFTP.Protocol.Models.Requests.Extended;
using JustSFTP.Protocol.Models.Responses;
using JustSFTP.Server;

namespace JustSFTP.Tests;

public class TestEndToEnd
{
    private static readonly Dictionary<string, string> serverExtensions = new()
    {
        { "example-extension-server@openssh.com", "example-value-server" },
    };
    private static readonly Dictionary<string, string> clientExtensions = new()
    {
        { "example-extension-client@openssh.com", "example-value-client" },
    };
    private static readonly string[] expectedDirectoryListing = ["file1.txt", "file2.txt"];
    private const string exampleFileContents = "This is an example file for testing.\n";

    [Fact]
    public async Task TestFullSession()
    {
        TraceSource clientTraceSource = new(nameof(SFTPClient), SourceLevels.All);
        clientTraceSource.Listeners.Add(new ConsoleTraceListener(useErrorStream: true));
        TraceSource serverTraceSource = new(nameof(SFTPServer), SourceLevels.All);
        serverTraceSource.Listeners.Add(new ConsoleTraceListener(useErrorStream: true));
        await using DummyServer dummyServer = DummyServer.Run(
            new SFTPExtensions(serverExtensions),
            serverTraceSource
        );
        using SFTPClient client = new(
            dummyServer.ClientReadStream,
            dummyServer.ClientWriteStream,
            traceSource: clientTraceSource
        );
        CancellationTokenSource clientCancel = new();

        // Test init handshake
        await client.InitAsync(clientExtensions);
        Assert.Equal(3u, client.ProtocolVersion);
        Assert.Equivalent(client.ServerExtensions, serverExtensions);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.InitAsync(null));

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
        // Convert to seconds accuracy because that's the accuracy preserved in SFTP
        DateTimeOffset utcNow = DateTimeOffset.FromUnixTimeSeconds(
            DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        );
        await client.SetStatAsync(
            "/example.txt",
            new SFTPAttributes() { LastModifiedTime = utcNow, LastAccessedTime = utcNow }
        );
        SFTPAttributes serverAttributes = await client.StatAsync("/example.txt");
        Assert.Equal((ulong)exampleFileContents.Length, serverAttributes.FileSize);
        Assert.Equal(utcNow, serverAttributes.LastModifiedTime);

        // Test extensions
        Assert.Equal(
            Status.OperationUnsupported,
            (
                await Assert.ThrowsAsync<HandlerException>(() =>
                    client.ExtendedRequestAsync<SFTPStatus>(requestId => new SFTPPosixRenameRequest(
                        requestId,
                        "/example.txt",
                        "/example2.txt"
                    ))
                )
            ).Status
        );

        clientCancel.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => clientTask);
    }
}
