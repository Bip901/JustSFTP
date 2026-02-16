using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JustSFTP.Protocol.Enums;
using JustSFTP.Protocol.IO;

namespace JustSFTP.Protocol.Models.Responses;

/// <summary>
/// An abstract SFTP response sent from server to client.
/// </summary>
public abstract record SFTPResponse(uint RequestId)
{
    private delegate Task<SFTPResponse> ReadAsyncMethod(
        SshStreamReader reader,
        CancellationToken cancellationToken
    );

    private static readonly Dictionary<
        ResponseType,
        ReadAsyncMethod
    > ResponseTypeToReadAsyncMethod = new()
    {
        { ResponseType.Status, SFTPStatus.ReadAsync },
        { ResponseType.Name, SFTPNameResponse.ReadAsync },
        { ResponseType.Handle, SFTPHandleResponse.ReadAsync },
        { ResponseType.Data, SFTPData.ReadAsync },
    };

    /// <summary>
    /// The write response type of this object.
    /// </summary>
    public abstract ResponseType ResponseType { get; }

    /// <summary>
    /// Serialize this response to the given stream.
    /// </summary>
    /// <exception cref="OperationCanceledException"/>
    /// <exception cref="ObjectDisposedException"/>
    public virtual async Task WriteAsync(
        SshStreamWriter writer,
        CancellationToken cancellationToken
    )
    {
        await writer.Write(ResponseType, cancellationToken).ConfigureAwait(false);
        await writer.Write(RequestId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads a server response (of any type) from the given stream.
    /// </summary>
    /// <exception cref="InvalidDataException"/>
    /// <exception cref="OperationCanceledException"/>
    /// <exception cref="ObjectDisposedException"/>
    public static async Task<SFTPResponse> ReadAsync(
        SshStreamReader reader,
        CancellationToken cancellationToken
    )
    {
        uint _messageLength = await reader.ReadUInt32(cancellationToken); // Ignore message length, all fields can be deduced from their types
        ResponseType responseType = (ResponseType)await reader.ReadByte(cancellationToken);
        if (
            !ResponseTypeToReadAsyncMethod.TryGetValue(
                responseType,
                out ReadAsyncMethod? readAsyncMethod
            )
        )
        {
            throw new InvalidDataException($"Invalid response type: {responseType}");
        }
        return await readAsyncMethod(reader, cancellationToken);
    }
}
