using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JustSFTP.Protocol.Enums;
using JustSFTP.Protocol.Models;

namespace JustSFTP.Protocol.IO;

/// <summary>
/// Reads SFTP data from any underlying stream.
/// </summary>
public class SshStreamReader
{
    public static readonly Encoding SFTPStringEncoding = new UTF8Encoding(false);

    /// <summary>
    /// The underlying stream.
    /// </summary>
    public Stream Stream { get; }

    /// <summary>
    /// Creates a new <see cref="SshStreamReader"/> that reads from the given stream.
    /// </summary>
    /// <exception cref="ArgumentNullException"></exception>
    public SshStreamReader(Stream stream)
    {
        Stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    public async Task<byte> ReadByte(CancellationToken cancellationToken = default) =>
        (await ReadBinary(1, cancellationToken).ConfigureAwait(false))[0];

    public async Task<uint> ReadUInt32(CancellationToken cancellationToken = default) =>
        BinaryPrimitives.ReadUInt32BigEndian(
            await ReadBinary(4, cancellationToken).ConfigureAwait(false)
        );

    public async Task<ulong> ReadUInt64(CancellationToken cancellationToken = default) =>
        BinaryPrimitives.ReadUInt64BigEndian(
            await ReadBinary(8, cancellationToken).ConfigureAwait(false)
        );

    public async Task<string> ReadString(CancellationToken cancellationToken = default)
    {
        return SFTPStringEncoding.GetString(
            await ReadBinary(cancellationToken).ConfigureAwait(false)
        );
    }

    public async Task<(string value, int totalLength)> ReadStringAndLength(
        CancellationToken cancellationToken = default
    )
    {
        byte[] encodedBytes = await ReadBinary(cancellationToken).ConfigureAwait(false);
        return (SFTPStringEncoding.GetString(encodedBytes), sizeof(uint) + encodedBytes.Length);
    }

    public async Task<AccessFlags> ReadAccessFlags(CancellationToken cancellationToken = default) =>
        (AccessFlags)await ReadUInt32(cancellationToken).ConfigureAwait(false);

    public async Task<DateTimeOffset> ReadTime(CancellationToken cancellationToken = default)
    {
        var seconds = await ReadUInt32(cancellationToken).ConfigureAwait(false);
        return seconds > 0 ? DateTimeOffset.FromUnixTimeSeconds(seconds) : DateTimeOffset.MinValue;
    }

    public async Task<SFTPAttributes> ReadAttributes(CancellationToken cancellationToken = default)
    {
        PFlags flags = (PFlags)await ReadUInt32(cancellationToken).ConfigureAwait(false);
        ulong? size = flags.HasFlag(PFlags.Size)
            ? await ReadUInt64(cancellationToken).ConfigureAwait(false)
            : null;
        uint? owner = flags.HasFlag(PFlags.UidGid)
            ? await ReadUInt32(cancellationToken).ConfigureAwait(false)
            : null;
        uint? group = flags.HasFlag(PFlags.UidGid)
            ? await ReadUInt32(cancellationToken).ConfigureAwait(false)
            : null;
        Permissions? permissions = flags.HasFlag(PFlags.Permissions)
            ? (Permissions)await ReadUInt32(cancellationToken).ConfigureAwait(false)
            : null;
        DateTimeOffset? atime = flags.HasFlag(PFlags.AccessModifiedTime)
            ? await ReadTime(cancellationToken).ConfigureAwait(false)
            : null;
        DateTimeOffset? mtime = flags.HasFlag(PFlags.AccessModifiedTime)
            ? await ReadTime(cancellationToken).ConfigureAwait(false)
            : null;
        Dictionary<string, string>? extendedAttributes = null;
        if (flags.HasFlag(PFlags.Extended))
        {
            uint extendedCount = await ReadUInt32(cancellationToken).ConfigureAwait(false);
            extendedAttributes = new Dictionary<string, string>((int)extendedCount);
            for (var i = 0; i < extendedCount; i++)
            {
                var type = await ReadString(cancellationToken).ConfigureAwait(false);
                var data = await ReadString(cancellationToken).ConfigureAwait(false);
                extendedAttributes.Add(type, data);
            }
        }
        return new SFTPAttributes()
        {
            FileSize = size,
            User = owner == null ? null : new SFTPUser(owner.Value),
            Group = group == null ? null : new SFTPGroup(group.Value),
            Permissions = permissions,
            LastAccessedTime = atime,
            LastModifiedTime = mtime,
            ExtendedAttributes = extendedAttributes,
        };
    }

    public async Task<byte[]> ReadBinary(CancellationToken cancellationToken = default) =>
        await ReadBinary(
                (int)await ReadUInt32(cancellationToken).ConfigureAwait(false),
                cancellationToken
            )
            .ConfigureAwait(false);

    public async Task<byte[]> ReadBinary(int length, CancellationToken cancellationToken = default)
    {
        if (length == 0)
        {
            return Array.Empty<byte>();
        }
        byte[] buffer = new byte[length];
        int offset = 0;
        while (offset < length)
        {
            int bytesRead = await Stream
                .ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken)
                .ConfigureAwait(false);
            if (bytesRead == 0)
            {
                throw new EndOfStreamException(
                    $"Unexpected end of stream while reading {length - offset}/{length} bytes"
                );
            }
            offset += bytesRead;
        }

        return buffer;
    }
}
