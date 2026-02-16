using System.Threading;
using System.Threading.Tasks;
using JustSFTP.Protocol.Enums;
using JustSFTP.Protocol.IO;

namespace JustSFTP.Protocol.Models.Responses;

/// <summary>
/// SSH_FXP_MKDIR
/// </summary>
public record SFTPMakeDirRequest(uint RequestId, string Path, SFTPAttributes Attrs)
    : SFTPRequest(RequestId)
{
    /// <inheritdoc/>
    public override RequestType RequestType => RequestType.MakeDir;

    /// <inheritdoc/>
    public override async Task WriteAsync(
        SshStreamWriter writer,
        CancellationToken cancellationToken
    )
    {
        await base.WriteAsync(writer, cancellationToken).ConfigureAwait(false);
        await writer.Write(Path, cancellationToken).ConfigureAwait(false);
        await writer.Write(Attrs, PFlags.DEFAULT, cancellationToken).ConfigureAwait(false);
    }
}
