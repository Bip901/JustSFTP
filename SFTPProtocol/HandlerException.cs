using System;
using JustSFTP.Protocol.Enums;

namespace JustSFTP.Protocol;

/// <summary>
/// Thrown when a status needs to be returned to the client (instead of the usual response).
/// </summary>
public class HandlerException : Exception
{
    /// <summary>
    /// The status to return to the client.
    /// </summary>
    public Status Status { get; }

    /// <summary>
    /// Whether this exception was constructed with an explicit message, (true), or an automatic one was generated (false).
    /// </summary>
    public bool HasExplicitMessage { get; }

    /// <param name="status">The status to return to the client.</param>
    /// <param name="message">Optionally, the message to display to the client.</param>
    /// <param name="innerException">Optionally, the exception that caused this.</param>
    public HandlerException(Status status, string? message, Exception? innerException)
        : base(message ?? status.ToString(), innerException)
    {
        Status = status;
        HasExplicitMessage = message != null;
    }

    /// <param name="status">The status to return to the client.</param>
    /// <param name="message">Optionally, the message to display to the client.</param>
    public HandlerException(Status status, string? message = null)
        : this(status, message, null) { }
}
