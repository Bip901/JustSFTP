using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
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
    /// <summary>
    /// The underlying stream.
    /// </summary>
    public Stream Stream { get; }

    /// <summary>
    /// The length after which to consider the stream ended.
    /// </summary>
    public int RemainingLength { get; set; }

    /// <summary>
    /// Creates a new <see cref="SshStreamReader"/> that reads from the given stream.
    /// </summary>
    /// <exception cref="ArgumentNullException"></exception>
    public SshStreamReader(Stream stream)
    {
        Stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    /// <exception cref="InvalidDataException"></exception>
    /// <exception cref="EndOfStreamException"></exception>
    public async Task<byte> ReadMessageHeader(
        int maxMessageLength = SFTPIOConsts.MaxMessageLength,
        CancellationToken cancellationToken = default
    )
    {
        RemainingLength = sizeof(uint);
        uint length = await ReadUInt32(cancellationToken).ConfigureAwait(false);
        if (length > maxMessageLength || length < sizeof(byte))
        {
            throw new InvalidDataException($"Invalid message length {length}");
        }
        RemainingLength = (int)length;
        return await ReadByte(cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte> ReadByte(CancellationToken cancellationToken = default)
    {
        using IMemoryOwner<byte> memoryOwner = MemoryPool<byte>.Shared.Rent(sizeof(byte));
        await ReadBinary(memoryOwner.Memory[..sizeof(byte)], cancellationToken).ConfigureAwait(false);
        return memoryOwner.Memory.Span[0];
    }

    public async Task<uint> ReadUInt32(CancellationToken cancellationToken = default)
    {
        using IMemoryOwner<byte> memoryOwner = MemoryPool<byte>.Shared.Rent(sizeof(uint));
        await ReadBinary(memoryOwner.Memory[..sizeof(uint)], cancellationToken).ConfigureAwait(false);
        return BinaryPrimitives.ReadUInt32BigEndian(memoryOwner.Memory.Span);
    }

    public async Task<ulong> ReadUInt64(CancellationToken cancellationToken = default)
    {
        using IMemoryOwner<byte> memoryOwner = MemoryPool<byte>.Shared.Rent(sizeof(ulong));
        await ReadBinary(memoryOwner.Memory[..sizeof(ulong)], cancellationToken).ConfigureAwait(false);
        return BinaryPrimitives.ReadUInt64BigEndian(memoryOwner.Memory.Span);
    }

    public async Task<string> ReadString(CancellationToken cancellationToken = default)
    {
        byte[] bytes = await ReadBinary(cancellationToken).ConfigureAwait(false);
        return SFTPIOConsts.StringEncoding.GetString(bytes);
    }

    public async Task<AccessFlags> ReadAccessFlags(CancellationToken cancellationToken = default)
    {
        return (AccessFlags)await ReadUInt32(cancellationToken).ConfigureAwait(false);
    }

    public async Task<DateTimeOffset> ReadTime(CancellationToken cancellationToken = default)
    {
        uint seconds = await ReadUInt32(cancellationToken).ConfigureAwait(false);
        return DateTimeOffset.FromUnixTimeSeconds(seconds);
    }

    public async Task<SFTPAttributes> ReadAttributes(CancellationToken cancellationToken = default)
    {
        PFlags flags = (PFlags)await ReadUInt32(cancellationToken).ConfigureAwait(false);
        ulong? size = flags.HasFlag(PFlags.Size) ? await ReadUInt64(cancellationToken).ConfigureAwait(false) : null;
        uint? owner = flags.HasFlag(PFlags.UidGid) ? await ReadUInt32(cancellationToken).ConfigureAwait(false) : null;
        uint? group = flags.HasFlag(PFlags.UidGid) ? await ReadUInt32(cancellationToken).ConfigureAwait(false) : null;
        PosixFileMode? permissions = flags.HasFlag(PFlags.Permissions)
            ? (PosixFileMode)await ReadUInt32(cancellationToken).ConfigureAwait(false)
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
            extendedAttributes = [];
            for (var i = 0; i < extendedCount; i++)
            {
                string type = await ReadString(cancellationToken).ConfigureAwait(false);
                string data = await ReadString(cancellationToken).ConfigureAwait(false);
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

    /// <exception cref="InvalidDataException"></exception>
    /// <exception cref="EndOfStreamException"></exception>
    public async Task<byte[]> ReadBinary(CancellationToken cancellationToken = default)
    {
        uint size = await ReadUInt32(cancellationToken).ConfigureAwait(false);
        byte[] bytes = AllocBound(size);
        await ReadBinary(bytes, cancellationToken).ConfigureAwait(false);
        return bytes;
    }

    /// <exception cref="InvalidDataException"></exception>
    private byte[] AllocBound(uint length)
    {
        if (length == 0)
        {
            return Array.Empty<byte>();
        }
        if (length > RemainingLength)
        {
            throw new InvalidDataException($"{RemainingLength} bytes remaining but tried to allocate {length}");
        }
        return new byte[(int)length];
    }

    /// <exception cref="InvalidDataException"></exception>
    /// <exception cref="EndOfStreamException"></exception>
    public async Task ReadBinary(Memory<byte> output, CancellationToken cancellationToken = default)
    {
        if (output.Length == 0)
        {
            return;
        }
        if (output.Length > RemainingLength)
        {
            throw new InvalidDataException($"{RemainingLength} bytes remaining but tried to read {output.Length}");
        }
        int offset = 0;
        while (offset < output.Length)
        {
            int bytesRead = await Stream.ReadAsync(output[offset..], cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                throw new EndOfStreamException(
                    $"Unexpected end of stream while reading {output.Length - offset}/{output.Length} bytes"
                );
            }
            offset += bytesRead;
        }

        RemainingLength -= output.Length;
    }
}
