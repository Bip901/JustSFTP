using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using JustSFTP.Protocol.Models;
using JustSFTP.Server;

namespace JustSFTP.Tests;

public sealed class DummyServer : IAsyncDisposable
{
    public Stream ClientReadStream { get; }
    public Stream ClientWriteStream { get; }

    private readonly SFTPServer server;
    private readonly Task serverTask;
    private readonly CancellationTokenSource serverCancel;

    public DummyServer(
        SFTPServer server,
        Task serverTask,
        CancellationTokenSource serverCancel,
        Stream clientReadStream,
        Stream clientWriteStream
    )
    {
        this.server = server;
        this.serverTask = serverTask;
        this.serverCancel = serverCancel;
        ClientReadStream = clientReadStream;
        ClientWriteStream = clientWriteStream;
    }

    public static DummyServer Run()
    {
        AnonymousPipeServerStream clientWrite = new(PipeDirection.Out);
        AnonymousPipeClientStream serverRead = new(
            PipeDirection.In,
            clientWrite.ClientSafePipeHandle
        );
        AnonymousPipeServerStream serverWrite = new(PipeDirection.Out);
        AnonymousPipeClientStream clientRead = new(
            PipeDirection.In,
            serverWrite.ClientSafePipeHandle
        );

        CancellationTokenSource serverCancel = new();
        string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        SFTPPath sftpRoot = new(Path.Combine(baseDirectory, "DummyServerFiles"));
        SFTPServer server = new(serverRead, serverWrite, sftpRoot);
        return new DummyServer(
            server,
            Task.Run(() => server.Run(serverCancel.Token)),
            serverCancel,
            clientRead,
            clientWrite
        );
    }

    public async ValueTask DisposeAsync()
    {
        serverCancel.Cancel();
        try
        {
            await serverTask;
        }
        catch (OperationCanceledException) { }
        server.Dispose();
    }
}
