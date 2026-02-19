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
public class DefaultSFTPHandler : ISFTPHandler, IDisposable
{
    private readonly Dictionary<byte[], SFTPPath> _filehandles = new();
    private readonly Dictionary<byte[], Stream> _streamhandles = new();
    private readonly SFTPPath _root;

    private static readonly Uri _virtualroot = new("virt://", UriKind.Absolute);

    public DefaultSFTPHandler(SFTPPath root) =>
        _root = root ?? throw new ArgumentNullException(nameof(root));

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
        var handle = CreateHandle();
        _streamhandles.Add(
            handle,
            File.Open(GetPhysicalPath(path), fileMode, fileAccess, FileShare.ReadWrite)
        );
        _filehandles.Add(handle, path);
        return Task.FromResult(handle);
    }

    public virtual Task Close(byte[] handle, CancellationToken cancellationToken = default)
    {
        _filehandles.Remove(handle);

        if (_streamhandles.TryGetValue(handle, out Stream? stream))
        {
            stream.Close();
            stream.Dispose();
        }
        _streamhandles.Remove(handle);
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
        Stream stream = RequireStreamHandle(handle);
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
        Stream stream = RequireStreamHandle(handle);
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
            : throw new HandlerException(Protocol.Enums.Status.NoSuchFile);

    public virtual Task<SFTPAttributes> FStat(
        byte[] handle,
        CancellationToken cancellationToken = default
    ) =>
        TryGetFileHandle(handle, out var path)
            ? Stat(path, cancellationToken)
            : throw new HandlerException(Protocol.Enums.Status.NoSuchFile);

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
        TryGetFileHandle(handle, out var path)
            ? SetStat(path, attributes, cancellationToken)
            : throw new HandlerException(Protocol.Enums.Status.NoSuchFile);

    public virtual Task<byte[]> OpenDir(
        SFTPPath path,
        CancellationToken cancellationToken = default
    )
    {
        var handle = CreateHandle();
        _filehandles.Add(handle, path);
        return Task.FromResult(handle);
    }

    public virtual Task<IEnumerable<SFTPName>> ReadDir(
        byte[] handle,
        CancellationToken cancellationToken = default
    ) =>
        TryGetFileHandle(handle, out var path)
            ? Task.FromResult(
                new DirectoryInfo(GetPhysicalPath(path))
                    .GetFileSystemInfos()
                    .Select(fso => SFTPName.FromFileSystemInfo(fso))
            )
            : throw new HandlerException(Protocol.Enums.Status.NoSuchFile);

    public virtual Task Remove(SFTPPath path, CancellationToken cancellationToken = default)
    {
        if (TryGetFSObject(path, out var fsObject) && fsObject is FileInfo)
        {
            File.Delete(fsObject.FullName);
            return Task.CompletedTask;
        }
        throw new HandlerException(Protocol.Enums.Status.NoSuchFile);
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
        throw new HandlerException(Protocol.Enums.Status.NoSuchFile);
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
        throw new HandlerException(Protocol.Enums.Status.NoSuchFile);
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
        throw new HandlerException(Protocol.Enums.Status.NoSuchFile);
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
        throw new HandlerException(Protocol.Enums.Status.NoSuchFile);
    }
#endif

    public virtual string GetPhysicalPath(SFTPPath path) =>
        Path.Join(_root.Path, GetVirtualPath(path));

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

    private static byte[] CreateHandle() => Guid.NewGuid().ToByteArray();

    protected bool TryGetFileHandle(byte[] key, [NotNullWhen(true)] out SFTPPath? path) =>
        _filehandles.TryGetValue(key, out path);

    private Stream RequireStreamHandle(byte[] handle)
    {
        if (!_streamhandles.TryGetValue(handle, out Stream? stream))
        {
            throw new HandlerException(Status.NoSuchFile);
        }
        return stream;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (Stream handle in _streamhandles.Values)
        {
            handle.Dispose();
        }
        _streamhandles.Clear();
        _filehandles.Clear();
    }
}
