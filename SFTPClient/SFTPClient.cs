using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JustSFTP.Protocol;
using JustSFTP.Protocol.Enums;
using JustSFTP.Protocol.IO;
using JustSFTP.Protocol.Models;
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
        writerSempahore = new(0, 1);
        requestsAwaitingResponse = [];
        TraceSource = traceSource ?? new TraceSource(nameof(SFTPClient), SourceLevels.Off);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        ((IDisposable)writer).Dispose();
        writerSempahore.Dispose();
        ObjectDisposedException exception = new(nameof(SFTPClient));
        foreach (
            TaskCompletionSource<SFTPResponse> taskCompletionSource in requestsAwaitingResponse.Values
        )
        {
            taskCompletionSource.TrySetException(exception);
        }
        requestsAwaitingResponse.Clear();
    }

    /// <summary>
    /// Runs the read loop of this client.
    /// When this task is canceled or throws an exception, the client is automatically disposed.
    /// </summary>
    /// <exception cref="OperationCanceledException"/>
    /// <exception cref="ObjectDisposedException"/>
    /// <exception cref="InvalidDataException"/>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            ProtocolVersion = await InitAsync(cancellationToken);
            TraceSource.TraceEvent(
                TraceEventType.Information,
                TraceEventIds.SFTPClient_InitSuccess,
                "Negotiated protocol version: {0}",
                ProtocolVersion
            );
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SFTPResponse response = await SFTPResponse.ReadAsync(reader, cancellationToken);
                TraceSource.TraceEvent(
                    TraceEventType.Verbose,
                    TraceEventIds.SFTPClient_ReceivedResponse,
                    "Response for request #{0}: {1}",
                    response.RequestId,
                    response
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
                        "Ignoring response for non-existent request id {0}",
                        response.RequestId
                    );
                    continue;
                }
                taskCompletionSource.TrySetResult(response);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// Opens the given file.
    /// </summary>
    /// <param name="Path">The remote path of the file to open.</param>
    /// <param name="Flags">The access flags, e.g. read/write.</param>
    /// <param name="Attributes">The initial attributes for the file. Default values will be used for those attributes that are not specified.</param>
    /// <exception cref="HandlerException"/>
    /// <exception cref="InvalidDataException"/>
    /// <exception cref="OperationCanceledException"/>
    /// <exception cref="ObjectDisposedException"/>
    public async Task<SFTPHandle> OpenFileAsync(
        string Path,
        AccessFlags Flags,
        SFTPAttributes Attributes
    )
    {
        SFTPResponse response = await RequestAsync(
                new SFTPOpenRequest(GetNextRequestId(), Path, Flags, Attributes)
            )
            .ConfigureAwait(false);
        if (response is SFTPStatus status)
        {
            throw new HandlerException(status.Status);
        }
        if (response is not SFTPHandleResponse handleResponse)
        {
            throw new InvalidDataException(
                $"Unexpected response type {response.GetType().FullName}"
            );
        }
        return new SFTPHandle(handleResponse.Handle);
    }

    private uint GetNextRequestId()
    {
        return Interlocked.Increment(ref lastRequestId);
    }

    /// <summary>
    /// Sends the given request to the remote server and waits for a response.
    /// </summary>
    /// <returns>The SFTP response.</returns>
    /// <exception cref="OperationCanceledException"/>
    /// <exception cref="ObjectDisposedException"/>
    private async Task<SFTPResponse> RequestAsync(
        SFTPRequest request,
        CancellationToken cancellationToken = default
    )
    {
        TraceSource.TraceEvent(
            TraceEventType.Verbose,
            TraceEventIds.SFTPClient_SendingRequest,
            "Request #{0}: {1}",
            request.RequestId,
            request.RequestType
        );
        TaskCompletionSource<SFTPResponse> taskCompletionSource = new();
        await writerSempahore.WaitAsync(cancellationToken);
        try
        {
            requestsAwaitingResponse.TryAdd(request.RequestId, taskCompletionSource);
            await request.WriteAsync(writer, cancellationToken);
            await writer.Flush(cancellationToken);
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
            serverExtensions[SshStreamReader.SFTPStringEncoding.GetString(nameBytes)] =
                SshStreamReader.SFTPStringEncoding.GetString(dataBytes);
            msglen -= (uint)(nameBytes.Length + dataBytes.Length);
        }

        writerSempahore.Release(); // Allow requests to write
        return Math.Min(serverVersion, CLIENT_SFTP_PROTOCOL_VERSION);
    }
}
