using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JustSFTP.Protocol;
using JustSFTP.Protocol.Enums;
using JustSFTP.Protocol.IO;
using JustSFTP.Protocol.Models;
using JustSFTP.Protocol.Models.Requests.Extended;
using JustSFTP.Protocol.Models.Responses;
using JustSFTP.Protocol.Models.Responses.Extended;
using Microsoft.Win32.SafeHandles;

namespace JustSFTP.Server;

/// <summary>
/// Serves a subtree of the regular filesystem over SFTP.
/// </summary>
public class DefaultSFTPHandler : ISFTPHandler, IDisposable
{
    /// <summary>
    /// Maximum data read that we are willing to accept.
    /// Values mirrors OpenSSH's SFTP_MAX_READ_LENGTH.
    /// </summary>
    private const int MAX_RESPONSE_BUFFER_SIZE = SFTPIOConsts.MaxMessageLength - 1024;

    private readonly SFTPHandleCollection<OpenSFTPFileOrDirectory> openHandles = new();
    private readonly SFTPRoot root;

    /// <summary>
    /// Server extensions to announce to clients.
    /// </summary>
    public SFTPExtensions ServerExtensions { get; set; }

    /// <exception cref="ArgumentException">If the root path is null or empty.</exception>
    public DefaultSFTPHandler(SFTPPath root)
    {
        this.root = new SFTPRoot(root.Path);
        ServerExtensions = new SFTPExtensions(
            new Dictionary<string, string>() { { Extensions.POSIX_RENAME, "1" }, { Extensions.OPEN_DIR_EAGER, "1" } }
        );
    }

    /// <inheritdoc/>
    public virtual Task<SFTPExtensions> Init(
        uint clientVersion,
        SFTPExtensions extensions,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(ServerExtensions);

    /// <inheritdoc/>
    public virtual Task<byte[]> Open(
        SFTPPath path,
        FileMode fileMode,
        FileAccess fileAccess,
        SFTPAttributes attributes,
        CancellationToken cancellationToken = default
    )
    {
        string physicalPath = GetPhysicalPath(path);
        try
        {
            byte[] handle = openHandles.Add(
                new OpenSFTPFile(File.Open(physicalPath, fileMode, fileAccess, FileShare.ReadWrite), fileMode)
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
        if (!openHandles.Remove(handle))
        {
            throw new HandlerException(Status.Failure, "No such handle");
        }
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
        OpenSFTPFile file = RequireFile(handle);
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
        OpenSFTPFile file = RequireFile(handle);
        if (file.FileMode == FileMode.Append)
        {
            await file.StreamSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await file.Stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                file.StreamSemaphore.Release();
            }
        }
        else
        {
            await RandomAccess
                .WriteAsync(file.Stream.SafeFileHandle, data.AsMemory(), (long)offset, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public virtual Task<SFTPAttributes> LStat(SFTPPath path, CancellationToken cancellationToken = default)
    {
        if (!TryGetFSObject(path, out var fso))
        {
            throw new HandlerException(Status.NoSuchFile);
        }
        return Task.FromResult(SFTPAttributes.FromFileSystemInfo(fso));
    }

    /// <inheritdoc/>
    public virtual Task<SFTPAttributes> FStat(byte[] handle, CancellationToken cancellationToken = default)
    {
        if (!openHandles.TryGet(handle, out OpenSFTPFileOrDirectory? openFile))
        {
            throw new HandlerException(Status.NoSuchFile);
        }
        if (openFile is OpenSFTPDirectory openDirectory)
        {
            return Stat(new SFTPPath(openDirectory.Path), cancellationToken);
        }
        SafeFileHandle fileHandle = ((OpenSFTPFile)openFile).Stream.SafeFileHandle;
        return Task.FromResult(
            new SFTPAttributes()
            {
                FileSize = (ulong)RandomAccess.GetLength(fileHandle),
                User = SFTPUser.Root,
                Group = SFTPGroup.Root,
                Permissions = PosixFileMode.DefaultFile,
                LastAccessedTime = File.GetLastAccessTimeUtc(fileHandle),
                LastModifiedTime = File.GetLastWriteTimeUtc(fileHandle),
            }
        );
    }

    /// <inheritdoc/>
    public virtual Task SetStat(SFTPPath path, SFTPAttributes attributes, CancellationToken cancellationToken = default)
    {
        if (!TryGetFSObject(path, out FileSystemInfo? fileSystemInfo))
        {
            throw new HandlerException(Status.NoSuchFile);
        }
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
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual Task FSetStat(
        byte[] handle,
        SFTPAttributes attributes,
        CancellationToken cancellationToken = default
    )
    {
        if (!openHandles.TryGet(handle, out OpenSFTPFileOrDirectory? openFile))
        {
            throw new HandlerException(Status.NoSuchFile);
        }
        if (openFile is OpenSFTPDirectory openDirectory)
        {
            return SetStat(new SFTPPath(openDirectory.Path), attributes, cancellationToken);
        }
        SafeFileHandle fileHandle = ((OpenSFTPFile)openFile).Stream.SafeFileHandle;
        if (attributes.FileSize.HasValue)
        {
            RandomAccess.SetLength(fileHandle, (long)attributes.FileSize);
        }
        if (attributes.LastAccessedTime.HasValue)
        {
            File.SetLastAccessTimeUtc(fileHandle, attributes.LastAccessedTime.Value.UtcDateTime);
        }
        if (attributes.LastModifiedTime.HasValue)
        {
            File.SetLastWriteTimeUtc(fileHandle, attributes.LastModifiedTime.Value.UtcDateTime);
        }
        // TODO: Read/Write/Execute... etc.
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual Task<byte[]> OpenDir(SFTPPath path, CancellationToken cancellationToken = default)
    {
        DirectoryInfo directoryInfo = new(GetPhysicalPath(path));
        IEnumerable<FileSystemInfo> fileSystemInfos;
        try
        {
            fileSystemInfos = directoryInfo.EnumerateFileSystemInfos();
        }
        catch (DirectoryNotFoundException ex)
        {
            throw new HandlerException(Status.NoSuchFile, null, ex);
        }
        return Task.FromResult(
            openHandles.Add(
                new OpenSFTPDirectory(
                    directoryInfo.FullName,
                    self => fileSystemInfos.Select(fso => SFTPName.FromFileSystemInfo(fso))
                )
            )
        );
    }

    /// <inheritdoc/>
    public virtual Task<IEnumerator<SFTPName>> ReadDir(byte[] handle, CancellationToken cancellationToken = default)
    {
        return Task.FromResult((IEnumerator<SFTPName>)RequireDirectory(handle));
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
        string physicalPath = GetPhysicalPath(path);
        DirectoryInfo? parent = Directory.GetParent(physicalPath);
        if (parent != null && !parent.Exists)
        {
            throw new HandlerException(Status.NoSuchFile);
        }
        if (Directory.Exists(physicalPath))
        {
            throw new HandlerException(Status.Failure, "Directory exists");
        }
        Directory.CreateDirectory(physicalPath);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual Task RemoveDir(SFTPPath path, CancellationToken cancellationToken = default)
    {
        if (root.Normalize(path.Path) == "/")
        {
            throw new HandlerException(Status.PermissionDenied);
        }
        if (TryGetFSObject(path, out var fsObject) && fsObject is DirectoryInfo)
        {
            Directory.Delete(fsObject.FullName);
            return Task.CompletedTask;
        }
        throw new HandlerException(Status.NoSuchFile);
    }

    /// <inheritdoc/>
    public virtual Task<SFTPPath> RealPath(SFTPPath path, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new SFTPPath(root.Normalize(path.Path)));
    }

    /// <inheritdoc/>
    public virtual Task<SFTPAttributes> Stat(SFTPPath path, CancellationToken cancellationToken = default) =>
        LStat(path, cancellationToken);

    /// <inheritdoc/>
    public virtual Task Rename(SFTPPath oldPath, SFTPPath newPath, CancellationToken cancellationToken = default)
    {
        Rename(oldPath, newPath, allowOverwrite: false);
        return Task.CompletedTask;
    }

    private void Rename(SFTPPath oldPath, SFTPPath newPath, bool allowOverwrite)
    {
        if (!TryGetFSObject(oldPath, out FileSystemInfo? fsOldObject))
        {
            throw new HandlerException(Status.NoSuchFile);
        }
        string newPhysicalPath = GetPhysicalPath(newPath);
        if (fsOldObject is FileInfo)
        {
            File.Move(fsOldObject.FullName, newPhysicalPath, allowOverwrite);
        }
        else
        {
            if (allowOverwrite)
            {
                try
                {
                    Directory.Delete(newPhysicalPath);
                }
                catch (DirectoryNotFoundException)
                {
                    // Good, no need to delete
                }
            }
            Directory.Move(fsOldObject.FullName, newPhysicalPath);
        }
    }

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

    /// <inheritdoc/>
    public virtual async Task<SFTPResponse> Extended(
        uint requestId,
        string requestName,
        SshStreamReader reader,
        CancellationToken cancellationToken = default
    )
    {
        switch (requestName)
        {
            case SFTPPosixRenameRequest.REQUEST_NAME:
            {
                SFTPPosixRenameRequest request = await SFTPPosixRenameRequest
                    .DeserializeAsync(requestId, reader, cancellationToken)
                    .ConfigureAwait(false);
                Rename(new SFTPPath(request.OldPath), new SFTPPath(request.NewPath), allowOverwrite: true);
                return new SFTPStatus(requestId, Status.Ok);
            }
            case SFTPOpenDirEagerRequest.REQUEST_NAME:
            {
                SFTPOpenDirEagerRequest request = await SFTPOpenDirEagerRequest
                    .DeserializeAsync(requestId, reader, cancellationToken)
                    .ConfigureAwait(false);
                byte[] handle = await OpenDir(new SFTPPath(request.Path), cancellationToken).ConfigureAwait(false);
                try
                {
                    IEnumerator<SFTPName> enumerator = await ReadDir(handle, cancellationToken).ConfigureAwait(false);
                    List<SFTPName> results = [];
                    for (int i = 0; i < SFTPServer.READ_DIR_PAGE_SIZE && enumerator.MoveNext(); i++)
                    {
                        results.Add(enumerator.Current);
                    }
                    if (results.Count < SFTPServer.READ_DIR_PAGE_SIZE)
                    {
                        await Close(handle, cancellationToken).ConfigureAwait(false);
                        handle = [];
                    }
                    return new SFTPOpenDirEagerResponse(requestId, handle, results);
                }
                catch
                {
                    if (handle.Length > 0)
                    {
                        await Close(handle, cancellationToken).ConfigureAwait(false);
                    }
                    throw;
                }
            }
            default:
                throw new HandlerException(Status.OperationUnsupported);
        }
    }

    /// <exception cref="HandlerException"/>
    public virtual string GetPhysicalPath(SFTPPath path)
    {
        return root.GetPhysicalPath(path.Path);
    }

    private bool TryGetFSObject(SFTPPath path, [NotNullWhen(true)] out FileSystemInfo? fileSystemObject)
    {
        string resolved = GetPhysicalPath(path);
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

    /// <summary>
    /// Throws an <see cref="HandlerException"/> with <see cref="Status.NoSuchFile"/> if the given handle does not correspond to an open file.
    /// </summary>
    /// <returns>The matching open file.</returns>
    /// <exception cref="HandlerException"/>
    private OpenSFTPFile RequireFile(byte[] handle)
    {
        if (
            !openHandles.TryGet(handle, out OpenSFTPFileOrDirectory? fileOrDirectory)
            || fileOrDirectory is not OpenSFTPFile file
        )
        {
            throw new HandlerException(Status.NoSuchFile);
        }
        return file;
    }

    /// <summary>
    /// Throws an <see cref="HandlerException"/> with <see cref="Status.NoSuchFile"/> if the given handle does not correspond to an open directory.
    /// </summary>
    /// <returns>The matching open directory.</returns>
    /// <exception cref="HandlerException"/>
    private OpenSFTPDirectory RequireDirectory(byte[] handle)
    {
        if (
            !openHandles.TryGet(handle, out OpenSFTPFileOrDirectory? fileOrDirectory)
            || fileOrDirectory is not OpenSFTPDirectory directory
        )
        {
            throw new HandlerException(Status.NoSuchFile);
        }
        return directory;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        openHandles.Dispose();
    }
}
