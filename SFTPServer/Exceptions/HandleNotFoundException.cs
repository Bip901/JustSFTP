using JustSFTP.Protocol.Models;

namespace JustSFTP.Server.Exceptions;

public class HandleNotFoundException : NotFoundException
{
    public SFTPHandle Handle { get; init; }

    public HandleNotFoundException(SFTPHandle handle)
        => Handle = handle;
}
