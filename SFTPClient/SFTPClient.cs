using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JustSFTP.Protocol.Enums;
using JustSFTP.Protocol.IO;

namespace JustSFTP.Client;

public class SFTPClient : IDisposable
{
    private const uint CLIENT_SFTP_PROTOCOL_VERSION = 3;

    private readonly SshStreamReader _reader;
    private readonly SshStreamWriter _writer;
    private uint serverVersion = 0;
    private uint _nextRequestId = 1;

    /// <summary>
    /// Creates a new <see cref="SFTPClient"/> over the given streams. The client is not responsible for closing the streams.
    /// </summary>
    /// <exception cref="ArgumentNullException"></exception>
    public SFTPClient(Stream inStream, Stream outStream, int writeBufferSize = 1048576) // 1 MiB
    {
        _reader = new SshStreamReader(
            inStream ?? throw new ArgumentNullException(nameof(inStream))
        );
        _writer = new SshStreamWriter(
            outStream ?? throw new ArgumentNullException(nameof(outStream)),
            writeBufferSize
        );
    }

    public void Dispose()
    {
        ((IDisposable)_writer).Dispose();
    }

    public async Task<uint> InitAsync(CancellationToken cancellationToken = default)
    {
        await _writer.Write(RequestType.Init, cancellationToken).ConfigureAwait(false);
        await _writer.Write(CLIENT_SFTP_PROTOCOL_VERSION, cancellationToken).ConfigureAwait(false);
        await _writer.Flush(cancellationToken).ConfigureAwait(false);

        var msglen = await _reader.ReadUInt32(cancellationToken).ConfigureAwait(false);
        if (msglen == 0)
            throw new EndOfStreamException();
        var typeByte = await _reader.ReadByte(cancellationToken).ConfigureAwait(false);
        if (typeByte != (byte)RequestType.Version)
            throw new InvalidDataException($"Expected Version response, got {typeByte}");

        serverVersion = await _reader.ReadUInt32(cancellationToken).ConfigureAwait(false);

        // Consume any extensions sent by server (remaining bytes = msglen - 5)
        var remaining = (int)msglen - 5;
        var utf8 = Encoding.UTF8;
        while (remaining > 0)
        {
            var name = await _reader.ReadString(cancellationToken).ConfigureAwait(false);
            var data = await _reader.ReadString(cancellationToken).ConfigureAwait(false);
            remaining -= 4 + utf8.GetByteCount(name) + 4 + utf8.GetByteCount(data);
        }

        return Math.Min(serverVersion, CLIENT_SFTP_PROTOCOL_VERSION);
    }
}
