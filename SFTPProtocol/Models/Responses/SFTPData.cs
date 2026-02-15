using System;
using System.Threading;
using System.Threading.Tasks;
using JustSFTP.Protocol.Enums;
using JustSFTP.Protocol.IO;

namespace JustSFTP.Protocol.Models.Responses;

/// <summary>
/// SSH_FXP_DATA
/// </summary>
public record SFTPData(uint RequestId, byte[] Data) : SFTPResponse(RequestId)
{
    /// <inheritdoc/>
    public override ResponseType ResponseType => ResponseType.Data;

    /// <inheritdoc/>
    public override async Task WriteAsync(
        SshStreamWriter writer,
        CancellationToken cancellationToken
    )
    {
        await base.WriteAsync(writer, cancellationToken).ConfigureAwait(false);
        await writer.Write(Data.Length, cancellationToken).ConfigureAwait(false);
        await writer.Write(Data, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deserialize an <see cref="SFTPData"/> from the given stream.
    /// </summary>
    /// <exception cref="OperationCanceledException"/>
    /// <exception cref="ObjectDisposedException"/>
    public static new async Task<SFTPResponse> ReadAsync(
        SshStreamReader reader,
        uint protocolVersion,
        CancellationToken cancellationToken
    )
    {
        uint requestId = await reader.ReadUInt32(cancellationToken).ConfigureAwait(false);
        byte[] data = await reader.ReadBinary(cancellationToken).ConfigureAwait(false);
        return new SFTPData(requestId, data);
    }
}
