using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JustSFTP.Protocol;
using JustSFTP.Protocol.Enums;
using JustSFTP.Protocol.Models;

namespace JustSFTP.Server;

/// <summary>
/// Serves a subtree of the regular filesystem over SFTP.
/// </summary>
public class DefaultSFTPHandler(SFTPPath root) : ISFTPHandler, IDisposable
{
    private const int MAX_RESPONSE_BUFFER_SIZE = 256 * 1024 - 1024; // 255 KiB

    private static readonly Uri _virtualroot = new("virt://", UriKind.Absolute);
    private readonly SFTPHandleCollection openHandles = new();
    private readonly SFTPPath root = root;

    /// <summary>
    /// Optionally, server extensions to announce to clients.
    /// </summary>
    public SFTPExtensions? ServerExtensions { get; init; }

    /// <inheritdoc/>
    public virtual Task<SFTPExtensions> Init(
        uint clientVersion,
        SFTPExtensions extensions,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(ServerExtensions ?? SFTPExtensions.None);

    /// <inheritdoc/>
    public virtual Task<byte[]> Open(
        SFTPPath path,
        FileMode fileMode,
        FileAccess fileAccess,
        SFTPAttributes attributes,
        CancellationToken cancellationToken = default
    )
    {
        if (openHandles.IsFull)
        {
            throw new HandlerException(Status.Failure);
        }
        string physicalPath = GetPhysicalPath(path);
        if (!File.Exists(physicalPath))
        {
            throw new HandlerException(Status.NoSuchFile);
        }
        try
        {
            byte[] handle = openHandles.Add(
                new SFTPHandleCollection.OpenSFTPFile(
                    path,
                    File.Open(physicalPath, fileMode, fileAccess, FileShare.ReadWrite)
                )
            );
            return Task.FromResult(handle);
        }
        catch (FileNotFoundException ex)
        {
            throw new HandlerException(Status.NoSuchFile, null, ex);
        }
    }

    /// <inheritdoc/>
    public virtual Task Close(byte[] handle, CancellationToken cancellationToken = default)
    {
        openHandles.Remove(handle);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual async Task<byte[]> Read(
        byte[] handle,
        ulong offset,
        uint length,
        CancellationToken cancellationToken = default
    )
    {
        SFTPHandleCollection.OpenSFTPFile file = openHandles.RequireFile(handle);
        if (offset >= (ulong)file.Stream.Length)
        {
            throw new HandlerException(Status.EndOfFile);
        }
        byte[] buffer = new byte[Math.Min(MAX_RESPONSE_BUFFER_SIZE, length)];
        int bytesRead = await RandomAccess
            .ReadAsync(file.Stream.SafeFileHandle, buffer.AsMemory(), (long)offset, cancellationToken)
            .ConfigureAwait(false);
        return buffer[..bytesRead];
    }

    /// <inheritdoc/>
    public virtual async Task Write(
        byte[] handle,
        ulong offset,
        byte[] data,
        CancellationToken cancellationToken = default
    )
    {
        SFTPHandleCollection.OpenSFTPFile file = openHandles.RequireFile(handle);
        await RandomAccess
            .WriteAsync(file.Stream.SafeFileHandle, data.AsMemory(), (long)offset, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public virtual Task<SFTPAttributes> LStat(SFTPPath path, CancellationToken cancellationToken = default) =>
        TryGetFSObject(path, out var fso)
            ? Task.FromResult(SFTPAttributes.FromFileSystemInfo(fso))
            : throw new HandlerException(Status.NoSuchFile);

    /// <inheritdoc/>
    public virtual Task<SFTPAttributes> FStat(byte[] handle, CancellationToken cancellationToken = default) =>
        openHandles.TryGet(handle, out var openFile)
            ? Stat(openFile.Path, cancellationToken)
            : throw new HandlerException(Status.NoSuchFile);

    /// <inheritdoc/>
    public virtual Task SetStat(
        SFTPPath path,
        SFTPAttributes attributes,
        CancellationToken cancellationToken = default
    ) => DoStat(path, attributes, cancellationToken);

    /// <inheritdoc/>
    public virtual Task FSetStat(
        byte[] handle,
        SFTPAttributes attributes,
        CancellationToken cancellationToken = default
    ) =>
        openHandles.TryGet(handle, out var openFile)
            ? SetStat(openFile.Path, attributes, cancellationToken)
            : throw new HandlerException(Status.NoSuchFile);

    /// <inheritdoc/>
    public virtual Task<byte[]> OpenDir(SFTPPath path, CancellationToken cancellationToken = default)
    {
        FileSystemInfo[] fileSystemInfos;
        try
        {
            fileSystemInfos = new DirectoryInfo(GetPhysicalPath(path)).GetFileSystemInfos();
        }
        catch (DirectoryNotFoundException ex)
        {
            throw new HandlerException(Status.NoSuchFile, null, ex);
        }
        return Task.FromResult(
            openHandles.Add(
                new SFTPHandleCollection.OpenSFTPDirectory(
                    path,
                    self => fileSystemInfos.Select(fso => SFTPName.FromFileSystemInfo(fso))
                )
            )
        );
    }

    /// <inheritdoc/>
    public virtual Task<IEnumerator<SFTPName>> ReadDir(byte[] handle, CancellationToken cancellationToken = default)
    {
        return Task.FromResult((IEnumerator<SFTPName>)openHandles.RequireDirectory(handle));
    }

    /// <inheritdoc/>
    public virtual Task Remove(SFTPPath path, CancellationToken cancellationToken = default)
    {
        if (TryGetFSObject(path, out var fsObject) && fsObject is FileInfo)
        {
            File.Delete(fsObject.FullName);
            return Task.CompletedTask;
        }
        throw new HandlerException(Status.NoSuchFile);
    }

    /// <inheritdoc/>
    public virtual Task MakeDir(SFTPPath path, SFTPAttributes attributes, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(GetPhysicalPath(path));
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual Task RemoveDir(SFTPPath path, CancellationToken cancellationToken = default)
    {
        if (TryGetFSObject(path, out var fsObject) && fsObject is DirectoryInfo)
        {
            Directory.Delete(fsObject.FullName);
            return Task.CompletedTask;
        }
        throw new HandlerException(Status.NoSuchFile);
    }

    /// <inheritdoc/>
    public virtual Task<SFTPPath> RealPath(SFTPPath path, CancellationToken cancellationToken = default) =>
        Task.FromResult(new SFTPPath(GetVirtualPath(path)));

    /// <inheritdoc/>
    public virtual Task<SFTPAttributes> Stat(SFTPPath path, CancellationToken cancellationToken = default) =>
        LStat(path, cancellationToken);

    /// <inheritdoc/>
    public virtual Task Rename(SFTPPath oldPath, SFTPPath newPath, CancellationToken cancellationToken = default)
    {
        if (TryGetFSObject(oldPath, out var fsOldObject) && fsOldObject is FileInfo)
        {
            File.Move(fsOldObject.FullName, GetPhysicalPath(newPath));
            return Task.CompletedTask;
        }
        throw new HandlerException(Status.NoSuchFile);
    }

#if NET6_0_OR_GREATER
    public virtual Task<SFTPName> ReadLink(SFTPPath path, CancellationToken cancellationToken = default)
    {
        if (TryGetFSObject(path, out var fsObject) && fsObject.LinkTarget != null)
        {
            return Task.FromResult(new SFTPName(fsObject.LinkTarget, SFTPAttributes.DummyFile));
        }
        throw new HandlerException(Status.NoSuchFile);
    }

    public virtual Task SymLink(SFTPPath linkPath, SFTPPath targetPath, CancellationToken cancellationToken = default)
    {
        var link = GetPhysicalPath(linkPath);
        if (TryGetFSObject(targetPath, out var fsObject))
        {
            switch (fsObject)
            {
                case FileInfo:
                    File.CreateSymbolicLink(link, fsObject.FullName);
                    break;
                case DirectoryInfo:
                    Directory.CreateSymbolicLink(link, fsObject.FullName);
                    break;
            }
            return Task.CompletedTask;
        }
        throw new HandlerException(Status.NoSuchFile);
    }
#endif

    /// <inheritdoc/>
    public virtual string GetPhysicalPath(SFTPPath path) => Path.Join(root.Path, GetVirtualPath(path));

    /// <inheritdoc/>
    public virtual string GetVirtualPath(SFTPPath path) => new Uri(_virtualroot, path.Path).LocalPath;

    private Task DoStat(SFTPPath path, SFTPAttributes attributes, CancellationToken cancellationToken = default)
    {
        if (TryGetFSObject(path, out FileSystemInfo? fileSystemInfo))
        {
            if (attributes.FileSize != null && fileSystemInfo is FileInfo fileInfo)
            {
                using FileStream stream = fileInfo.Open(FileMode.Open, FileAccess.Write);
                stream.SetLength((long)attributes.FileSize);
            }
            if (attributes.LastAccessedTime != null)
            {
                fileSystemInfo.LastAccessTimeUtc = attributes.LastAccessedTime.Value.UtcDateTime;
            }
            if (attributes.LastModifiedTime != null)
            {
                fileSystemInfo.LastWriteTimeUtc = attributes.LastModifiedTime.Value.UtcDateTime;
            }
            // TODO: Read/Write/Execute... etc.
        }

        return Task.CompletedTask;
    }

    private bool TryGetFSObject(SFTPPath path, [NotNullWhen(true)] out FileSystemInfo? fileSystemObject)
    {
        var resolved = GetPhysicalPath(path);
        if (Directory.Exists(resolved))
        {
            fileSystemObject = new DirectoryInfo(resolved);
            return true;
        }
        if (File.Exists(resolved))
        {
            fileSystemObject = new FileInfo(resolved);
            return true;
        }
        fileSystemObject = null;
        return false;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        openHandles.Dispose();
    }
}
