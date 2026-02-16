using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JustSFTP.Protocol.Enums;
using JustSFTP.Protocol.Models;

namespace JustSFTP.Protocol.IO;

/// <summary>
/// A buffered stream writer that sends length-prefixed messages on flush.
/// </summary>
public class SshStreamWriter : IDisposable
{
    private static readonly Encoding SFTPStringEncoding = new UTF8Encoding(false);
    private readonly Stream innerStream;
    private readonly MemoryStream memoryStream;

    /// <summary>
    /// Creates a new <see cref="SshStreamWriter"/> that writes to the specified stream.
    /// </summary>
    /// <param name="stream">The underlying stream.</param>
    /// <param name="bufferSize">The buffer size. Sent messages can't be longer than this number.</param>
    /// <exception cref="ArgumentNullException"/>
    public SshStreamWriter(Stream stream, int bufferSize)
    {
        innerStream = stream ?? throw new ArgumentNullException(nameof(stream));
        memoryStream = new MemoryStream(bufferSize);
    }

    public Task Write(RequestType requestType, CancellationToken cancellationToken = default) =>
        Write((byte)requestType, cancellationToken);

    public Task Write(ResponseType responseType, CancellationToken cancellationToken = default) =>
        Write((byte)responseType, cancellationToken);

    public Task Write(PFlags fileAttributeFlags, CancellationToken cancellationToken = default) =>
        Write((uint)fileAttributeFlags, cancellationToken);

    public Task Write(Permissions permissions, CancellationToken cancellationToken = default) =>
        Write((uint)permissions, cancellationToken);

    public Task Write(Status status, CancellationToken cancellationToken = default) =>
        Write((uint)status, cancellationToken);

    public async Task Write(SFTPName name, CancellationToken cancellationToken = default)
    {
        SFTPAttributes fileattrs = name.Attributes;
        await Write(name.Name, cancellationToken).ConfigureAwait(false);
        await Write(name.LongName, cancellationToken).ConfigureAwait(false);
        await Write(fileattrs, PFlags.DEFAULT, cancellationToken).ConfigureAwait(false);
    }

    public async Task Write(
        SFTPAttributes attributes,
        PFlags flags = PFlags.DEFAULT,
        CancellationToken cancellationToken = default
    )
    {
        await Write(flags, cancellationToken).ConfigureAwait(false);
        if (flags.HasFlag(PFlags.Size))
        {
            await Write(attributes.FileSize, cancellationToken).ConfigureAwait(false);
        }

        if (flags.HasFlag(PFlags.UidGid))
        {
            await Write(attributes.User.Id, cancellationToken).ConfigureAwait(false);
            await Write(attributes.Group.Id, cancellationToken).ConfigureAwait(false);
        }
        if (flags.HasFlag(PFlags.Permissions))
        {
            await Write(attributes.Permissions, cancellationToken).ConfigureAwait(false);
        }

        if (flags.HasFlag(PFlags.AccessModifiedTime))
        {
            await Write(attributes.LastAccessedTime, cancellationToken).ConfigureAwait(false);
            await Write(attributes.LastModifiedTime, cancellationToken).ConfigureAwait(false);
        }

        if (flags.HasFlag(PFlags.Extended))
        {
            await Write(attributes.ExtendeAttributes.Count, cancellationToken)
                .ConfigureAwait(false);
            foreach (var a in attributes.ExtendeAttributes)
            {
                await Write(a.Key, cancellationToken).ConfigureAwait(false); //type
                await Write(a.Value, cancellationToken).ConfigureAwait(false); //data
            }
        }
    }

    public async Task Write(
        DateTimeOffset dateTime,
        CancellationToken cancellationToken = default
    ) => await Write((uint)dateTime.ToUnixTimeSeconds(), cancellationToken).ConfigureAwait(false);

    public Task Write(byte value, CancellationToken cancellationToken = default) =>
        Write([value], cancellationToken);

    public Task Write(int value, CancellationToken cancellationToken = default) =>
        Write((uint)value, cancellationToken);

    public Task Write(uint value, CancellationToken cancellationToken = default)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return memoryStream.WriteAsync(bytes, 0, 4, cancellationToken);
    }

    public Task Write(ulong value, CancellationToken cancellationToken = default)
    {
        byte[] bytes = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        return memoryStream.WriteAsync(bytes, 0, 8, cancellationToken);
    }

    public async Task Write(string str, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(str);
        byte[] data = SFTPStringEncoding.GetBytes(str);
        await Write(data.Length, cancellationToken).ConfigureAwait(false);
        await Write(data, cancellationToken).ConfigureAwait(false);
    }

    public Task Write(byte[] data, CancellationToken cancellationToken = default) =>
        memoryStream.WriteAsync(data, 0, data.Length, cancellationToken);

    /// <summary>
    /// Writes the built message, prefixed with its size, to the underlying stream.
    /// </summary>
    public async Task Flush(CancellationToken cancellationToken = default)
    {
        var data = memoryStream.ToArray();

        byte[] len = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(len, (uint)data.Length);

        await innerStream.WriteAsync(len, cancellationToken).ConfigureAwait(false);
        await innerStream.WriteAsync(data, cancellationToken).ConfigureAwait(false);

        memoryStream.Position = 0;
        memoryStream.SetLength(0);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        ((IDisposable)memoryStream).Dispose();
    }
}
