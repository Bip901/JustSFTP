using System.Threading;
using System.Threading.Tasks;
using JustSFTP.Protocol.Enums;
using JustSFTP.Protocol.IO;

namespace JustSFTP.Protocol.Models.Responses;

/// <summary>
/// SSH_FXP_READDIR
/// </summary>
public record SFTPReadDirRequest(uint RequestId, string Handle) : SFTPRequest(RequestId)
{
    /// <inheritdoc/>
    public override RequestType RequestType => RequestType.ReadDir;

    /// <inheritdoc/>
    public override async Task WriteAsync(
        SshStreamWriter writer,
        CancellationToken cancellationToken
    )
    {
        await base.WriteAsync(writer, cancellationToken).ConfigureAwait(false);
        await writer.Write(Handle, cancellationToken).ConfigureAwait(false);
    }
}
