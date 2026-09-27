using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace JustSFTP.Server;

/// <summary>
/// A collection of open SFTP handles.
/// </summary>
public class SFTPHandleCollection<T> : IDisposable
    where T : IDisposable
{
    /// <summary>
    /// Whether this handle collection allows any more open handles.
    /// </summary>
    public bool IsFull => openHandles.Count >= maxConcurrentHandles;

    private readonly ConcurrentDictionary<SFTPHandle, T> openHandles;
    private readonly int maxConcurrentHandles;

    /// <summary>
    /// Creates a new empty <see cref="SFTPHandleCollection{T}"/>.
    /// </summary>
    /// <param name="maxConcurrentHandles">The maximum amount of concurrently open handles.</param>
    public SFTPHandleCollection(int maxConcurrentHandles = 16)
    {
        this.maxConcurrentHandles = maxConcurrentHandles;
        openHandles = new ConcurrentDictionary<SFTPHandle, T>(1, maxConcurrentHandles);
    }

    /// <summary>
    /// Adds an item to the collection.
    /// </summary>
    /// <returns>A handle to the given item.</returns>
    /// <exception cref="InvalidOperationException">If exceeded the maximum allowed amount of concurrently open handles.</exception>
    public byte[] Add(T item)
    {
        if (IsFull)
        {
            item.Dispose();
            throw new InvalidOperationException($"Exceeded max concurrent handles ({maxConcurrentHandles})");
        }
        byte[] handle = CreateHandle();
        openHandles.TryAdd(new SFTPHandle(handle), item); // Should always return true since the key is new
        return handle;
    }

    /// <summary>
    /// Disposes and removes a handle from the collection.
    /// </summary>
    /// <returns>Whether the handle existed in the collection.</returns>
    public bool Remove(byte[] handle)
    {
        if (!openHandles.Remove(new SFTPHandle(handle), out T? fd))
        {
            return false;
        }
        fd.Dispose();
        return true;
    }

    /// <summary>
    /// Attempts returning the open file identified by the given handle.
    /// </summary>
    /// <returns>Whether the file was found.</returns>
    public bool TryGet(byte[] handle, [NotNullWhen(true)] out T? fd)
    {
        return openHandles.TryGetValue(new SFTPHandle(handle), out fd);
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
        foreach (T fd in openHandles.Values)
        {
            fd.Dispose();
        }
        openHandles.Clear();
    }
}
