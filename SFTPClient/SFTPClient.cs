using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JustSFTP.Protocol.Enums;
using JustSFTP.Protocol.IO;
using JustSFTP.Protocol.Models.Responses;

namespace JustSFTP.Client;

/// <summary>
/// An SFTP protocol client over any data stream.
/// </summary>
public class SFTPClient : IDisposable
{
    private const uint CLIENT_SFTP_PROTOCOL_VERSION = 3;

    /// <summary>
    /// The trace source this <see cref="SFTPClient"/> logs to.
    /// </summary>
    public TraceSource TraceSource { get; }

    /// <summary>
    /// The negotiated protocol version.
    /// This will be 0 until <see cref="RunAsync"/> is called and a handshake is performed.
    /// </summary>
    public uint ProtocolVersion { get; private set; } = 0;

    private readonly SshStreamReader reader;
    private readonly SshStreamWriter writer;
    private uint lastRequestId = 0;

    private readonly SemaphoreSlim writerSempahore;
    private readonly ConcurrentDictionary<
        uint,
        TaskCompletionSource<SFTPResponse>
    > requestsAwaitingResponse;

    /// <summary>
    /// Creates a new <see cref="SFTPClient"/> over the given streams. The client is not responsible for closing the streams.
    /// </summary>
    /// <exception cref="ArgumentNullException"></exception>
    public SFTPClient(
        Stream inStream,
        Stream outStream,
        int writeBufferSize = 1048576,
        TraceSource? traceSource = null
    )
    {
        reader = new SshStreamReader(inStream ?? throw new ArgumentNullException(nameof(inStream)));
        writer = new SshStreamWriter(
            outStream ?? throw new ArgumentNullException(nameof(outStream)),
            writeBufferSize
        );
        writerSempahore = new(1, 1);
        requestsAwaitingResponse = [];
        TraceSource = traceSource ?? new TraceSource(nameof(SFTPClient), SourceLevels.Off);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        ((IDisposable)writer).Dispose();
        writerSempahore.Dispose();
    }

    /// <summary>
    /// Runs the read loop of this client.
    /// When this task is canceled, the client is automatically disposed.
    /// </summary>
    /// <exception cref="OperationCanceledException"/>
    /// <exception cref="ObjectDisposedException"/>
    /// <exception cref="InvalidDataException"/>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        ProtocolVersion = await InitAsync(cancellationToken);
        TraceSource.TraceEvent(
            TraceEventType.Information,
            TraceEventIds.SFTPClient_InitSuccess,
            $"Negotiated protocol version: {ProtocolVersion}"
        );
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SFTPResponse response = await SFTPResponse.ReadAsync(
                    reader,
                    ProtocolVersion,
                    cancellationToken
                );
                if (
                    !requestsAwaitingResponse.TryRemove(
                        response.RequestId,
                        out TaskCompletionSource<SFTPResponse>? taskCompletionSource
                    )
                )
                {
                    TraceSource.TraceEvent(
                        TraceEventType.Warning,
                        TraceEventIds.SFTPClient_DroppingResponse,
                        $"Ignoring response for non-existent request id {response.RequestId}"
                    );
                    continue;
                }
                taskCompletionSource.SetResult(response);
            }
        }
        catch (OperationCanceledException)
        {
            Dispose();
            throw;
        }
    }

    /// <exception cref="OperationCanceledException"/>
    /// <exception cref="ObjectDisposedException"/>
    private async Task<SFTPResponse> RequestAsync(
        RequestType requestType,
        byte[] requestData,
        CancellationToken cancellationToken
    )
    {
        TaskCompletionSource<SFTPResponse> taskCompletionSource = new();
        await writerSempahore.WaitAsync(cancellationToken);
        try
        {
            uint requestId = ++lastRequestId;
            requestsAwaitingResponse.TryAdd(requestId, taskCompletionSource);
            await writer.Write(requestData.Length + 5, cancellationToken);
            await writer.Write(requestType, cancellationToken);
            await writer.Write(requestId, cancellationToken);
            await writer.Write(requestData, cancellationToken);
        }
        finally
        {
            writerSempahore.Release();
        }
        return await taskCompletionSource.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// Performs the init handshake with the remote server.
    /// </summary>
    /// <returns>The negotiated SFTP protocol version.</returns>
    /// <exception cref="OperationCanceledException"/>
    /// <exception cref="InvalidDataException"/>
    private async Task<uint> InitAsync(CancellationToken cancellationToken = default)
    {
        await writer.Write(RequestType.Init, cancellationToken).ConfigureAwait(false);
        await writer.Write(CLIENT_SFTP_PROTOCOL_VERSION, cancellationToken).ConfigureAwait(false);
        await writer.Flush(cancellationToken).ConfigureAwait(false);

        uint msglen = await reader.ReadUInt32(cancellationToken).ConfigureAwait(false);
        if (msglen < 5)
            throw new InvalidDataException($"Message length {msglen} is too short");
        byte typeByte = await reader.ReadByte(cancellationToken).ConfigureAwait(false);
        if (typeByte != (byte)ResponseType.Version)
            throw new InvalidDataException($"Expected Version response, got {typeByte}");

        uint serverVersion = await reader.ReadUInt32(cancellationToken).ConfigureAwait(false);

        msglen -= 5; // We've already read 1 byte for the type and 4 for the server version
        var serverExtensions = new Dictionary<string, string>();
        while (msglen > 0)
        {
            byte[] nameBytes = await reader.ReadBinary(cancellationToken).ConfigureAwait(false);
            byte[] dataBytes = await reader.ReadBinary(cancellationToken).ConfigureAwait(false);
            serverExtensions[reader.StringEncoding.GetString(nameBytes)] =
                reader.StringEncoding.GetString(dataBytes);
            msglen -= (uint)(nameBytes.Length + dataBytes.Length);
        }

        return Math.Min(serverVersion, CLIENT_SFTP_PROTOCOL_VERSION);
    }
}
