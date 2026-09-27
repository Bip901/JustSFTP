using System;
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

namespace JustSFTP.Server;

/// <summary>
/// An SFTP server that serves just the SFTP protocol over any streams.
/// </summary>
public sealed class SFTPServer : ISFTPServer, IDisposable
{
    /// <summary>
    /// The protocol version to advertise when handshaking with clients.
    /// </summary>
    public const uint SERVER_SFTP_PROTOCOL_VERSION = 3;

    /// <summary>
    /// The maximum amount of file entries to include in a read dir response.
    /// </summary>
    public const int READ_DIR_PAGE_SIZE = 128;

    private delegate Task<SFTPResponse> MessageHandler(uint requestId, CancellationToken cancellationToken);

    /// <summary>
    /// The trace source this <see cref="SFTPServer"/> logs to.
    /// </summary>
    public TraceSource TraceSource { get; }

    private readonly SshStreamReader reader;
    private readonly SshStreamWriter writer;
    private readonly ISFTPHandler sftpHandler;
    private readonly int maxMessageLength;
    private uint? protocolVersion;

    private readonly Dictionary<RequestType, MessageHandler> messageHandlers;

    /// <summary>
    /// Creates a new <see cref="SFTPServer"/> over the given streams, serving files using the given <see cref="ISFTPHandler"/>.
    /// The server is not responsible for closing the streams.
    /// </summary>
    /// <param name="inStream">The stream to read from.</param>
    /// <param name="outStream">The stream to write to.</param>
    /// <param name="sftpHandler">The SFTP handler.</param>
    /// <param name="maxMessageLength">The maximum message length to allow reading.</param>
    /// <param name="initialWriteBufferSize">The initial write buffer size in bytes. The maximum outgoing message length is entirely up to the <paramref name="sftpHandler"/>.</param>
    /// <param name="traceSource">Optionally, a trace source to log to. Defaults to a silent trace source. See also: <see cref="TraceEventIds"/>.</param>
    /// <exception cref="ArgumentNullException"></exception>
    public SFTPServer(
        Stream inStream,
        Stream outStream,
        ISFTPHandler sftpHandler,
        TraceSource? traceSource = null,
        int maxMessageLength = SFTPIOConsts.MaxMessageLength,
        int initialWriteBufferSize = SFTPIOConsts.MaxMessageLength
    )
    {
        reader = new SshStreamReader(inStream ?? throw new ArgumentNullException(nameof(inStream)));
        writer = new SshStreamWriter(
            outStream ?? throw new ArgumentNullException(nameof(outStream)),
            initialWriteBufferSize
        );
        this.sftpHandler = sftpHandler ?? throw new ArgumentNullException(nameof(sftpHandler));
        this.maxMessageLength = maxMessageLength;

        messageHandlers = new()
        {
            { RequestType.Open, OpenHandler },
            { RequestType.Close, CloseHandler },
            { RequestType.Read, ReadHandler },
            { RequestType.Write, WriteHandler },
            { RequestType.LStat, LStatHandler },
            { RequestType.FStat, FStatHandler },
            { RequestType.SetStat, SetStatHandler },
            { RequestType.FSetStat, FSetStatHandler },
            { RequestType.OpenDir, OpenDirHandler },
            { RequestType.ReadDir, ReadDirHandler },
            { RequestType.Remove, RemoveHandler },
            { RequestType.MakeDir, MakeDirHandler },
            { RequestType.RemoveDir, RemoveDirHandler },
            { RequestType.RealPath, RealPathHandler },
            { RequestType.Stat, StatHandler },
            { RequestType.Rename, RenameHandler },
            { RequestType.ReadLink, ReadLinkHandler },
            { RequestType.SymLink, SymLinkHandler },
            { RequestType.Extended, ExtendedHandler },
        };

        TraceSource = traceSource ?? new TraceSource(nameof(SFTPServer), SourceLevels.Off);
    }

    /// <summary>
    /// Runs this server until canceled or end-of-stream.
    /// </summary>
    /// <exception cref="OperationCanceledException"/>
    public async Task Run(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestType requestType;
            try
            {
                requestType = (RequestType)
                    await reader.ReadMessageHeader(maxMessageLength, cancellationToken).ConfigureAwait(false);
            }
            catch (EndOfStreamException)
            {
                break;
            }
            if (protocolVersion.HasValue)
            {
                uint requestId = await reader.ReadUInt32(cancellationToken).ConfigureAwait(false);
                TraceSource.TraceEvent(
                    TraceEventType.Verbose,
                    TraceEventIds.SFTPServer_ReceivedRequest,
                    "RECV: #{0} {1}",
                    requestId,
                    requestType
                );
                SFTPResponse response = await BuildResponseAsync(requestId, requestType, cancellationToken)
                    .ConfigureAwait(false);
                response = EnsureStatusProtocolVersion(response);
                TraceSource.TraceEvent(
                    TraceEventType.Verbose,
                    TraceEventIds.SFTPServer_SendingResponse,
                    "SEND: {0}",
                    response
                );
                await response.WriteAsync(writer, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                if (requestType != RequestType.Init)
                {
                    throw new InvalidDataException($"Received a request of type {requestType} before Init");
                }
                await InitHandler(cancellationToken).ConfigureAwait(false);
            }

            // Write response
            await writer.Flush(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<SFTPResponse> BuildResponseAsync(
        uint requestId,
        RequestType requestType,
        CancellationToken cancellationToken
    )
    {
        if (!messageHandlers.TryGetValue(requestType, out MessageHandler? handler))
        {
            return BuildStatus(requestId, Status.OperationUnsupported);
        }
        try
        {
            return await handler(requestId, cancellationToken).ConfigureAwait(false);
        }
        catch (HandlerException ex)
        {
            return BuildStatus(requestId, ex.Status, ex.HasExplicitMessage ? ex.Message : null);
        }
        catch (Exception ex)
        {
            TraceSource.TraceEvent(
                TraceEventType.Error,
                TraceEventIds.SFTPServer_SendingResponse,
                "Uncaught exception while responding to request #{0} of type {1}: {2}",
                requestId,
                requestType,
                ex
            );
            return BuildStatus(requestId, Status.Failure);
        }
    }

    private SFTPResponse EnsureStatusProtocolVersion(SFTPResponse response)
    {
        if (
            protocolVersion >= 3
            && response is SFTPStatus statusResponse
            && (statusResponse.LanguageTag == null || statusResponse.ErrorMessage == null)
        )
        {
            return statusResponse with
            {
                LanguageTag = statusResponse.LanguageTag ?? string.Empty,
                ErrorMessage = statusResponse.ErrorMessage ?? sftpHandler.GetDefaultStatusString(statusResponse.Status),
            };
        }
        return response;
    }

    private async Task InitHandler(CancellationToken cancellationToken = default)
    {
        // Get client version
        uint clientVersion = await reader.ReadUInt32(cancellationToken).ConfigureAwait(false);
        protocolVersion = Math.Min(clientVersion, SERVER_SFTP_PROTOCOL_VERSION);

        // Get client extensions (if any)
        Dictionary<string, string> clientExtensions = [];
        while (reader.RemainingLength > 0)
        {
            string name = await reader.ReadString(cancellationToken).ConfigureAwait(false);
            string data = await reader.ReadString(cancellationToken).ConfigureAwait(false);
            clientExtensions[name] = data;
        }

        SFTPExtensions serverExtensions = await sftpHandler
            .Init(clientVersion, new SFTPExtensions(clientExtensions), cancellationToken)
            .ConfigureAwait(false);

        // Send version response
        await writer.Write(ResponseType.Version, cancellationToken).ConfigureAwait(false);
        await writer.Write(protocolVersion.Value, cancellationToken).ConfigureAwait(false);
        foreach (var pair in serverExtensions)
        {
            await writer.Write(pair.Key, cancellationToken).ConfigureAwait(false);
            await writer.Write(pair.Value, cancellationToken).ConfigureAwait(false);
        }

        TraceSource.TraceEvent(
            TraceEventType.Information,
            TraceEventIds.SFTPServer_InitSuccess,
            "Negotiated protocol version: {0}",
            protocolVersion
        );
    }

    private async Task<SFTPResponse> OpenHandler(uint requestId, CancellationToken cancellationToken = default)
    {
        var path = await reader.ReadString(cancellationToken).ConfigureAwait(false);
        var flags = await reader.ReadAccessFlags(cancellationToken).ConfigureAwait(false);
        var attrs = await reader.ReadAttributes(cancellationToken).ConfigureAwait(false);
        var result = await sftpHandler
            .Open(new SFTPPath(path), flags.ToFileMode(), flags.ToFileAccess(), attrs, cancellationToken)
            .ConfigureAwait(false);
        return new SFTPHandleResponse(requestId, result);
    }

    private async Task<SFTPResponse> CloseHandler(uint requestId, CancellationToken cancellationToken = default)
    {
        byte[] handle = await reader.ReadBinary(cancellationToken).ConfigureAwait(false);
        await sftpHandler.Close(handle, cancellationToken).ConfigureAwait(false);
        return BuildStatus(requestId, Status.Ok);
    }

    private async Task<SFTPResponse> ReadHandler(uint requestId, CancellationToken cancellationToken = default)
    {
        byte[] handle = await reader.ReadBinary(cancellationToken).ConfigureAwait(false);
        var offset = await reader.ReadUInt64(cancellationToken).ConfigureAwait(false);
        var len = await reader.ReadUInt32(cancellationToken).ConfigureAwait(false);
        byte[] result = await sftpHandler.Read(handle, offset, len, cancellationToken).ConfigureAwait(false);
        return new SFTPData(requestId, result);
    }

    private async Task<SFTPResponse> WriteHandler(uint requestId, CancellationToken cancellationToken = default)
    {
        byte[] handle = await reader.ReadBinary(cancellationToken).ConfigureAwait(false);
        var offset = await reader.ReadUInt64(cancellationToken).ConfigureAwait(false);
        var data = await reader.ReadBinary(cancellationToken).ConfigureAwait(false);
        await sftpHandler.Write(handle, offset, data, cancellationToken).ConfigureAwait(false);
        return BuildStatus(requestId, Status.Ok);
    }

    private async Task<SFTPResponse> LStatHandler(uint requestId, CancellationToken cancellationToken = default)
    {
        var path = new SFTPPath(await reader.ReadString(cancellationToken).ConfigureAwait(false));
        SFTPAttributes attrs = await sftpHandler.LStat(path, cancellationToken).ConfigureAwait(false);
        return new SFTPAttributesResponse(requestId, attrs);
    }

    private async Task<SFTPResponse> FStatHandler(uint requestId, CancellationToken cancellationToken = default)
    {
        byte[] handle = await reader.ReadBinary(cancellationToken).ConfigureAwait(false);
        SFTPAttributes attrs = await sftpHandler.FStat(handle, cancellationToken).ConfigureAwait(false);
        return new SFTPAttributesResponse(requestId, attrs);
    }

    private async Task<SFTPResponse> SetStatHandler(uint requestId, CancellationToken cancellationToken = default)
    {
        var path = new SFTPPath(await reader.ReadString(cancellationToken).ConfigureAwait(false));
        var attrs = await reader.ReadAttributes(cancellationToken).ConfigureAwait(false);
        await sftpHandler.SetStat(path, attrs, cancellationToken).ConfigureAwait(false);
        return BuildStatus(requestId, Status.Ok);
    }

    private async Task<SFTPResponse> FSetStatHandler(uint requestId, CancellationToken cancellationToken = default)
    {
        byte[] handle = await reader.ReadBinary(cancellationToken).ConfigureAwait(false);
        var attrs = await reader.ReadAttributes(cancellationToken).ConfigureAwait(false);
        await sftpHandler.FSetStat(handle, attrs, cancellationToken).ConfigureAwait(false);
        return BuildStatus(requestId, Status.Ok);
    }

    private async Task<SFTPResponse> OpenDirHandler(uint requestId, CancellationToken cancellationToken = default)
    {
        SFTPPath path = new(await reader.ReadString(cancellationToken).ConfigureAwait(false));
        byte[] result = await sftpHandler.OpenDir(path, cancellationToken).ConfigureAwait(false);
        return new SFTPHandleResponse(requestId, result);
    }

    private async Task<SFTPResponse> ReadDirHandler(uint requestId, CancellationToken cancellationToken = default)
    {
        byte[] handle = await reader.ReadBinary(cancellationToken).ConfigureAwait(false);
        IEnumerator<SFTPName> enumerator = await sftpHandler.ReadDir(handle, cancellationToken).ConfigureAwait(false);
        List<SFTPName> results = [];
        for (int i = 0; i < READ_DIR_PAGE_SIZE && enumerator.MoveNext(); i++)
        {
            results.Add(enumerator.Current);
        }
        if (results.Count == 0)
        {
            return BuildStatus(requestId, Status.EndOfFile);
        }
        return new SFTPNameResponse(requestId, results);
    }

    private async Task<SFTPResponse> RemoveHandler(uint requestId, CancellationToken cancellationToken = default)
    {
        var path = new SFTPPath(await reader.ReadString(cancellationToken).ConfigureAwait(false));
        await sftpHandler.Remove(path, cancellationToken).ConfigureAwait(false);
        return BuildStatus(requestId, Status.Ok);
    }

    private async Task<SFTPResponse> MakeDirHandler(uint requestId, CancellationToken cancellationToken = default)
    {
        var path = new SFTPPath(await reader.ReadString(cancellationToken).ConfigureAwait(false));
        var attrs = await reader.ReadAttributes(cancellationToken).ConfigureAwait(false);
        await sftpHandler.MakeDir(path, attrs, cancellationToken).ConfigureAwait(false);
        return BuildStatus(requestId, Status.Ok);
    }

    private async Task<SFTPResponse> RemoveDirHandler(uint requestId, CancellationToken cancellationToken = default)
    {
        var path = new SFTPPath(await reader.ReadString(cancellationToken).ConfigureAwait(false));
        await sftpHandler.RemoveDir(path, cancellationToken).ConfigureAwait(false);
        return BuildStatus(requestId, Status.Ok);
    }

    private async Task<SFTPResponse> RealPathHandler(uint requestId, CancellationToken cancellationToken = default)
    {
        string path = await reader.ReadString(cancellationToken).ConfigureAwait(false);
        SFTPPath result = await sftpHandler.RealPath(new SFTPPath(path), cancellationToken).ConfigureAwait(false);
        return new SFTPNameResponse(requestId, [new SFTPName(result.Path, new SFTPAttributes())]);
    }

    private async Task<SFTPResponse> StatHandler(uint requestId, CancellationToken cancellationToken = default)
    {
        var path = new SFTPPath(await reader.ReadString(cancellationToken).ConfigureAwait(false));
        SFTPAttributes attrs = await sftpHandler.Stat(path, cancellationToken).ConfigureAwait(false);
        return new SFTPAttributesResponse(requestId, attrs);
    }

    private async Task<SFTPResponse> RenameHandler(uint requestId, CancellationToken cancellationToken = default)
    {
        var oldpath = new SFTPPath(await reader.ReadString(cancellationToken).ConfigureAwait(false));
        var newpath = new SFTPPath(await reader.ReadString(cancellationToken).ConfigureAwait(false));
        await sftpHandler.Rename(oldpath, newpath, cancellationToken).ConfigureAwait(false);
        return BuildStatus(requestId, Status.Ok);
    }

    private async Task<SFTPResponse> ReadLinkHandler(uint requestId, CancellationToken cancellationToken = default)
    {
        var path = new SFTPPath(await reader.ReadString(cancellationToken).ConfigureAwait(false));
        var result = await sftpHandler.ReadLink(path, cancellationToken).ConfigureAwait(false);

        return new SFTPNameResponse(requestId, [result]);
    }

    private async Task<SFTPResponse> SymLinkHandler(uint requestId, CancellationToken cancellationToken = default)
    {
        // NOTE: target and link are swapped from the RFC due to OpenSSH's prevalent mistake.
        // See comment on SFTPSymLinkRequest.cs.
        SFTPPath targetpath = new SFTPPath(await reader.ReadString(cancellationToken).ConfigureAwait(false));
        SFTPPath linkpath = new SFTPPath(await reader.ReadString(cancellationToken).ConfigureAwait(false));

        await sftpHandler.SymLink(linkpath, targetpath, cancellationToken).ConfigureAwait(false);
        return BuildStatus(requestId, Status.Ok);
    }

    private async Task<SFTPResponse> ExtendedHandler(uint requestId, CancellationToken cancellationToken = default)
    {
        string requestName = await reader.ReadString(cancellationToken).ConfigureAwait(false);
        return await sftpHandler.Extended(requestId, requestName, reader, cancellationToken).ConfigureAwait(false);
    }

    private SFTPStatus BuildStatus(uint requestId, Status status, string? errorMessage = null)
    {
        if (protocolVersion >= 3)
        {
            return new(requestId, status)
            {
                ErrorMessage = errorMessage ?? sftpHandler.GetDefaultStatusString(status),
                LanguageTag = string.Empty,
            };
        }
        else
        {
            return new(requestId, status);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        ((IDisposable)writer).Dispose();
        if (sftpHandler is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
