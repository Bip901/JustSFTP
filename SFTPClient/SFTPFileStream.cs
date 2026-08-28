using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JustSFTP.Protocol;
using JustSFTP.Protocol.Enums;

namespace JustSFTP.Client;

/// <summary>
/// A <see cref="Stream"/> over a remote SFTP file.
/// Reads and writes on this stream translate exactly to read and write SFTP requests,
/// so callers might want to add a layer of buffering (e.g. <see cref="BufferedStream"/>, <see cref="System.IO.Pipelines.PipeReader"/>).
/// </summary>
public class SFTPFileStream : Stream, IAsyncDisposableCancelable
{
    /// <inheritdoc/>
    public override bool CanRead => canRead;

    /// <inheritdoc/>
    public override bool CanWrite => canWrite;

    /// <inheritdoc/>
    public override bool CanSeek => canSeek;

    /// <summary>
    /// The length of the remote file in bytes. This property is only available if the stream was opened with seeking support.
    /// </summary>
    public override long Length => length == -1 ? throw new NotSupportedException() : length;
    private long length;

    /// <inheritdoc/>
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
    private CancellationToken disposeCancellationToken;

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
            throw new ArgumentException($"If {nameof(canSeek)} is true, length must be provided.", nameof(length));
        }
        this.length = length;
        position = initialPosition;
    }

    /// <inheritdoc/>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
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

    /// <inheritdoc/>
    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default
    )
    {
        await client.WriteAsync(fileHandle, (ulong)position, buffer.ToArray(), cancellationToken).ConfigureAwait(false);
        position += buffer.Length;
        if (length != -1 && length < position)
        {
            length = position;
        }
    }

    /// <summary>
    /// A no-op, as this stream does no buffering anyway.
    /// </summary>
    public override void Flush() { }

    /// <inheritdoc/>
    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return await ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        ValueTask<int> valueTask = ReadAsync(buffer.AsMemory(offset, count));
        if (valueTask.IsCompleted) // Try short-circuiting if a result is already available
        {
            return valueTask.Result;
        }
        return valueTask.AsTask().Result; // Block until task completes
    }

    /// <inheritdoc/>
    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        await WriteAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count)
    {
        ValueTask valueTask = WriteAsync(buffer.AsMemory(offset, count));
        if (!valueTask.IsCompletedSuccessfully) // Try short-circuiting if the task is already complete
        {
            valueTask.AsTask().Wait(); // Block until task completes
        }
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        if (hasSentCloseRequest)
        {
            return;
        }
        hasSentCloseRequest = true;
        await client.CloseFileAsync(fileHandle, disposeCancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public void SetDisposeCancellationToken(CancellationToken cancellationToken)
    {
        this.disposeCancellationToken = cancellationToken;
    }
}
