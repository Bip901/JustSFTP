using System;
using System.Threading;
using System.Threading.Tasks;
using JustSFTP.Protocol.Enums;
using JustSFTP.Protocol.IO;

namespace JustSFTP.Protocol.Models.Responses;

/// <summary>
/// SSH_FXP_ATTRS
/// </summary>
public record SFTPAttributesResponse(uint RequestId, SFTPAttributes Attrs) : SFTPResponse(RequestId)
{
    /// <inheritdoc/>
    public override ResponseType ResponseType => ResponseType.Attributes;

    /// <inheritdoc/>
    public override async Task WriteAsync(
        SshStreamWriter writer,
        CancellationToken cancellationToken
    )
    {
        await base.WriteAsync(writer, cancellationToken).ConfigureAwait(false);
        await writer.Write(Attrs, PFlags.DEFAULT, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deserialize an <see cref="SFTPAttributesResponse"/> from the given stream.
    /// </summary>
    /// <exception cref="OperationCanceledException"/>
    /// <exception cref="ObjectDisposedException"/>
    public static new async Task<SFTPResponse> ReadAsync(
        SshStreamReader reader,
        CancellationToken cancellationToken
    )
    {
        uint requestId = await reader.ReadUInt32(cancellationToken).ConfigureAwait(false);
        SFTPAttributes attrs = await reader.ReadAttributes(cancellationToken).ConfigureAwait(false);
        return new SFTPAttributesResponse(requestId, attrs);
    }
}
