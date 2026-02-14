using JustSFTP.Protocol.Enums;

namespace JustSFTP.Server.Exceptions;

public class PermissionException : HandlerException
{
    public string? Reason { get; init; }

    public PermissionException(string? reason = null)
        : base(Status.PermissionDenied)
        => Reason = reason;
}