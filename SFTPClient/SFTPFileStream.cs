using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JustSFTP.Protocol;
using JustSFTP.Protocol.Enums;

namespace JustSFTP.Client;

internal class SFTPFileStream : Stream
{
    public override bool CanRead => canRead;

    public override bool CanWrite => canWrite;

    public override bool CanSeek => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => position;
        set => Seek(value, SeekOrigin.Begin);
    }
    private long position;

    private readonly SFTPClient client;
    private readonly byte[] fileHandle;
    private readonly bool canRead;
    private readonly bool canWrite;
    private bool hasSentCloseRequest;

    internal SFTPFileStream(SFTPClient client, byte[] fileHandle, bool canRead, bool canWrite)
    {
        this.client = client;
        this.fileHandle = fileHandle;
        this.canRead = canRead;
        this.canWrite = canWrite;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default
    )
    {
        byte[] data;
        try
        {
            data = await client
                .ReadAsync(fileHandle, (ulong)position, (uint)buffer.Length, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HandlerException ex) when (ex.Status == Status.EndOfFile)
        {
            return 0;
        }
        data.CopyTo(buffer);
        position += data.Length;
        return data.Length;
    }

    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default
    )
    {
        await client
            .WriteAsync(fileHandle, (ulong)position, buffer.ToArray(), cancellationToken)
            .ConfigureAwait(false);
        position += buffer.Length;
    }

    public override void Flush()
    {
        // This stream does not buffer anyway
    }

    public override async Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken
    )
    {
        return await ReadAsync(buffer.AsMemory(offset, count), cancellationToken)
            .ConfigureAwait(false);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ValueTask<int> valueTask = ReadAsync(buffer.AsMemory(offset, count));
        if (valueTask.IsCompleted) // Try short-circuiting if a result is already available
        {
            return valueTask.Result;
        }
        return valueTask.AsTask().Result; // Block until task completes
    }

    public override async Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken
    )
    {
        await WriteAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        ValueTask valueTask = WriteAsync(buffer.AsMemory(offset, count));
        if (!valueTask.IsCompletedSuccessfully) // Try short-circuiting if the task is already complete
        {
            valueTask.AsTask().Wait(); // Block until task completes
        }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        switch (origin)
        {
            case SeekOrigin.Begin:
                break;
            case SeekOrigin.Current:
                break;
            case SeekOrigin.End:
                break;
        }
        // TODO
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        // TODO
        throw new NotSupportedException();
    }

    public override async ValueTask DisposeAsync()
    {
        if (hasSentCloseRequest)
        {
            return;
        }
        hasSentCloseRequest = true;
        await client.CloseFileAsync(fileHandle).ConfigureAwait(false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ValueTask valueTask = DisposeAsync();
            if (!valueTask.IsCompletedSuccessfully)
            {
                valueTask.AsTask().Wait();
            }
        }
    }
}
