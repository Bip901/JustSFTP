using JustSFTP.Server.Enums;

namespace JustSFTP.Server.Exceptions;

public abstract class NotFoundException : HandlerException
{
    public NotFoundException()
        : base(Status.NoSuchFile) { }
}