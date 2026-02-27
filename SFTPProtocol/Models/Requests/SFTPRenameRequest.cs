using System.Threading;
using System.Threading.Tasks;
using JustSFTP.Protocol.Enums;
using JustSFTP.Protocol.IO;

namespace JustSFTP.Protocol.Models.Requests;

/// <summary>
/// SSH_FXP_RENAME
/// </summary>
public record SFTPRenameRequest(uint RequestId, string OldPath, string NewPath)
    : SFTPRequest(RequestId)
{
    /// <inheritdoc/>
    public override RequestType RequestType => RequestType.Rename;

    /// <inheritdoc/>
    public override async Task WriteAsync(
        SshStreamWriter writer,
        CancellationToken cancellationToken
    )
    {
        await base.WriteAsync(writer, cancellationToken).ConfigureAwait(false);
        await writer.Write(OldPath, cancellationToken).ConfigureAwait(false);
        await writer.Write(NewPath, cancellationToken).ConfigureAwait(false);
    }
}
