using System.Threading;
using System.Threading.Tasks;
using JustSFTP.Protocol.Enums;
using JustSFTP.Protocol.IO;

namespace JustSFTP.Protocol.Models.Responses;

/// <summary>
/// SSH_FXP_SYMLINK
/// </summary>
public record SFTPSymLinkRequest(uint RequestId, string TargetPath, string LinkPath)
    : SFTPRequest(RequestId)
{
    /// <inheritdoc/>
    public override RequestType RequestType => RequestType.SymLink;

    /// <inheritdoc/>
    public override async Task WriteAsync(
        SshStreamWriter writer,
        CancellationToken cancellationToken
    )
    {
        await base.WriteAsync(writer, cancellationToken).ConfigureAwait(false);
        await writer.Write(TargetPath, cancellationToken).ConfigureAwait(false);
        await writer.Write(LinkPath, cancellationToken).ConfigureAwait(false);
    }
}
