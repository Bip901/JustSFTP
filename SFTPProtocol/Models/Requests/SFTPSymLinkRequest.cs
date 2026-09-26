using System.Threading;
using System.Threading.Tasks;
using JustSFTP.Protocol.Enums;
using JustSFTP.Protocol.IO;

namespace JustSFTP.Protocol.Models.Requests;

/// <summary>
/// SSH_FXP_SYMLINK.
/// <remarks>
/// The TargetPath and LinkPath argument order follows the OpenSSH mistake rather than the SFTP specification.
/// See <see href="https://github.com/openssh/openssh-portable/blob/master/PROTOCOL">openssh-portable/PROTOCOL 4.1</see>.
/// </remarks>
/// </summary>
public record SFTPSymLinkRequest(uint RequestId, string TargetPath, string LinkPath) : SFTPRequest(RequestId)
{
    /// <inheritdoc/>
    public override RequestType RequestType => RequestType.SymLink;

    /// <inheritdoc/>
    public override async Task WriteAsync(SshStreamWriter writer, CancellationToken cancellationToken)
    {
        await base.WriteAsync(writer, cancellationToken).ConfigureAwait(false);
        await writer.Write(TargetPath, cancellationToken).ConfigureAwait(false);
        await writer.Write(LinkPath, cancellationToken).ConfigureAwait(false);
    }
}
