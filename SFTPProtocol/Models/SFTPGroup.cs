namespace JustSFTP.Protocol.Models;

/// <summary>
/// A unix permission group.
/// </summary>
/// <param name="Gid">The group id.</param>
public record SFTPGroup(uint Gid) : SFTPIdentifier(Gid)
{
    /// <summary>
    /// The "root" group.
    /// </summary>
    public static readonly SFTPGroup Root = new(0);
}
