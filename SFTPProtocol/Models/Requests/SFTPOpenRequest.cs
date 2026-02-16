using System;
using System.Threading;
using System.Threading.Tasks;
using JustSFTP.Protocol.Enums;
using JustSFTP.Protocol.IO;

namespace JustSFTP.Protocol.Models.Responses;

/// <summary>
/// SSH_FXP_OPEN
/// </summary>
public record SFTPOpenRequest(
    uint RequestId,
    string Path,
    AccessFlags Flags,
    SFTPAttributes Attributes
) : SFTPRequest(RequestId)
{
    /// <inheritdoc/>
    public override RequestType RequestType => RequestType.Open;

    /// <inheritdoc/>
    public override async Task WriteAsync(
        SshStreamWriter writer,
        CancellationToken cancellationToken
    )
    {
        await base.WriteAsync(writer, cancellationToken).ConfigureAwait(false);
        await writer.Write(Path, cancellationToken);
        await writer.Write((uint)Flags, cancellationToken);
        await writer.Write(Attributes, PFlags.DEFAULT, cancellationToken);
    }
}
