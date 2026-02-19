using System;
using System.Threading;
using System.Threading.Tasks;
using JustSFTP.Protocol.Enums;
using JustSFTP.Protocol.IO;

namespace JustSFTP.Protocol.Models.Responses;

/// <summary>
/// SSH_FXP_HANDLE
/// </summary>
public record SFTPHandleResponse(uint RequestId, byte[] Handle) : SFTPResponse(RequestId)
{
    /// <inheritdoc/>
    public override ResponseType ResponseType => ResponseType.Handle;

    /// <inheritdoc/>
    public override async Task WriteAsync(
        SshStreamWriter writer,
        CancellationToken cancellationToken
    )
    {
        await base.WriteAsync(writer, cancellationToken).ConfigureAwait(false);
        await writer.Write(Handle.Length, cancellationToken).ConfigureAwait(false);
        await writer.Write(Handle, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deserialize an <see cref="SFTPHandleResponse"/> from the given stream.
    /// </summary>
    /// <exception cref="OperationCanceledException"/>
    /// <exception cref="ObjectDisposedException"/>
    public static new async Task<SFTPResponse> ReadAsync(
        SshStreamReader reader,
        CancellationToken cancellationToken
    )
    {
        uint requestId = await reader.ReadUInt32(cancellationToken).ConfigureAwait(false);
        byte[] handle = await reader.ReadBinary(cancellationToken).ConfigureAwait(false);
        return new SFTPHandleResponse(requestId, handle);
    }
}
