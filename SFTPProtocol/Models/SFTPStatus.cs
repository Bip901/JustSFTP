using System;
using System.Threading;
using System.Threading.Tasks;
using JustSFTP.Protocol.Enums;
using JustSFTP.Protocol.IO;

namespace JustSFTP.Protocol.Models;

/// <summary>
/// SSH_FXP_STATUS
/// </summary>
public record SFTPStatus(uint RequestId, Status Status) : SFTPResponse(RequestId)
{
    /// <inheritdoc/>
    public override ResponseType ResponseType => ResponseType.Status;

    /// <summary>
    /// UTF-8 Error message.
    /// Only sent in protocol version >= 3.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Language tag as defined in https://datatracker.ietf.org/doc/html/rfc1766.
    /// Only sent in protocol version >= 3.
    /// </summary>
    public string? LanguageTag { get; init; }

    /// <inheritdoc/>
    public override async Task WriteAsync(
        SshStreamWriter writer,
        CancellationToken cancellationToken
    )
    {
        await base.WriteAsync(writer, cancellationToken).ConfigureAwait(false);
        await writer.Write(Status, cancellationToken).ConfigureAwait(false);
        if (ErrorMessage != null && LanguageTag != null) // Protocol version >= 3
        {
            await writer.Write(ErrorMessage, cancellationToken).ConfigureAwait(false);
            await writer.Write(LanguageTag, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Deserialize an <see cref="SFTPStatus"/> from the given stream.
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
        Status status = (Status)await reader.ReadByte(cancellationToken).ConfigureAwait(false);
        string? errorMessage = null,
            languageTag = null;
        if (protocolVersion >= 3)
        {
            errorMessage = await reader.ReadString(cancellationToken).ConfigureAwait(false);
            languageTag = await reader.ReadString(cancellationToken).ConfigureAwait(false);
        }
        return new SFTPStatus(requestId, status)
        {
            ErrorMessage = errorMessage,
            LanguageTag = languageTag,
        };
    }
}
