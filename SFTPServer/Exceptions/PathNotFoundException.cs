using JustSFTP.Server.Models;

namespace JustSFTP.Server.Exceptions;

public class PathNotFoundException : NotFoundException
{
    public SFTPPath Path { get; init; }

    public PathNotFoundException(SFTPPath path)
        => Path = path;
}