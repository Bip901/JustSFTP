using System;
using JustSFTP.Protocol.Enums;

namespace JustSFTP.Server.Exceptions;

public abstract class HandlerException : Exception
{
    public Status Status { get; init; }

    public HandlerException(Status status) => Status = status;
}
