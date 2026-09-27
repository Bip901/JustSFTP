using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JustSFTP.Protocol.IO;

namespace JustSFTP.Protocol.Models.Requests.Extended;

/// <summary>
/// open-dir-eager@justsftp
/// <para>
/// Bundles 4 round-trip requests (open, read, read, close) into one, reducing directory listing latency by up to 75%.
/// </para>
/// </summary>
public record SFTPOpenDirEagerRequest(uint RequestId, string Path) : SFTPExtendedRequest(RequestId, REQUEST_NAME)
{
    /// <summary>
    /// The name of this extended request.
    /// </summary>
    public const string REQUEST_NAME = "open-dir-eager@justsftp";

    /// <inheritdoc/>
    public override async Task WriteAsync(SshStreamWriter writer, CancellationToken cancellationToken)
    {
        await base.WriteAsync(writer, cancellationToken).ConfigureAwait(false);
        await writer.Write(Path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deserialize an <see cref="SFTPOpenDirEagerRequest"/> from the given stream reader.
    /// </summary>
    /// <exception cref="OperationCanceledException"/>
    public static async Task<SFTPOpenDirEagerRequest> DeserializeAsync(
        uint requestId,
        SshStreamReader reader,
        CancellationToken cancellationToken
    )
    {
        string path = await reader.ReadString(cancellationToken).ConfigureAwait(false);
        return new(requestId, path);
    }
}
