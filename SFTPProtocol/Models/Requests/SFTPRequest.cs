using System;
using System.Threading;
using System.Threading.Tasks;
using JustSFTP.Protocol.Enums;
using JustSFTP.Protocol.IO;

namespace JustSFTP.Protocol.Models.Requests;

/// <summary>
/// Represents an SFTP request from client to server.
/// </summary>
public abstract record SFTPRequest(uint RequestId)
{
    /// <summary>
    /// The type of this request.
    /// </summary>
    public abstract RequestType RequestType { get; }

    /// <summary>
    /// Serialize this response to the given stream.
    /// </summary>
    /// <exception cref="OperationCanceledException"/>
    /// <exception cref="ObjectDisposedException"/>
    public virtual async Task WriteAsync(
        SshStreamWriter writer,
        CancellationToken cancellationToken
    )
    {
        await writer.Write(RequestType, cancellationToken).ConfigureAwait(false);
        await writer.Write(RequestId, cancellationToken).ConfigureAwait(false);
    }
}
