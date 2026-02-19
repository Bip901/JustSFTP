using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JustSFTP.Protocol;
using JustSFTP.Protocol.Enums;
using JustSFTP.Protocol.IO;
using JustSFTP.Protocol.Models;
using JustSFTP.Protocol.Models.Responses;

namespace JustSFTP.Server;

public sealed class SFTPServer : ISFTPServer, IDisposable
{
    private const uint SERVER_SFTP_PROTOCOL_VERSION = 3;
    private delegate Task<SFTPResponse> MessageHandler(
        uint requestId,
        CancellationToken cancellationToken
    );

    private readonly SshStreamReader _reader;
    private readonly SshStreamWriter _writer;
    private readonly ISFTPHandler _sftphandler;
    private uint _protocolversion;

    private readonly Dictionary<RequestType, MessageHandler> _messageHandlers;
    private readonly ConcurrentDictionary<byte[], PagedResult<SFTPName>> _directorypages = new();

    /// <summary>
    /// Creates a new <see cref="SFTPServer"/> over the given streams, serving files from the given path.
    /// The server is not responsible for closing the streams.
    /// </summary>
    /// <exception cref="ArgumentNullException"></exception>
    public SFTPServer(
        Stream inStream,
        Stream outStream,
        SFTPPath root,
        int writeBufferSize = 1048576
    ) // 1 MiB
        : this(inStream, outStream, new DefaultSFTPHandler(root), writeBufferSize) { }

    /// <summary>
    /// Creates a new <see cref="SFTPServer"/> over the given streams, serving files using the given <see cref="ISFTPHandler"/>.
    /// The server is not responsible for closing the streams.
    /// </summary>
    /// <exception cref="ArgumentNullException"></exception>
    public SFTPServer(
        Stream inStream,
        Stream outStream,
        ISFTPHandler sftpHandler,
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
            if (msglength > 0)
            {
                // Determine message type
                var msgtype = (RequestType)
                    await _reader.ReadByte(cancellationToken).ConfigureAwait(false);
                if (_protocolversion == 0 && msgtype is RequestType.Init)
                {
                    // We subtract 5 bytes (1 for requesttype and 4 for protocolversion) from msglength and pass the
                    // remainder so the inithandler can parse extensions (if any)
                    await InitHandler(msglength - 5, cancellationToken).ConfigureAwait(false);
                }
                else if (_protocolversion > 0)
                {
                    uint requestId = await _reader
                        .ReadUInt32(cancellationToken)
                        .ConfigureAwait(false);
                    SFTPResponse response;
                    if (_messageHandlers.TryGetValue(msgtype, out var handler))
                    {
                        try
                        {
                            response = await handler(requestId, cancellationToken)
                                .ConfigureAwait(false);
                        }
                        catch (HandlerException ex)
                        {
                            response = BuildStatus(requestId, ex.Status);
                        }
                        catch
                        {
                            response = BuildStatus(requestId, Status.Failure);
                        }
                    }
                    else
                    {
                        response = BuildStatus(requestId, Status.OperationUnsupported);
                    }
                    await response.WriteAsync(_writer, cancellationToken).ConfigureAwait(false);
                }

                // Write response
                await _writer.Flush(cancellationToken).ConfigureAwait(false);
            }
        } while (!cancellationToken.IsCancellationRequested && msglength > 0);
    }

    private async Task InitHandler(
        uint extensiondatalength,
        CancellationToken cancellationToken = default
    )
    {
        // Get client version
        var clientversion = await _reader.ReadUInt32(cancellationToken).ConfigureAwait(false);
        _protocolversion = Math.Min(clientversion, SERVER_SFTP_PROTOCOL_VERSION);

        // Get client extensions (if any)
        var clientExtensions = new Dictionary<string, string>();
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
        foreach (var e in serverExtensions)
        {
            await _writer.Write(e.Key, cancellationToken).ConfigureAwait(false);
            await _writer.Write(e.Value, cancellationToken).ConfigureAwait(false);
        }
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
        _directorypages.TryRemove(handle, out _);
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

        // Retrieve results (if not already done for this handle) and put into PagedResults
        PagedResult<SFTPName> pagedResults = _directorypages.GetOrAdd(
            handle,
            new PagedResult<SFTPName>(
                await _sftphandler.ReadDir(handle, cancellationToken).ConfigureAwait(false)
            )
        );
        // Get next page
        IEnumerable<SFTPName> page = pagedResults.NextPage();
        if (page.Any())
        {
            return new SFTPNameResponse(requestId, [.. page]);
        }
        else
        {
            // Remove paged results and send "EOF"
            _directorypages.TryRemove(handle, out _);
            return BuildStatus(requestId, Status.EndOfFile);
        }
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

        return new SFTPNameResponse(requestId, [SFTPName.FromString(result.Path)]);
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

    private SFTPStatus BuildStatus(uint requestId, Status status)
    {
        if (_protocolversion >= 3)
        {
            return new(requestId, status)
            {
                ErrorMessage = GetStatusString(status),
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

    private class PagedResult<T>
    {
        private readonly IList<T> _results;
        private readonly int _pagesize;
        private int _page;

        public PagedResult(IEnumerable<T> items, int pagesize = 100)
        {
            _results = items.ToList();
            _pagesize = pagesize;
            _page = 0;
        }

        public IEnumerable<T> NextPage() => _results.Skip(_page++ * _pagesize).Take(_pagesize);
    }
}
