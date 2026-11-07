using System;
using System.Threading;
using System.Threading.Tasks;
using JustSFTP.Protocol.Enums;
using JustSFTP.Protocol.IO;

namespace JustSFTP.Protocol.Models.Responses.Extended;

/// <summary>
/// LIBSSH2_SFTP_STATVFS.
/// The response of a <see cref="Requests.Extended.SFTPStatVFSRequest"/> and <see cref="Requests.Extended.SFTPFStatVFSRequest"/>.
/// It is unspecified whether all members of the returned struct have meaningful values on all file systems.
/// </summary>
/// <param name="RequestId">SFTP request identifier.</param>
/// <param name="BlockSize">f_bsize - file system block size.</param>
/// <param name="FragmentSize">Fragment size.</param>
/// <param name="Blocks">Size of file system in fragment size units.</param>
/// <param name="FreeBlocks">Number of free blocks.</param>
/// <param name="AvailableBlocks">Number of free blocks for non-root.</param>
/// <param name="Files">Number of inodes.</param>
/// <param name="FreeINodes">Number of free inodes.</param>
/// <param name="AvailableINodes">Number of free inodes for non-root.</param>
/// <param name="FileSystemId">File system ID.</param>
/// <param name="Flags">Mount flags.</param>
/// <param name="MaxFilenameLength">Maximum filename length.</param>
public record SFTPStatVFS(
    uint RequestId,
    ulong BlockSize,
    ulong FragmentSize,
    ulong Blocks,
    ulong FreeBlocks,
    ulong AvailableBlocks,
    ulong Files,
    ulong FreeINodes,
    ulong AvailableINodes,
    ulong FileSystemId,
    SFTPStatVFS.MountFlags Flags,
    ulong MaxFilenameLength
) : SFTPResponse(RequestId)
{
    /// <summary>
    /// Mount flags (f_flag).
    /// </summary>
    [Flags]
    public enum MountFlags
    {
        /// <summary>
        /// Zero.
        /// </summary>
        None = 0,

        /// <summary>
        /// LIBSSH2_SFTP_ST_RDONLY: Read-only file system.
        /// </summary>
        ReadOnly = 1,

        /// <summary>
        /// LIBSSH2_SFTP_ST_NOSUID: Set-user-ID/set-group-ID bits are ignored by exec.
        /// </summary>
        NoSUID = 2,
    }

    /// <inheritdoc/>
    public override ResponseType ResponseType => ResponseType.Extended;

    /// <inheritdoc/>
    public override async Task WriteAsync(SshStreamWriter writer, CancellationToken cancellationToken)
    {
        await base.WriteAsync(writer, cancellationToken).ConfigureAwait(false);
        await writer.Write(BlockSize, cancellationToken).ConfigureAwait(false);
        await writer.Write(FragmentSize, cancellationToken).ConfigureAwait(false);
        await writer.Write(Blocks, cancellationToken).ConfigureAwait(false);
        await writer.Write(FreeBlocks, cancellationToken).ConfigureAwait(false);
        await writer.Write(AvailableBlocks, cancellationToken).ConfigureAwait(false);
        await writer.Write(Files, cancellationToken).ConfigureAwait(false);
        await writer.Write(FreeINodes, cancellationToken).ConfigureAwait(false);
        await writer.Write(AvailableINodes, cancellationToken).ConfigureAwait(false);
        await writer.Write(FileSystemId, cancellationToken).ConfigureAwait(false);
        await writer.Write((ulong)Flags, cancellationToken).ConfigureAwait(false);
        await writer.Write(MaxFilenameLength, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deserialize an <see cref="SFTPStatVFS"/> from the given stream.
    /// </summary>
    /// <exception cref="OperationCanceledException"/>
    /// <exception cref="ObjectDisposedException"/>
    public static async Task<SFTPResponse> ReadAsync(
        uint requestId,
        SshStreamReader reader,
        CancellationToken cancellationToken
    )
    {
        ulong blockSize = await reader.ReadUInt64(cancellationToken).ConfigureAwait(false);
        ulong fragmentSize = await reader.ReadUInt64(cancellationToken).ConfigureAwait(false);
        ulong blocks = await reader.ReadUInt64(cancellationToken).ConfigureAwait(false);
        ulong freeBlocks = await reader.ReadUInt64(cancellationToken).ConfigureAwait(false);
        ulong availableBlocks = await reader.ReadUInt64(cancellationToken).ConfigureAwait(false);
        ulong files = await reader.ReadUInt64(cancellationToken).ConfigureAwait(false);
        ulong freeINodes = await reader.ReadUInt64(cancellationToken).ConfigureAwait(false);
        ulong availableINodes = await reader.ReadUInt64(cancellationToken).ConfigureAwait(false);
        ulong fileSystemId = await reader.ReadUInt64(cancellationToken).ConfigureAwait(false);
        MountFlags flags = (MountFlags)await reader.ReadUInt64(cancellationToken).ConfigureAwait(false);
        ulong maxFilenameLength = await reader.ReadUInt64(cancellationToken).ConfigureAwait(false);

        return new SFTPStatVFS(
            requestId,
            blockSize,
            fragmentSize,
            blocks,
            freeBlocks,
            availableBlocks,
            files,
            freeINodes,
            availableINodes,
            fileSystemId,
            flags,
            maxFilenameLength
        );
    }
}
