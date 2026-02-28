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
    private const uint SERVER_SFTP_PROTOCOL_VERSION = 3;
    private const int READ_DIR_PAGE_SIZE = 128;
    private delegate Task<SFTPResponse> MessageHandler(
        uint requestId,
        uint remainingLength,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// The trace source this <see cref="SFTPServer"/> logs to.
    /// </summary>
    public TraceSource TraceSource { get; }

    private readonly SshStreamReader reader;
    private readonly SshStreamWriter writer;
    private readonly ISFTPHandler sftpHandler;
    private uint protocolVersion;

    private readonly Dictionary<RequestType, MessageHandler> messageHandlers;

    /// <summary>
    /// Creates a new <see cref="SFTPServer"/> over the given streams, serving files from the given path.
    /// The server is not responsible for closing the streams.
    /// </summary>
    /// <param name="inStream">The stream to read from.</param>
    /// <param name="outStream">The stream to write to.</param>
    /// <param name="root">The root path in the local filesystem to serve from.</param>
    /// <param name="writeBufferSize">The write buffer size in bytes. Longer messages will not be able to be written.</param>
    /// <param name="traceSource">Optionally, a trace source to log to. Defaults to a silent trace source. See also: <seealso cref="TraceEventIds"/>.</param>
    /// <exception cref="ArgumentNullException"></exception>
    public SFTPServer(
        Stream inStream,
        Stream outStream,
        SFTPPath root,
        TraceSource? traceSource = null,
        int writeBufferSize = 1048576
    ) // 1 MiB
        : this(inStream, outStream, new DefaultSFTPHandler(root), traceSource, writeBufferSize) { }

    /// <summary>
    /// Creates a new <see cref="SFTPServer"/> over the given streams, serving files using the given <see cref="ISFTPHandler"/>.
    /// The server is not responsible for closing the streams.
    /// </summary>
    /// <param name="inStream">The stream to read from.</param>
    /// <param name="outStream">The stream to write to.</param>
    /// <param name="sftpHandler">The SFTP handler.</param>
    /// <param name="writeBufferSize">The write buffer size in bytes. Longer messages will not be able to be written.</param>
    /// <param name="traceSource">Optionally, a trace source to log to. Defaults to a silent trace source. See also: <seealso cref="TraceEventIds"/>.</param>
    /// <exception cref="ArgumentNullException"></exception>
    public SFTPServer(
        Stream inStream,
        Stream outStream,
        ISFTPHandler sftpHandler,
        TraceSource? traceSource = null,
        int writeBufferSize = 1048576
    ) // 1 MiB
    {
        reader = new SshStreamReader(inStream ?? throw new ArgumentNullException(nameof(inStream)));
        writer = new SshStreamWriter(
            outStream ?? throw new ArgumentNullException(nameof(outStream)),
            writeBufferSize
        );
        this.sftpHandler = sftpHandler ?? throw new ArgumentNullException(nameof(sftpHandler));

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
#if NET6_0_OR_GREATER
            { RequestType.ReadLink, ReadLinkHandler },
            { RequestType.SymLink, SymLinkHandler },
#endif
            { RequestType.Extended, ExtendedHandler },
        };

        TraceSource = traceSource ?? new TraceSource(nameof(SFTPServer), SourceLevels.Off);
    }

    /// <summary>
    /// Runs this server until canceled.
    /// </summary>
    /// <exception cref="OperationCanceledException"/>
    public async Task Run(CancellationToken cancellationToken = default)
    {
        uint msgLength;
        do
        {
            msgLength = await reader.ReadUInt32(cancellationToken).ConfigureAwait(false);
            if (msgLength == 0)
            {
                break;
            }
            // Determine message type
            RequestType requestType = (RequestType)
                await reader.ReadByte(cancellationToken).ConfigureAwait(false);
            if (protocolVersion == 0 && requestType is RequestType.Init)
            {
                // We subtract 5 bytes (1 for requestType and 4 for protocolVersion) from msgLength and pass the
                // remainder so the InitHandler can parse extensions (if any)
                await InitHandler(msgLength - sizeof(RequestType) - sizeof(uint), cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (protocolVersion > 0)
            {
                uint requestId = await reader.ReadUInt32(cancellationToken).ConfigureAwait(false);
                TraceSource.TraceEvent(
                    TraceEventType.Verbose,
                    TraceEventIds.SFTPServer_ReceivedRequest,
                    "RECV: #{0} {1}",
                    requestId,
                    requestType
                );
                SFTPResponse response;
                if (messageHandlers.TryGetValue(requestType, out MessageHandler? handler))
                {
                    try
                    {
                        response = await handler(
                                requestId,
                                msgLength - sizeof(RequestType) - sizeof(uint),
                                cancellationToken
                            )
                            .ConfigureAwait(false);
                    }
                    catch (HandlerException ex)
                    {
                        response = BuildStatus(
                            requestId,
                            ex.Status,
                            ex.HasExplicitMessage ? ex.Message : null
                        );
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
                        response = BuildStatus(requestId, Status.Failure);
                    }
                }
                else
                {
                    response = BuildStatus(requestId, Status.OperationUnsupported);
                }
                TraceSource.TraceEvent(
                    TraceEventType.Verbose,
                    TraceEventIds.SFTPServer_SendingResponse,
                    "SEND: {0}",
                    response
                );
                await response.WriteAsync(writer, cancellationToken).ConfigureAwait(false);
            }

            // Write response
            await writer.Flush(cancellationToken).ConfigureAwait(false);
        } while (!cancellationToken.IsCancellationRequested && msgLength > 0);
    }

    private async Task InitHandler(
        uint extensionDataLength,
        CancellationToken cancellationToken = default
    )
    {
        // Get client version
        uint clientversion = await reader.ReadUInt32(cancellationToken).ConfigureAwait(false);
        protocolVersion = Math.Min(clientversion, SERVER_SFTP_PROTOCOL_VERSION);

        // Get client extensions (if any)
        Dictionary<string, string> clientExtensions = [];
        while (extensionDataLength > 0)
        {
            byte[] nameBytes = await reader.ReadBinary(cancellationToken).ConfigureAwait(false);
            byte[] dataBytes = await reader.ReadBinary(cancellationToken).ConfigureAwait(false);
            clientExtensions[SshStreamReader.SFTPStringEncoding.GetString(nameBytes)] =
                SshStreamReader.SFTPStringEncoding.GetString(dataBytes);
            extensionDataLength -= (uint)(
                sizeof(uint) + nameBytes.Length + sizeof(uint) + dataBytes.Length
            );
        }

        SFTPExtensions serverExtensions = await sftpHandler
            .Init(clientversion, new SFTPExtensions(clientExtensions), cancellationToken)
            .ConfigureAwait(false);

        // Send version response
        await writer.Write(ResponseType.Version, cancellationToken).ConfigureAwait(false);
        await writer.Write(protocolVersion, cancellationToken).ConfigureAwait(false);
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

    private async Task<SFTPResponse> OpenHandler(
        uint requestId,
        uint remainingLength,
        CancellationToken cancellationToken = default
    )
    {
        var path = await reader.ReadString(cancellationToken).ConfigureAwait(false);
        var flags = await reader.ReadAccessFlags(cancellationToken).ConfigureAwait(false);
        var attrs = await reader.ReadAttributes(cancellationToken).ConfigureAwait(false);
        var result = await sftpHandler
            .Open(
                new SFTPPath(path),
                flags.ToFileMode(),
                flags.ToFileAccess(),
                attrs,
                cancellationToken
            )
            .ConfigureAwait(false);
        return new SFTPHandleResponse(requestId, result);
    }

    private async Task<SFTPResponse> CloseHandler(
        uint requestId,
        uint remainingLength,
        CancellationToken cancellationToken = default
    )
    {
        byte[] handle = await reader.ReadBinary(cancellationToken).ConfigureAwait(false);
        await sftpHandler.Close(handle, cancellationToken).ConfigureAwait(false);
        return BuildStatus(requestId, Status.Ok);
    }

    private async Task<SFTPResponse> ReadHandler(
        uint requestId,
        uint remainingLength,
        CancellationToken cancellationToken = default
    )
    {
        byte[] handle = await reader.ReadBinary(cancellationToken).ConfigureAwait(false);
        var offset = await reader.ReadUInt64(cancellationToken).ConfigureAwait(false);
        var len = await reader.ReadUInt32(cancellationToken).ConfigureAwait(false);
        byte[] result = await sftpHandler
            .Read(handle, offset, len, cancellationToken)
            .ConfigureAwait(false);
        return new SFTPData(requestId, result);
    }

    private async Task<SFTPResponse> WriteHandler(
        uint requestId,
        uint remainingLength,
        CancellationToken cancellationToken = default
    )
    {
        byte[] handle = await reader.ReadBinary(cancellationToken).ConfigureAwait(false);
        var offset = await reader.ReadUInt64(cancellationToken).ConfigureAwait(false);
        var data = await reader.ReadBinary(cancellationToken).ConfigureAwait(false);
        await sftpHandler.Write(handle, offset, data, cancellationToken).ConfigureAwait(false);
        return BuildStatus(requestId, Status.Ok);
    }

    private async Task<SFTPResponse> LStatHandler(
        uint requestId,
        uint remainingLength,
        CancellationToken cancellationToken = default
    )
    {
        var path = new SFTPPath(await reader.ReadString(cancellationToken).ConfigureAwait(false));
        SFTPAttributes attrs = await sftpHandler
            .LStat(path, cancellationToken)
            .ConfigureAwait(false);
        return new SFTPAttributesResponse(requestId, attrs);
    }

    private async Task<SFTPResponse> FStatHandler(
        uint requestId,
        uint remainingLength,
        CancellationToken cancellationToken = default
    )
    {
        byte[] handle = await reader.ReadBinary(cancellationToken).ConfigureAwait(false);
        SFTPAttributes attrs = await sftpHandler
            .FStat(handle, cancellationToken)
            .ConfigureAwait(false);
        return new SFTPAttributesResponse(requestId, attrs);
    }

    private async Task<SFTPResponse> SetStatHandler(
        uint requestId,
        uint remainingLength,
        CancellationToken cancellationToken = default
    )
    {
        var path = new SFTPPath(await reader.ReadString(cancellationToken).ConfigureAwait(false));
        var attrs = await reader.ReadAttributes(cancellationToken).ConfigureAwait(false);
        await sftpHandler.SetStat(path, attrs, cancellationToken).ConfigureAwait(false);
        return BuildStatus(requestId, Status.Ok);
    }

    private async Task<SFTPResponse> FSetStatHandler(
        uint requestId,
        uint remainingLength,
        CancellationToken cancellationToken = default
    )
    {
        byte[] handle = await reader.ReadBinary(cancellationToken).ConfigureAwait(false);
        var attrs = await reader.ReadAttributes(cancellationToken).ConfigureAwait(false);
        await sftpHandler.FSetStat(handle, attrs, cancellationToken).ConfigureAwait(false);
        return BuildStatus(requestId, Status.Ok);
    }

    private async Task<SFTPResponse> OpenDirHandler(
        uint requestId,
        uint remainingLength,
        CancellationToken cancellationToken = default
    )
    {
        SFTPPath path = new(await reader.ReadString(cancellationToken).ConfigureAwait(false));
        byte[] result = await sftpHandler.OpenDir(path, cancellationToken).ConfigureAwait(false);
        return new SFTPHandleResponse(requestId, result);
    }

    private async Task<SFTPResponse> ReadDirHandler(
        uint requestId,
        uint remainingLength,
        CancellationToken cancellationToken = default
    )
    {
        byte[] handle = await reader.ReadBinary(cancellationToken).ConfigureAwait(false);
        IEnumerator<SFTPName> enumerator = await sftpHandler
            .ReadDir(handle, cancellationToken)
            .ConfigureAwait(false);
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

    private async Task<SFTPResponse> RemoveHandler(
        uint requestId,
        uint remainingLength,
        CancellationToken cancellationToken = default
    )
    {
        var path = new SFTPPath(await reader.ReadString(cancellationToken).ConfigureAwait(false));
        await sftpHandler.Remove(path, cancellationToken).ConfigureAwait(false);
        return BuildStatus(requestId, Status.Ok);
    }

    private async Task<SFTPResponse> MakeDirHandler(
        uint requestId,
        uint remainingLength,
        CancellationToken cancellationToken = default
    )
    {
        var path = new SFTPPath(await reader.ReadString(cancellationToken).ConfigureAwait(false));
        var attrs = await reader.ReadAttributes(cancellationToken).ConfigureAwait(false);
        await sftpHandler.MakeDir(path, attrs, cancellationToken).ConfigureAwait(false);
        return BuildStatus(requestId, Status.Ok);
    }

    private async Task<SFTPResponse> RemoveDirHandler(
        uint requestId,
        uint remainingLength,
        CancellationToken cancellationToken = default
    )
    {
        var path = new SFTPPath(await reader.ReadString(cancellationToken).ConfigureAwait(false));
        await sftpHandler.RemoveDir(path, cancellationToken).ConfigureAwait(false);
        return BuildStatus(requestId, Status.Ok);
    }

    private async Task<SFTPResponse> RealPathHandler(
        uint requestId,
        uint remainingLength,
        CancellationToken cancellationToken = default
    )
    {
        var path = await reader.ReadString(cancellationToken).ConfigureAwait(false);
        path = string.IsNullOrEmpty(path) || path == "." ? "/" : path;

        var result = await sftpHandler
            .RealPath(new SFTPPath(path), cancellationToken)
            .ConfigureAwait(false);

        return new SFTPNameResponse(requestId, [new SFTPName(result.Path, new SFTPAttributes())]);
    }

    private async Task<SFTPResponse> StatHandler(
        uint requestId,
        uint remainingLength,
        CancellationToken cancellationToken = default
    )
    {
        var path = new SFTPPath(await reader.ReadString(cancellationToken).ConfigureAwait(false));
        SFTPAttributes attrs = await sftpHandler
            .Stat(path, cancellationToken)
            .ConfigureAwait(false);
        return new SFTPAttributesResponse(requestId, attrs);
    }

    private async Task<SFTPResponse> RenameHandler(
        uint requestId,
        uint remainingLength,
        CancellationToken cancellationToken = default
    )
    {
        var oldpath = new SFTPPath(
            await reader.ReadString(cancellationToken).ConfigureAwait(false)
        );
        var newpath = new SFTPPath(
            await reader.ReadString(cancellationToken).ConfigureAwait(false)
        );
        await sftpHandler.Rename(oldpath, newpath, cancellationToken).ConfigureAwait(false);
        return BuildStatus(requestId, Status.Ok);
    }

#if NET6_0_OR_GREATER
    private async Task<SFTPResponse> ReadLinkHandler(
        uint requestId,
        uint remainingLength,
        CancellationToken cancellationToken = default
    )
    {
        var path = new SFTPPath(await reader.ReadString(cancellationToken).ConfigureAwait(false));
        var result = await sftpHandler.ReadLink(path, cancellationToken).ConfigureAwait(false);

        return new SFTPNameResponse(requestId, [result]);
    }

    private async Task<SFTPResponse> SymLinkHandler(
        uint requestId,
        uint remainingLength,
        CancellationToken cancellationToken = default
    )
    {
        //NOTE: target and link appear to be swapped from the RFC??
        //Tested with sftp (commandline tool), WinSCP and CyberDuck
        var targetpath = new SFTPPath(
            await reader.ReadString(cancellationToken).ConfigureAwait(false)
        );
        var linkpath = new SFTPPath(
            await reader.ReadString(cancellationToken).ConfigureAwait(false)
        );

        await sftpHandler.SymLink(linkpath, targetpath, cancellationToken).ConfigureAwait(false);
        return BuildStatus(requestId, Status.Ok);
    }
#endif

    private async Task<SFTPResponse> ExtendedHandler(
        uint requestId,
        uint remainingLength,
        CancellationToken cancellationToken = default
    )
    {
        (string requestName, int requestNameLength) = await reader
            .ReadStringAndLength(cancellationToken)
            .ConfigureAwait(false);
        byte[] restOfRequest = await reader
            .ReadBinary((int)remainingLength - requestNameLength, cancellationToken)
            .ConfigureAwait(false);
        using MemoryStream memoryStream = new(restOfRequest);
        return await sftpHandler
            .Extended(requestId, requestName, memoryStream, cancellationToken)
            .ConfigureAwait(false);
    }

    private SFTPStatus BuildStatus(uint requestId, Status status, string? errorMessage = null)
    {
        if (protocolVersion >= 3)
        {
            return new(requestId, status)
            {
                ErrorMessage = errorMessage ?? GetStatusString(status),
                LanguageTag = string.Empty,
            };
        }
        else
        {
            return new(requestId, status);
        }
    }

    private static string GetStatusString(Status status) =>
        status switch
        {
            Status.Ok => "Success",
            Status.EndOfFile => "End of file",
            Status.NoSuchFile => "No such file",
            Status.PermissionDenied => "Permission denied",
            Status.Failure => "Failure",
            Status.BadMessage => "Bad message",
            Status.NoConnection => "No connection",
            Status.ConnectionLost => "Connection lost",
            Status.OperationUnsupported => "Operation unsupported",
            _ => "Unknown error",
        };

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
