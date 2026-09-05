using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JustSFTP.Protocol.Enums;
using JustSFTP.Protocol.IO;

namespace JustSFTP.Protocol.Models.Responses.Extended;

/// <summary>
/// The response of a <see cref="Requests.Extended.SFTPOpenDirEagerRequest"/>.
/// </summary>
public record SFTPOpenDirEagerResponse(uint RequestId, byte[] Handle, IReadOnlyCollection<SFTPName> Names)
    : SFTPResponse(RequestId)
{
    /// <inheritdoc/>
    public override ResponseType ResponseType => ResponseType.Extended;

    /// <summary>
    /// Whether the response contains a non-empty handle.
    /// If so, there is more to be read, and the caller is responsible to close the handle.
    /// </summary>
    public bool HasHandle => Handle.Length > 0;

    /// <inheritdoc/>
    public override async Task WriteAsync(SshStreamWriter writer, CancellationToken cancellationToken)
    {
        await base.WriteAsync(writer, cancellationToken).ConfigureAwait(false);
        await writer.Write(Handle.Length, cancellationToken).ConfigureAwait(false);
        await writer.Write(Handle, cancellationToken).ConfigureAwait(false);
        await writer.Write(Names.Count, cancellationToken).ConfigureAwait(false);
        foreach (SFTPName name in Names)
        {
            await writer.Write(name, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Deserialize an <see cref="SFTPOpenDirEagerResponse"/> from the given stream.
    /// </summary>
    /// <exception cref="OperationCanceledException"/>
    /// <exception cref="ObjectDisposedException"/>
    public static async Task<SFTPResponse> ReadAsync(
        uint requestId,
        SshStreamReader reader,
        CancellationToken cancellationToken
    )
    {
        byte[] handle = await reader.ReadBinary(cancellationToken).ConfigureAwait(false);
        int count = (int)await reader.ReadUInt32(cancellationToken).ConfigureAwait(false);
        List<SFTPName> names = new(count);
        for (int i = 0; i < count; i++)
        {
            string name = await reader.ReadString(cancellationToken).ConfigureAwait(false);
            string longName = await reader.ReadString(cancellationToken).ConfigureAwait(false);
            SFTPAttributes attrs = await reader.ReadAttributes(cancellationToken).ConfigureAwait(false);
            names.Add(new SFTPName(name, longName, attrs));
        }

        return new SFTPOpenDirEagerResponse(requestId, handle, names);
    }
}
