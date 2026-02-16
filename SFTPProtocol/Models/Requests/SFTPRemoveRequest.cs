using System.Threading;
using System.Threading.Tasks;
using JustSFTP.Protocol.Enums;
using JustSFTP.Protocol.IO;

namespace JustSFTP.Protocol.Models.Responses;

/// <summary>
/// SSH_FXP_REMOVE
/// </summary>
public record SFTPRemoveRequest(uint RequestId, string Path) : SFTPRequest(RequestId)
{
    /// <inheritdoc/>
    public override RequestType RequestType => RequestType.Remove;

    /// <inheritdoc/>
    public override async Task WriteAsync(
        SshStreamWriter writer,
        CancellationToken cancellationToken
    )
    {
        await base.WriteAsync(writer, cancellationToken).ConfigureAwait(false);
        await writer.Write(Path, cancellationToken).ConfigureAwait(false);
    }
}
