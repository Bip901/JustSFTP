using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using JustSFTP.Protocol.Models;

namespace JustSFTP.Server;

/// <summary>
/// Represents an open SFTP file or directory.
/// </summary>
public abstract record class OpenSFTPFileOrDirectory : IDisposable
{
    /// <inheritdoc/>
    public virtual void Dispose() { }
}

/// <param name="Stream">The open file stream.</param>
/// <param name="FileMode"> The mode this was opened with.</param>
public record OpenSFTPFile(FileStream Stream, FileMode FileMode) : OpenSFTPFileOrDirectory
{
    /// <summary>
    /// The semaphore to use when using non-concurrent stream APIs.
    /// Do not wait for this semaphore when using <see cref="RandomAccess"/> APIs.
    /// </summary>
    public SemaphoreSlim StreamSemaphore { get; } = new SemaphoreSlim(1, 1);

    /// <inheritdoc/>
    public override void Dispose()
    {
        Stream.Dispose();
        StreamSemaphore.Dispose();
    }
}

public record OpenSFTPDirectory(string Path, Func<OpenSFTPDirectory, IEnumerable<SFTPName>> GetChildren)
    : OpenSFTPFileOrDirectory,
        IEnumerator<SFTPName>
{
    /// <summary>
    /// The path to the directory.
    /// </summary>
    public string Path { get; } = Path;

    /// <exception cref="InvalidOperationException"/>
    public SFTPName Current => inner?.Current ?? throw new InvalidOperationException();

    object IEnumerator.Current => Current;
    private IEnumerator<SFTPName>? inner;

    /// <inheritdoc/>
    public void Reset()
    {
        inner?.Dispose();
        inner = null;
    }

    /// <inheritdoc/>
    public bool MoveNext()
    {
        inner ??= GetChildren(this).GetEnumerator();
        return inner.MoveNext();
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        inner?.Dispose();
    }
}
