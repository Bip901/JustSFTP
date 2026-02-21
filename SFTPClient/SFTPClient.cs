using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
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
    private readonly bool ownsStreams;
    private uint lastRequestId = 0;

    private readonly SemaphoreSlim writerSempahore;
    private readonly ConcurrentDictionary<
        uint,
        TaskCompletionSource<SFTPResponse>
    > requestsAwaitingResponse;

    /// <summary>
    /// Creates a new <see cref="SFTPClient"/> over the given streams.
    /// </summary>
    /// <param name="inStream">The stream to read from.</param>
    /// <param name="outStream">The stream to write to.</param>
    /// <param name="writeBufferSize">The write buffer size in bytes. Longer messages will not be able to be written.</param>
    /// <param name="traceSource">Optionally, a trace source to log to. Defaults to a silent trace source. See also: <seealso cref="TraceEventIds"/>.</param>
    /// <param name="ownsStreams">Whether to dispose the inStream and outStream when this client is disposed.</param>
    /// <exception cref="ArgumentNullException"></exception>
    public SFTPClient(
        Stream inStream,
        Stream outStream,
        int writeBufferSize = 1048576,
        TraceSource? traceSource = null,
        bool ownsStreams = false
    )
    {
        reader = new SshStreamReader(inStream ?? throw new ArgumentNullException(nameof(inStream)));
        writer = new SshStreamWriter(
            outStream ?? throw new ArgumentNullException(nameof(outStream)),
            writeBufferSize,
            ownsStreams
        );
        writerSempahore = new(0, 1);
        requestsAwaitingResponse = [];
        TraceSource = traceSource ?? new TraceSource(nameof(SFTPClient), SourceLevels.Off);
        this.ownsStreams = ownsStreams;
    }

    /// <inheritdoc/>
#pragma warning disable CA1816 // Dispose methods should call SuppressFinalize
    public void Dispose()
    {
        Dispose(null);
    }

    private void Dispose(Exception? reason)
    {
        GC.SuppressFinalize(this);
#pragma warning restore CA1816 // Dispose methods should call SuppressFinalize
        ((IDisposable)writer).Dispose();
        if (ownsStreams)
        {
            reader.Stream.Dispose();
        }
        writerSempahore.Dispose();
        ObjectDisposedException exception = new(nameof(SFTPClient), reason);
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
                    "RECV: {0}",
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
        catch (Exception ex)
        {
            Dispose(ex);
            throw;
        }
    }

    #region High-level requests
    /// <summary>
    /// Opens the given file.
    /// </summary>
    /// <param name="path">The remote path of the file to open.</param>
    /// <param name="flags">The access flags, e.g. read/write.</param>
    /// <param name="attributes">The initial attributes for the file. Default values will be used for those attributes that are not specified.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <exception cref="HandlerException"/>
    /// <exception cref="InvalidDataException"/>
    /// <exception cref="OperationCanceledException"/>
    /// <exception cref="ObjectDisposedException"/>
    public async Task<Stream> OpenFileAsync(
        string path,
        AccessFlags flags,
        SFTPAttributes attributes,
        CancellationToken cancellationToken = default
    )
    {
        SFTPResponse response = await RequestAsync(
                new SFTPOpenRequest(GetNextRequestId(), path, flags, attributes),
                cancellationToken
            )
            .ConfigureAwait(false);
        SFTPHandleResponse handleResponse = CheckResponseTypeAndStatus<SFTPHandleResponse>(
            response
        );
        return new SFTPFileStream(
            this,
            handleResponse.Handle,
            flags.HasFlag(AccessFlags.Read),
            flags.HasFlag(AccessFlags.Write)
        );
    }

    /// <summary>
    /// Yields the immediate children of the given directory.
    /// </summary>
    /// <exception cref="HandlerException"/>
    /// <exception cref="InvalidDataException"/>
    /// <exception cref="OperationCanceledException"/>
    /// <exception cref="ObjectDisposedException"/>
    public async IAsyncEnumerable<SFTPName> IterDirAsync(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        SFTPResponse openResponse = await RequestAsync(
                new SFTPOpenDirRequest(GetNextRequestId(), path),
                cancellationToken
            )
            .ConfigureAwait(false);
        byte[] handle = CheckResponseTypeAndStatus<SFTPHandleResponse>(openResponse).Handle;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SFTPNameResponse readDirResponse;
            try
            {
                SFTPResponse readDirResponseRaw = await RequestAsync(
                    new SFTPReadDirRequest(GetNextRequestId(), handle),
                    cancellationToken
                );
                readDirResponse = CheckResponseTypeAndStatus<SFTPNameResponse>(readDirResponseRaw);
            }
            catch (Exception ex)
            {
                await CloseFileAsync(handle, cancellationToken).ConfigureAwait(false);
                if (
                    ex is HandlerException handlerException
                    && handlerException.Status == Status.EndOfFile
                )
                {
                    break;
                }
                throw;
            }
            foreach (SFTPName name in readDirResponse.Names)
            {
                yield return name;
            }
        }
    }

    /// <summary>
    /// Creates a directory at the specified remote path with the given attributes.
    /// </summary>
    /// <exception cref="HandlerException"/>
    /// <exception cref="InvalidDataException"/>
    /// <exception cref="OperationCanceledException"/>
    /// <exception cref="ObjectDisposedException"/>
    public async Task MakeDirAsync(
        string path,
        SFTPAttributes attributes,
        CancellationToken cancellationToken = default
    )
    {
        SFTPResponse response = await RequestAsync(
                new SFTPMakeDirRequest(GetNextRequestId(), path, attributes),
                cancellationToken
            )
            .ConfigureAwait(false);
        CheckResponseTypeAndStatus<SFTPStatus>(response);
    }

    /// <summary>
    /// Removes the (empty) directory at the given remote path.
    /// </summary>
    /// <exception cref="HandlerException"/>
    /// <exception cref="InvalidDataException"/>
    /// <exception cref="OperationCanceledException"/>
    /// <exception cref="ObjectDisposedException"/>
    public async Task RemoveDirAsync(string path, CancellationToken cancellationToken = default)
    {
        SFTPResponse response = await RequestAsync(
                new SFTPRemoveDirRequest(GetNextRequestId(), path),
                cancellationToken
            )
            .ConfigureAwait(false);
        CheckResponseTypeAndStatus<SFTPStatus>(response);
    }

    /// <summary>
    /// Removes the file at the given remote path.
    /// </summary>
    /// <exception cref="HandlerException"/>
    /// <exception cref="InvalidDataException"/>
    /// <exception cref="OperationCanceledException"/>
    /// <exception cref="ObjectDisposedException"/>
    public async Task RemoveAsync(string path, CancellationToken cancellationToken = default)
    {
        SFTPResponse response = await RequestAsync(
                new SFTPRemoveRequest(GetNextRequestId(), path),
                cancellationToken
            )
            .ConfigureAwait(false);
        CheckResponseTypeAndStatus<SFTPStatus>(response);
    }

    /// <summary>
    /// Renames a file or directory from <paramref name="oldPath"/> to <paramref name="newPath"/>.
    /// </summary>
    /// <exception cref="HandlerException"/>
    /// <exception cref="InvalidDataException"/>
    /// <exception cref="OperationCanceledException"/>
    /// <exception cref="ObjectDisposedException"/>
    public async Task RenameAsync(
        string oldPath,
        string newPath,
        CancellationToken cancellationToken = default
    )
    {
        SFTPResponse response = await RequestAsync(
                new SFTPRenameRequest(GetNextRequestId(), oldPath, newPath),
                cancellationToken
            )
            .ConfigureAwait(false);
        CheckResponseTypeAndStatus<SFTPStatus>(response);
    }
    #endregion

    #region Low-level requests
    internal Task CloseFileAsync(byte[] handle, CancellationToken cancellationToken = default)
    {
        return RequestAsync(new SFTPCloseRequest(GetNextRequestId(), handle), cancellationToken);
    }

    internal async Task<byte[]> ReadAsync(
        byte[] handle,
        ulong offset,
        uint length,
        CancellationToken cancellationToken = default
    )
    {
        SFTPResponse response = await RequestAsync(
                new SFTPReadRequest(GetNextRequestId(), handle, offset, length),
                cancellationToken
            )
            .ConfigureAwait(false);
        SFTPData data = CheckResponseTypeAndStatus<SFTPData>(response);
        return data.Data;
    }

    /// <exception cref="HandlerException"></exception>
    /// <exception cref="InvalidDataException"></exception>
    internal async Task WriteAsync(
        byte[] handle,
        ulong offset,
        byte[] data,
        CancellationToken cancellationToken = default
    )
    {
        SFTPResponse response = await RequestAsync(
                new SFTPWriteRequest(GetNextRequestId(), handle, offset, data),
                cancellationToken
            )
            .ConfigureAwait(false);
        CheckResponseTypeAndStatus<SFTPStatus>(response);
    }
    #endregion

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
            "SEND: {0}",
            request
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

    /// <exception cref="HandlerException"></exception>
    /// <exception cref="InvalidDataException"></exception>
    private static T CheckResponseTypeAndStatus<T>(SFTPResponse response)
        where T : SFTPResponse
    {
        if (
            response is SFTPStatus status
            && (typeof(T) != typeof(SFTPStatus) || status.Status != Status.Ok)
        )
        {
            throw new HandlerException(status.Status);
        }
        if (response is not T castedResponse)
        {
            throw new InvalidDataException($"Unexpected response type {response.ResponseType}");
        }
        return castedResponse;
    }
}
