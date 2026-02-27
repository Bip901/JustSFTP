using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JustSFTP.Protocol.IO;

namespace JustSFTP.Protocol.Models.Requests.Extended;

/// <summary>
/// posix-rename@openssh.com
/// <para>See: https://libssh2.org/libssh2_sftp_posix_rename_ex.html and https://cvsweb.openbsd.org/cgi-bin/cvsweb/~checkout~/src/usr.bin/ssh/PROTOCOL</para>
/// </summary>
public record SFTPPosixRenameRequest(uint RequestId, string OldPath, string NewPath)
    : SFTPExtendedRequest(RequestId, "posix-rename@openssh.com")
{
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

    /// <summary>
    /// Deserialize an <see cref="SFTPPosixRenameRequest"/> from the given stream.
    /// </summary>
    /// <exception cref="OperationCanceledException"/>
    /// <exception cref="ObjectDisposedException"/>
    public static async Task<SFTPPosixRenameRequest> DeserializeAsync(
        uint requestId,
        Stream stream,
        CancellationToken cancellationToken
    )
    {
        SshStreamReader reader = new(stream);
        string oldPath = await reader.ReadString(cancellationToken).ConfigureAwait(false);
        string newPath = await reader.ReadString(cancellationToken).ConfigureAwait(false);
        return new(requestId, oldPath, newPath);
    }
}
