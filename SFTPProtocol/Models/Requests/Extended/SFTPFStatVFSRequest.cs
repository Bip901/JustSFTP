using System;
using System.Threading;
using System.Threading.Tasks;
using JustSFTP.Protocol.IO;

namespace JustSFTP.Protocol.Models.Requests.Extended;

/// <summary>
/// fstatvfs@openssh.com
/// <para>See: https://libssh2.org/libssh2_sftp_fstatvfs.html</para>
/// </summary>
public record SFTPFStatVFSRequest(uint RequestId, byte[] Handle) : SFTPExtendedRequest(RequestId, REQUEST_NAME)
{
    /// <summary>
    /// The name of this extended request.
    /// </summary>
    public const string REQUEST_NAME = "fstatvfs@openssh.com";

    /// <inheritdoc/>
    public override async Task WriteAsync(SshStreamWriter writer, CancellationToken cancellationToken)
    {
        await base.WriteAsync(writer, cancellationToken).ConfigureAwait(false);
        await writer.Write(Handle.Length, cancellationToken).ConfigureAwait(false);
        await writer.Write(Handle, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deserialize an <see cref="SFTPFStatVFSRequest"/> from the given stream reader.
    /// </summary>
    /// <exception cref="OperationCanceledException"/>
    public static async Task<SFTPFStatVFSRequest> DeserializeAsync(
        uint requestId,
        SshStreamReader reader,
        CancellationToken cancellationToken
    )
    {
        byte[] handle = await reader.ReadBinary(cancellationToken).ConfigureAwait(false);
        return new(requestId, handle);
    }
}
