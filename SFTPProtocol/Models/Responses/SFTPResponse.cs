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
    /// <summary>
    /// A method that consumes and returns an SFTP response from the given reader, after its length, type and request id were already consumed.
    /// </summary>
    public delegate Task<SFTPResponse> ReadAsyncMethod(
        uint requestId,
        SshStreamReader reader,
        CancellationToken cancellationToken
    );

    private static readonly Dictionary<
        ResponseType,
        ReadAsyncMethod
    > ResponseTypeToReadAsyncMethod = new()
    {
        { ResponseType.Status, SFTPStatus.ReadAsync },
        { ResponseType.Handle, SFTPHandleResponse.ReadAsync },
        { ResponseType.Data, SFTPData.ReadAsync },
        { ResponseType.Name, SFTPNameResponse.ReadAsync },
        { ResponseType.Attributes, SFTPAttributesResponse.ReadAsync },
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
        CancellationToken cancellationToken,
        Func<uint, ReadAsyncMethod?>? getExtendedReadAsyncMethod = null
    )
    {
        uint _messageLength = await reader.ReadUInt32(cancellationToken).ConfigureAwait(false); // Ignore message length, all fields can be deduced from their types
        ResponseType responseType = (ResponseType)
            await reader.ReadByte(cancellationToken).ConfigureAwait(false);
        uint requestId = await reader.ReadUInt32(cancellationToken).ConfigureAwait(false);
        if (responseType == ResponseType.Extended)
        {
            ReadAsyncMethod? extendedReadAsyncMethod = getExtendedReadAsyncMethod?.Invoke(
                requestId
            );
            if (extendedReadAsyncMethod == null)
            {
                throw new InvalidDataException(
                    $"Don't know how to handle extended response for request {requestId}"
                );
            }
            return await extendedReadAsyncMethod(requestId, reader, cancellationToken);
        }
        if (
            !ResponseTypeToReadAsyncMethod.TryGetValue(
                responseType,
                out ReadAsyncMethod? readAsyncMethod
            )
        )
        {
            throw new InvalidDataException($"Invalid response type: {responseType}");
        }
        return await readAsyncMethod(requestId, reader, cancellationToken);
    }
}
