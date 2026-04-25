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

    public override bool CanSeek => canSeek;

    public override long Length => length == -1 ? throw new NotSupportedException() : length;
    private long length;

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
    private readonly bool canSeek;
    private bool hasSentCloseRequest;

    internal SFTPFileStream(
        SFTPClient client,
        byte[] fileHandle,
        bool canRead,
        bool canWrite,
        bool canSeek,
        long length = -1,
        long initialPosition = 0
    )
    {
        this.client = client;
        this.fileHandle = fileHandle;
        this.canRead = canRead;
        this.canWrite = canWrite;
        this.canSeek = canSeek;
        if (canSeek && length == -1)
        {
            throw new ArgumentException(
                $"If {nameof(canSeek)} is true, length must be provided.",
                nameof(length)
            );
        }
        this.length = length;
        position = initialPosition;
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
        if (length != -1 && length < position)
        {
            length = position;
        }
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
        if (!canSeek)
        {
            throw new NotSupportedException();
        }
        return origin switch
        {
            SeekOrigin.Begin => position = offset,
            SeekOrigin.Current => position += offset,
            SeekOrigin.End => position = length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
    }

    /// <summary>
    /// Since this method is not async, you should use <see cref="SFTPClient.SetStatAsync"/> instead.
    /// </summary>
    /// <exception cref="NotSupportedException"></exception>
    public override void SetLength(long value)
    {
        throw new NotSupportedException("Use SetStatAsync instead.");
    }

    public override async ValueTask DisposeAsync()
    {
        if (hasSentCloseRequest)
        {
            return;
        }
        hasSentCloseRequest = true;
        _ = Task.Run(() => client.CloseFileAsync(fileHandle));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ValueTask valueTask = DisposeAsync();
            if (!valueTask.IsCompletedSuccessfully)
            {
                _ = Task.Run(() => valueTask);
            }
        }
    }
}
