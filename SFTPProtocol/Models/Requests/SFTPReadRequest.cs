using System.Threading;
using System.Threading.Tasks;
using JustSFTP.Protocol.Enums;
using JustSFTP.Protocol.IO;

namespace JustSFTP.Protocol.Models.Requests;

/// <summary>
/// SSH_FXP_READ
/// </summary>
public record SFTPReadRequest(uint RequestId, byte[] Handle, ulong Offset, uint Length)
    : SFTPRequest(RequestId)
{
    /// <inheritdoc/>
    public override RequestType RequestType => RequestType.Read;

    /// <inheritdoc/>
    public override async Task WriteAsync(
        SshStreamWriter writer,
        CancellationToken cancellationToken
    )
    {
        await base.WriteAsync(writer, cancellationToken).ConfigureAwait(false);
        await writer.Write(Handle.Length, cancellationToken).ConfigureAwait(false);
        await writer.Write(Handle, cancellationToken).ConfigureAwait(false);
        await writer.Write(Offset, cancellationToken).ConfigureAwait(false);
        await writer.Write(Length, cancellationToken).ConfigureAwait(false);
    }
}
