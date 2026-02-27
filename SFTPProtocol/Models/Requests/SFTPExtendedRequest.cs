using System.Threading;
using System.Threading.Tasks;
using JustSFTP.Protocol.Enums;
using JustSFTP.Protocol.IO;

namespace JustSFTP.Protocol.Models.Responses;

/// <summary>
/// SSH_FXP_EXTENDED
/// </summary>
/// <param name="RequestId">The request id.</param>
/// <param name="RequestName">A string of the format name@domain, where domain is an internet domain name of the vendor defining the request.</param>
public abstract record SFTPExtendedRequest(uint RequestId, string RequestName) : SFTPRequest(RequestId)
{
    /// <inheritdoc/>
    public override RequestType RequestType => RequestType.Extended;

    /// <inheritdoc/>
    public override async Task WriteAsync(
        SshStreamWriter writer,
        CancellationToken cancellationToken
    )
    {
        await base.WriteAsync(writer, cancellationToken).ConfigureAwait(false);
        await writer.Write(RequestName, cancellationToken).ConfigureAwait(false);
    }
}
