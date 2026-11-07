using System;
using System.Threading;
using System.Threading.Tasks;
using JustSFTP.Protocol.IO;

namespace JustSFTP.Protocol.Models.Requests.Extended;

/// <summary>
/// statvfs@openssh.com
/// <para>See: https://libssh2.org/libssh2_sftp_statvfs.html</para>
/// </summary>
public record SFTPStatVFSRequest(uint RequestId, string Path) : SFTPExtendedRequest(RequestId, REQUEST_NAME)
{
    /// <summary>
    /// The name of this extended request.
    /// </summary>
    public const string REQUEST_NAME = "statvfs@openssh.com";

    /// <inheritdoc/>
    public override async Task WriteAsync(SshStreamWriter writer, CancellationToken cancellationToken)
    {
        await base.WriteAsync(writer, cancellationToken).ConfigureAwait(false);
        await writer.Write(Path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deserialize an <see cref="SFTPStatVFSRequest"/> from the given stream reader.
    /// </summary>
    /// <exception cref="OperationCanceledException"/>
    public static async Task<SFTPStatVFSRequest> DeserializeAsync(
        uint requestId,
        SshStreamReader reader,
        CancellationToken cancellationToken
    )
    {
        string path = await reader.ReadString(cancellationToken).ConfigureAwait(false);
        return new(requestId, path);
    }
}
