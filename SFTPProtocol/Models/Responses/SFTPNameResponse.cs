using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JustSFTP.Protocol.Enums;
using JustSFTP.Protocol.IO;

namespace JustSFTP.Protocol.Models.Responses;

/// <summary>
/// SSH_FXP_NAME
/// </summary>
public record SFTPNameResponse(uint RequestId, IReadOnlyCollection<SFTPName> Names)
    : SFTPResponse(RequestId)
{
    /// <inheritdoc/>
    public override ResponseType ResponseType => ResponseType.Name;

    /// <inheritdoc/>
    public override async Task WriteAsync(
        SshStreamWriter writer,
        CancellationToken cancellationToken
    )
    {
        await base.WriteAsync(writer, cancellationToken).ConfigureAwait(false);
        await writer.Write(Names.Count, cancellationToken).ConfigureAwait(false);
        foreach (SFTPName name in Names)
        {
            await writer.Write(name, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Deserialize an <see cref="SFTPNameResponse"/> from the given stream.
    /// </summary>
    /// <exception cref="OperationCanceledException"/>
    /// <exception cref="ObjectDisposedException"/>
    public static new async Task<SFTPResponse> ReadAsync(
        SshStreamReader reader,
        uint protocolVersion,
        CancellationToken cancellationToken
    )
    {
        uint requestId = await reader.ReadUInt32(cancellationToken).ConfigureAwait(false);
        var count = (int)await reader.ReadUInt32(cancellationToken).ConfigureAwait(false);
        var names = new List<SFTPName>(count);
        for (var i = 0; i < count; i++)
        {
            var name = await reader.ReadString(cancellationToken).ConfigureAwait(false);
            var longName = await reader.ReadString(cancellationToken).ConfigureAwait(false);
            var attrs = await reader.ReadAttributes(cancellationToken).ConfigureAwait(false);
            names.Add(new SFTPName(name, longName, attrs));
        }

        return new SFTPNameResponse(requestId, names);
    }
}
