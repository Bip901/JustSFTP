using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using JustSFTP.Protocol;
using JustSFTP.Protocol.Enums;
using JustSFTP.Protocol.Models;

namespace JustSFTP.Server;

/// <summary>
/// A collection of open SFTP handles.
/// </summary>
public class SFTPHandleCollection : IDisposable
{
    /// <summary>
    /// Represents an open SFTP file or directory.
    /// </summary>
    /// <param name="Path">The path to the file or directory.</param>
    public abstract record class OpenSFTPFileOrDirectory(SFTPPath Path) : IDisposable
    {
        /// <inheritdoc/>
        public virtual void Dispose() { }
    }

    public record OpenSFTPFile(SFTPPath Path, Stream Stream) : OpenSFTPFileOrDirectory(Path)
    {
        /// <inheritdoc/>
        public override void Dispose()
        {
            Stream.Dispose();
        }
    }

    public record OpenSFTPDirectory(SFTPPath Path, Func<OpenSFTPDirectory, IEnumerable<SFTPName>> GetChildren)
        : OpenSFTPFileOrDirectory(Path),
            IEnumerator<SFTPName>
    {
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

    /// <summary>
    /// Whether this handle collection allows any more open handles.
    /// </summary>
    public bool IsFull => openFiles.Count >= maxConcurrentHandles;

    private readonly Dictionary<SFTPHandle, OpenSFTPFileOrDirectory> openFiles;
    private readonly int maxConcurrentHandles;

    /// <summary>
    /// Creates a new empty <see cref="SFTPHandleCollection"/>.
    /// </summary>
    /// <param name="maxConcurrentHandles">The maximum amount of concurrently open handles.</param>
    public SFTPHandleCollection(int maxConcurrentHandles = 16)
    {
        this.maxConcurrentHandles = maxConcurrentHandles;
        openFiles = new Dictionary<SFTPHandle, OpenSFTPFileOrDirectory>(maxConcurrentHandles);
    }

    /// <summary>
    /// Adds a file or directory to the collection.
    /// </summary>
    /// <returns>A handle to the given file.</returns>
    /// <exception cref="InvalidOperationException">If exceeded the maximum allowed amount of concurrently open files.</exception>
    public byte[] Add(OpenSFTPFileOrDirectory item)
    {
        if (IsFull)
        {
            item.Dispose();
            throw new InvalidOperationException(
                $"Exceeded max concurrent handles ({maxConcurrentHandles})"
            );
        }
        byte[] handle = CreateHandle();
        openFiles.Add(new SFTPHandle(handle), item);
        return handle;
    }

    /// <summary>
    /// Disposes and removes a file from the collection.
    /// </summary>
    /// <returns>Whether the handle existed in the collection.</returns>
    public bool Remove(byte[] handle)
    {
        if (!openFiles.Remove(new SFTPHandle(handle), out OpenSFTPFileOrDirectory? file))
        {
            return false;
        }
        file.Dispose();
        return true;
    }

    /// <summary>
    /// Attempts returning the open file identified by the given handle.
    /// </summary>
    /// <returns>Whether the file was found.</returns>
    public bool TryGet(byte[] handle, [NotNullWhen(true)] out OpenSFTPFileOrDirectory? file)
    {
        return openFiles.TryGetValue(new SFTPHandle(handle), out file);
    }

    /// <summary>
    /// Throws an <see cref="HandlerException"/> with <see cref="Status.NoSuchFile"/> if the given handle does not correspond to an open file.
    /// </summary>
    /// <returns>The matching open file's stream.</returns>
    /// <exception cref="HandlerException"/>
    public Stream RequireFileStream(byte[] handle)
    {
        if (
            !openFiles.TryGetValue(
                new SFTPHandle(handle),
                out OpenSFTPFileOrDirectory? fileOrDirectory
            ) || fileOrDirectory is not OpenSFTPFile file
        )
        {
            throw new HandlerException(Status.NoSuchFile);
        }
        return file.Stream;
    }

    /// <summary>
    /// Returns a new SFTP handle unique to this collection.
    /// </summary>
    private static byte[] CreateHandle()
    {
        return Guid.NewGuid().ToByteArray();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        foreach (OpenSFTPFileOrDirectory file in openFiles.Values)
        {
            file.Dispose();
        }
        openFiles.Clear();
    }
}
