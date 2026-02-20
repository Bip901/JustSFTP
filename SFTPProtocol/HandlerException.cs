using System;
using JustSFTP.Protocol.Enums;

namespace JustSFTP.Protocol;

/// <summary>
/// Thrown when a status needs to be returned to the client (instead of the usual response).
/// </summary>
/// <param name="status">The status to return to the client.</param>
/// <param name="message">Optionally, the message to display to the client.</param>
public class HandlerException(Status status, string? message = null) : Exception(message ?? status.ToString())
{
    /// <summary>
    /// The status to return to the client.
    /// </summary>
    public Status Status { get; } = status;

    /// <summary>
    /// Whether this exception was constructed with an explicit message, (true), or an automatic one was generated (false).
    /// </summary>
    public bool HasExplicitMessage { get; } = message != null;
}
