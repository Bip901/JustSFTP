using System;
using JustSFTP.Server.Enums;

namespace JustSFTP.Server.Exceptions;

public abstract class HandlerException : Exception
{
    public Status Status { get; init; }

    public HandlerException(Status status) => Status = status;
}
