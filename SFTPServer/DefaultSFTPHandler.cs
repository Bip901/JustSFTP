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
    private static readonly Uri _virtualroot = new("virt://", UriKind.Absolute);
    private readonly SFTPHandleCollection openHandles = new();
    private readonly SFTPPath root = root;

    public virtual Task<SFTPExtensions> Init(
        uint clientVersion,
        SFTPExtensions extensions,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(SFTPExtensions.None);

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
        byte[] handle = openHandles.Add(
            new SFTPHandleCollection.OpenSFTPFile(
                path,
                File.Open(GetPhysicalPath(path), fileMode, fileAccess, FileShare.ReadWrite)
            )
        );
        return Task.FromResult(handle);
    }

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
        Stream stream = openHandles.RequireFileStream(handle);
        if (offset >= (ulong)stream.Length)
        {
            throw new HandlerException(Status.EndOfFile);
        }
        stream.Seek((long)offset, SeekOrigin.Begin);
        byte[] buffer = new byte[length];
        int bytesRead = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer[..bytesRead];
    }

    public virtual async Task Write(
        byte[] handle,
        ulong offset,
        byte[] data,
        CancellationToken cancellationToken = default
    )
    {
        Stream stream = openHandles.RequireFileStream(handle);
        if (stream.Position != (long)offset)
        {
            stream.Seek((long)offset, SeekOrigin.Begin);
        }
        await stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
    }

    public virtual Task<SFTPAttributes> LStat(
        SFTPPath path,
        CancellationToken cancellationToken = default
    ) =>
        TryGetFSObject(path, out var fso)
            ? Task.FromResult(SFTPAttributes.FromFileSystemInfo(fso))
            : throw new HandlerException(Status.NoSuchFile);

    public virtual Task<SFTPAttributes> FStat(
        byte[] handle,
        CancellationToken cancellationToken = default
    ) =>
        openHandles.TryGet(handle, out var openFile)
            ? Stat(openFile.Path, cancellationToken)
            : throw new HandlerException(Status.NoSuchFile);

    public virtual Task SetStat(
        SFTPPath path,
        SFTPAttributes attributes,
        CancellationToken cancellationToken = default
    ) => DoStat(path, attributes, cancellationToken);

    public virtual Task FSetStat(
        byte[] handle,
        SFTPAttributes attributes,
        CancellationToken cancellationToken = default
    ) =>
        openHandles.TryGet(handle, out var openFile)
            ? SetStat(openFile.Path, attributes, cancellationToken)
            : throw new HandlerException(Status.NoSuchFile);

    public virtual Task<byte[]> OpenDir(
        SFTPPath path,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult(
            openHandles.Add(
                new SFTPHandleCollection.OpenSFTPDirectory(
                    path,
                    self =>
                        new DirectoryInfo(GetPhysicalPath(self.Path))
                            .GetFileSystemInfos()
                            .Select(fso => SFTPName.FromFileSystemInfo(fso))
                )
            )
        );
    }

    public virtual Task<IEnumerator<SFTPName>> ReadDir(
        byte[] handle,
        CancellationToken cancellationToken = default
    )
    {
        if (
            !openHandles.TryGet(handle, out var openFile)
            || openFile is not SFTPHandleCollection.OpenSFTPDirectory directory
        )
        {
            throw new HandlerException(Status.NoSuchFile);
        }
        return Task.FromResult((IEnumerator<SFTPName>)directory);
    }

    public virtual Task Remove(SFTPPath path, CancellationToken cancellationToken = default)
    {
        if (TryGetFSObject(path, out var fsObject) && fsObject is FileInfo)
        {
            File.Delete(fsObject.FullName);
            return Task.CompletedTask;
        }
        throw new HandlerException(Status.NoSuchFile);
    }

    public virtual Task MakeDir(
        SFTPPath path,
        SFTPAttributes attributes,
        CancellationToken cancellationToken = default
    )
    {
        Directory.CreateDirectory(GetPhysicalPath(path));
        return Task.CompletedTask;
    }

    public virtual Task RemoveDir(SFTPPath path, CancellationToken cancellationToken = default)
    {
        if (TryGetFSObject(path, out var fsObject) && fsObject is DirectoryInfo)
        {
            Directory.Delete(fsObject.FullName);
            return Task.CompletedTask;
        }
        throw new HandlerException(Status.NoSuchFile);
    }

    public virtual Task<SFTPPath> RealPath(
        SFTPPath path,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(new SFTPPath(GetVirtualPath(path)));

    public virtual Task<SFTPAttributes> Stat(
        SFTPPath path,
        CancellationToken cancellationToken = default
    ) => LStat(path, cancellationToken);

    public virtual Task Rename(
        SFTPPath oldPath,
        SFTPPath newPath,
        CancellationToken cancellationToken = default
    )
    {
        if (TryGetFSObject(oldPath, out var fsOldObject) && fsOldObject is FileInfo)
        {
            File.Move(fsOldObject.FullName, GetPhysicalPath(newPath));
            return Task.CompletedTask;
        }
        throw new HandlerException(Status.NoSuchFile);
    }

#if NET6_0_OR_GREATER
    public virtual Task<SFTPName> ReadLink(
        SFTPPath path,
        CancellationToken cancellationToken = default
    )
    {
        if (TryGetFSObject(path, out var fsObject) && fsObject.LinkTarget != null)
        {
            return Task.FromResult(SFTPName.FromString(fsObject.LinkTarget));
        }
        throw new HandlerException(Status.NoSuchFile);
    }

    public virtual Task SymLink(
        SFTPPath linkPath,
        SFTPPath targetPath,
        CancellationToken cancellationToken = default
    )
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

    public virtual string GetPhysicalPath(SFTPPath path) =>
        Path.Join(root.Path, GetVirtualPath(path));

    public virtual string GetVirtualPath(SFTPPath path) =>
        new Uri(_virtualroot, path.Path).LocalPath;

    private Task DoStat(
        SFTPPath path,
        SFTPAttributes attributes,
        CancellationToken cancellationToken = default
    )
    {
        if (TryGetFSObject(path, out var fsoObject))
        {
            if (attributes.LastAccessedTime != DateTimeOffset.MinValue)
            {
                fsoObject.LastAccessTimeUtc = attributes.LastAccessedTime.UtcDateTime;
            }
            if (attributes.LastModifiedTime != DateTimeOffset.MinValue)
            {
                fsoObject.LastWriteTimeUtc = attributes.LastModifiedTime.UtcDateTime;
            }
            // TODO: Read/Write/Execute... etc.
        }

        return Task.CompletedTask;
    }

    private bool TryGetFSObject(
        SFTPPath path,
        [NotNullWhen(true)] out FileSystemInfo? fileSystemObject
    )
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
