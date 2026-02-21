namespace JustSFTP.Protocol.Models;

/// <summary>
/// A unix permission group.
/// </summary>
public record SFTPGroup : SFTPIdentifier
{
    /// <summary>
    /// The "root" group.
    /// </summary>
    public static readonly SFTPGroup Root = new(0);

    /// <param name="gid">The group id.</param>
    public SFTPGroup(uint gid)
        : base(gid) { }
}
