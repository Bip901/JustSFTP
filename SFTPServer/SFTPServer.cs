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
        CancellationToken cancellationToken
    );

    /// <summary>
    /// The trace source this <see cref="SFTPServer"/> logs to.
    /// </summary>
    public TraceSource TraceSource { get; }

    private readonly SshStreamReader _reader;
    private readonly SshStreamWriter _writer;
    private readonly ISFTPHandler _sftphandler;
    private uint _protocolversion;

    private readonly Dictionary<RequestType, MessageHandler> _messageHandlers;

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
        _reader = new SshStreamReader(
            inStream ?? throw new ArgumentNullException(nameof(inStream))
        );
        _writer = new SshStreamWriter(
            outStream ?? throw new ArgumentNullException(nameof(outStream)),
            writeBufferSize
        );
        _sftphandler = sftpHandler ?? throw new ArgumentNullException(nameof(sftpHandler));

        _messageHandlers = new()
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
        uint msglength;
        do
        {
            msglength = await _reader.ReadUInt32(cancellationToken).ConfigureAwait(false);
            if (msglength == 0)
            {
                break;
            }
            // Determine message type
            RequestType requestType = (RequestType)
                await _reader.ReadByte(cancellationToken).ConfigureAwait(false);
            if (_protocolversion == 0 && requestType is RequestType.Init)
            {
                // We subtract 5 bytes (1 for requesttype and 4 for protocolversion) from msglength and pass the
                // remainder so the inithandler can parse extensions (if any)
                await InitHandler(msglength - 5, cancellationToken).ConfigureAwait(false);
            }
            else if (_protocolversion > 0)
            {
                uint requestId = await _reader.ReadUInt32(cancellationToken).ConfigureAwait(false);
                TraceSource.TraceEvent(
                    TraceEventType.Verbose,
                    TraceEventIds.SFTPServer_ReceivedRequest,
                    "RECV: #{0} {1}",
                    requestId,
                    requestType
                );
                SFTPResponse response;
                if (_messageHandlers.TryGetValue(requestType, out MessageHandler? handler))
                {
                    try
                    {
                        response = await handler(requestId, cancellationToken)
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
                await response.WriteAsync(_writer, cancellationToken).ConfigureAwait(false);
            }

            // Write response
            await _writer.Flush(cancellationToken).ConfigureAwait(false);
        } while (!cancellationToken.IsCancellationRequested && msglength > 0);
    }

    private async Task InitHandler(
        uint extensiondatalength,
        CancellationToken cancellationToken = default
    )
    {
        // Get client version
        uint clientversion = await _reader.ReadUInt32(cancellationToken).ConfigureAwait(false);
        _protocolversion = Math.Min(clientversion, SERVER_SFTP_PROTOCOL_VERSION);

        // Get client extensions (if any)
        Dictionary<string, string> clientExtensions = new Dictionary<string, string>();
        while (extensiondatalength > 0)
        {
            byte[] nameBytes = await _reader.ReadBinary(cancellationToken).ConfigureAwait(false);
            byte[] dataBytes = await _reader.ReadBinary(cancellationToken).ConfigureAwait(false);
            clientExtensions[SshStreamReader.SFTPStringEncoding.GetString(nameBytes)] =
                SshStreamReader.SFTPStringEncoding.GetString(dataBytes);
            extensiondatalength -= (uint)(nameBytes.Length + dataBytes.Length);
        }

        SFTPExtensions serverExtensions = await _sftphandler
            .Init(clientversion, new SFTPExtensions(clientExtensions), cancellationToken)
            .ConfigureAwait(false);

        // Send version response
        await _writer.Write(ResponseType.Version, cancellationToken).ConfigureAwait(false);
        await _writer.Write(_protocolversion, cancellationToken).ConfigureAwait(false);
        foreach (var pair in serverExtensions)
        {
            await _writer.Write(pair.Key, cancellationToken).ConfigureAwait(false);
            await _writer.Write(pair.Value, cancellationToken).ConfigureAwait(false);
        }

        TraceSource.TraceEvent(
            TraceEventType.Information,
            TraceEventIds.SFTPServer_InitSuccess,
            "Negotiated protocol version: {0}",
            _protocolversion
        );
    }

    private async Task<SFTPResponse> OpenHandler(
        uint requestId,
        CancellationToken cancellationToken = default
    )
    {
        var path = await _reader.ReadString(cancellationToken).ConfigureAwait(false);
        var flags = await _reader.ReadAccessFlags(cancellationToken).ConfigureAwait(false);
        var attrs = await _reader.ReadAttributes(cancellationToken).ConfigureAwait(false);
        var result = await _sftphandler
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
        CancellationToken cancellationToken = default
    )
    {
        byte[] handle = await _reader.ReadBinary(cancellationToken).ConfigureAwait(false);
        await _sftphandler.Close(handle, cancellationToken).ConfigureAwait(false);
        return BuildStatus(requestId, Status.Ok);
    }

    private async Task<SFTPResponse> ReadHandler(
        uint requestId,
        CancellationToken cancellationToken = default
    )
    {
        byte[] handle = await _reader.ReadBinary(cancellationToken).ConfigureAwait(false);
        var offset = await _reader.ReadUInt64(cancellationToken).ConfigureAwait(false);
        var len = await _reader.ReadUInt32(cancellationToken).ConfigureAwait(false);
        byte[] result = await _sftphandler
            .Read(handle, offset, len, cancellationToken)
            .ConfigureAwait(false);
        return new SFTPData(requestId, result);
    }

    private async Task<SFTPResponse> WriteHandler(
        uint requestId,
        CancellationToken cancellationToken = default
    )
    {
        byte[] handle = await _reader.ReadBinary(cancellationToken).ConfigureAwait(false);
        var offset = await _reader.ReadUInt64(cancellationToken).ConfigureAwait(false);
        var data = await _reader.ReadBinary(cancellationToken).ConfigureAwait(false);
        await _sftphandler.Write(handle, offset, data, cancellationToken).ConfigureAwait(false);
        return BuildStatus(requestId, Status.Ok);
    }

    private async Task<SFTPResponse> LStatHandler(
        uint requestId,
        CancellationToken cancellationToken = default
    )
    {
        var path = new SFTPPath(await _reader.ReadString(cancellationToken).ConfigureAwait(false));
        SFTPAttributes attrs = await _sftphandler
            .LStat(path, cancellationToken)
            .ConfigureAwait(false);
        return new SFTPAttributesResponse(requestId, attrs);
    }

    private async Task<SFTPResponse> FStatHandler(
        uint requestId,
        CancellationToken cancellationToken = default
    )
    {
        byte[] handle = await _reader.ReadBinary(cancellationToken).ConfigureAwait(false);
        SFTPAttributes attrs = await _sftphandler
            .FStat(handle, cancellationToken)
            .ConfigureAwait(false);
        return new SFTPAttributesResponse(requestId, attrs);
    }

    private async Task<SFTPResponse> SetStatHandler(
        uint requestId,
        CancellationToken cancellationToken = default
    )
    {
        var path = new SFTPPath(await _reader.ReadString(cancellationToken).ConfigureAwait(false));
        var attrs = await _reader.ReadAttributes(cancellationToken).ConfigureAwait(false);
        await _sftphandler.SetStat(path, attrs, cancellationToken).ConfigureAwait(false);
        return BuildStatus(requestId, Status.Ok);
    }

    private async Task<SFTPResponse> FSetStatHandler(
        uint requestId,
        CancellationToken cancellationToken = default
    )
    {
        byte[] handle = await _reader.ReadBinary(cancellationToken).ConfigureAwait(false);
        var attrs = await _reader.ReadAttributes(cancellationToken).ConfigureAwait(false);
        await _sftphandler.FSetStat(handle, attrs, cancellationToken).ConfigureAwait(false);
        return BuildStatus(requestId, Status.Ok);
    }

    private async Task<SFTPResponse> OpenDirHandler(
        uint requestId,
        CancellationToken cancellationToken = default
    )
    {
        SFTPPath path = new(await _reader.ReadString(cancellationToken).ConfigureAwait(false));
        byte[] result = await _sftphandler.OpenDir(path, cancellationToken).ConfigureAwait(false);
        return new SFTPHandleResponse(requestId, result);
    }

    private async Task<SFTPResponse> ReadDirHandler(
        uint requestId,
        CancellationToken cancellationToken = default
    )
    {
        byte[] handle = await _reader.ReadBinary(cancellationToken).ConfigureAwait(false);
        IEnumerator<SFTPName> enumerator = await _sftphandler
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
        CancellationToken cancellationToken = default
    )
    {
        var path = new SFTPPath(await _reader.ReadString(cancellationToken).ConfigureAwait(false));
        await _sftphandler.Remove(path, cancellationToken).ConfigureAwait(false);
        return BuildStatus(requestId, Status.Ok);
    }

    private async Task<SFTPResponse> MakeDirHandler(
        uint requestId,
        CancellationToken cancellationToken = default
    )
    {
        var path = new SFTPPath(await _reader.ReadString(cancellationToken).ConfigureAwait(false));
        var attrs = await _reader.ReadAttributes(cancellationToken).ConfigureAwait(false);
        await _sftphandler.MakeDir(path, attrs, cancellationToken).ConfigureAwait(false);
        return BuildStatus(requestId, Status.Ok);
    }

    private async Task<SFTPResponse> RemoveDirHandler(
        uint requestId,
        CancellationToken cancellationToken = default
    )
    {
        var path = new SFTPPath(await _reader.ReadString(cancellationToken).ConfigureAwait(false));
        await _sftphandler.RemoveDir(path, cancellationToken).ConfigureAwait(false);
        return BuildStatus(requestId, Status.Ok);
    }

    private async Task<SFTPResponse> RealPathHandler(
        uint requestId,
        CancellationToken cancellationToken = default
    )
    {
        var path = await _reader.ReadString(cancellationToken).ConfigureAwait(false);
        path = string.IsNullOrEmpty(path) || path == "." ? "/" : path;

        var result = await _sftphandler
            .RealPath(new SFTPPath(path), cancellationToken)
            .ConfigureAwait(false);

        return new SFTPNameResponse(requestId, [new SFTPName(result.Path, new SFTPAttributes())]);
    }

    private async Task<SFTPResponse> StatHandler(
        uint requestId,
        CancellationToken cancellationToken = default
    )
    {
        var path = new SFTPPath(await _reader.ReadString(cancellationToken).ConfigureAwait(false));
        SFTPAttributes attrs = await _sftphandler
            .Stat(path, cancellationToken)
            .ConfigureAwait(false);
        return new SFTPAttributesResponse(requestId, attrs);
    }

    private async Task<SFTPResponse> RenameHandler(
        uint requestId,
        CancellationToken cancellationToken = default
    )
    {
        var oldpath = new SFTPPath(
            await _reader.ReadString(cancellationToken).ConfigureAwait(false)
        );
        var newpath = new SFTPPath(
            await _reader.ReadString(cancellationToken).ConfigureAwait(false)
        );
        await _sftphandler.Rename(oldpath, newpath, cancellationToken).ConfigureAwait(false);
        return BuildStatus(requestId, Status.Ok);
    }

#if NET6_0_OR_GREATER
    private async Task<SFTPResponse> ReadLinkHandler(
        uint requestId,
        CancellationToken cancellationToken = default
    )
    {
        var path = new SFTPPath(await _reader.ReadString(cancellationToken).ConfigureAwait(false));
        var result = await _sftphandler.ReadLink(path, cancellationToken).ConfigureAwait(false);

        return new SFTPNameResponse(requestId, [result]);
    }

    private async Task<SFTPResponse> SymLinkHandler(
        uint requestId,
        CancellationToken cancellationToken = default
    )
    {
        //NOTE: target and link appear to be swapped from the RFC??
        //Tested with sftp (commandline tool), WinSCP and CyberDuck
        var targetpath = new SFTPPath(
            await _reader.ReadString(cancellationToken).ConfigureAwait(false)
        );
        var linkpath = new SFTPPath(
            await _reader.ReadString(cancellationToken).ConfigureAwait(false)
        );

        await _sftphandler.SymLink(linkpath, targetpath, cancellationToken).ConfigureAwait(false);
        return BuildStatus(requestId, Status.Ok);
    }
#endif

    private Task<SFTPResponse> ExtendedHandler(
        uint requestId,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotImplementedException();
    }

    private SFTPStatus BuildStatus(uint requestId, Status status, string? errorMessage = null)
    {
        if (_protocolversion >= 3)
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
        ((IDisposable)_writer).Dispose();
        if (_sftphandler is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
