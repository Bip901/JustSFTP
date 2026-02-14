using JustSFTP.Protocol.Enums;

namespace JustSFTP.Server.Exceptions;

public abstract class NotFoundException : HandlerException
{
    public NotFoundException()
        : base(Status.NoSuchFile) { }
}