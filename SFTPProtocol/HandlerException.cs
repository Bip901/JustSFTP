using System;
using JustSFTP.Protocol.Enums;

namespace JustSFTP.Protocol;

/// <summary>
/// Thrown when a status needs to be returned to the client (instead of the usual response).
/// </summary>
public class HandlerException(Status Status) : Exception
{
    /// <summary>
    /// The status to return to the client.
    /// </summary>
    public Status Status { get; init; } = Status;
}
