using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JustSFTP.Protocol;
using JustSFTP.Protocol.Enums;
using JustSFTP.Protocol.Models;
using JustSFTP.Protocol.Models.Responses;

namespace JustSFTP.Server;

public interface ISFTPHandler
{
    Task<SFTPExtensions> Init(
        uint clientVersion,
        SFTPExtensions extensions,
        CancellationToken cancellationToken = default
    );

    Task<byte[]> Open(
        SFTPPath path,
        FileMode fileMode,
        FileAccess fileAccess,
        SFTPAttributes attributes,
        CancellationToken cancellationToken = default
    );

    Task Close(byte[] handle, CancellationToken cancellationToken = default);

    /// <exception cref="HandlerException"/>
    /// <exception cref="Exception"/>
    Task<byte[]> Read(
        byte[] handle,
        ulong offset,
        uint length,
        CancellationToken cancellationToken = default
    );

    Task Write(
        byte[] handle,
        ulong offset,
        byte[] data,
        CancellationToken cancellationToken = default
    );

    Task<SFTPAttributes> LStat(SFTPPath path, CancellationToken cancellationToken = default);

    Task<SFTPAttributes> FStat(byte[] handle, CancellationToken cancellationToken = default);

    Task SetStat(
        SFTPPath path,
        SFTPAttributes attributes,
        CancellationToken cancellationToken = default
    );

    Task FSetStat(
        byte[] handle,
        SFTPAttributes attributes,
        CancellationToken cancellationToken = default
    );

    Task<byte[]> OpenDir(SFTPPath path, CancellationToken cancellationToken = default);

    Task<IEnumerator<SFTPName>> ReadDir(
        byte[] handle,
        CancellationToken cancellationToken = default
    );

    Task Remove(SFTPPath path, CancellationToken cancellationToken = default);

    Task MakeDir(
        SFTPPath path,
        SFTPAttributes attributes,
        CancellationToken cancellationToken = default
    );

    Task RemoveDir(SFTPPath path, CancellationToken cancellationToken = default);

    Task<SFTPPath> RealPath(SFTPPath path, CancellationToken cancellationToken = default);

    Task<SFTPAttributes> Stat(SFTPPath path, CancellationToken cancellationToken = default);

    Task Rename(SFTPPath oldPath, SFTPPath newPath, CancellationToken cancellationToken = default);

#if NET6_0_OR_GREATER
    Task<SFTPName> ReadLink(SFTPPath path, CancellationToken cancellationToken = default);

    Task SymLink(
        SFTPPath linkPath,
        SFTPPath targetPath,
        CancellationToken cancellationToken = default
    );
#endif

    /// <summary>
    /// Handle any vendor-specific SFTP extensions.
    /// </summary>
    /// <remarks>Throw HandlerException(Status.OperationUnsupported) if you don't recognize the request name.</remarks>
    /// <exception cref="HandlerException"/>
    Task<SFTPResponse> Extended(
        string requestName,
        Stream restOfRequest,
        CancellationToken cancellationToken = default
    )
    {
        throw new HandlerException(Status.OperationUnsupported);
    }
}
