namespace JustSFTP.Protocol.Models;

/// <summary>
/// A unix user.
/// </summary>
/// <param name="Uid">The user id.</param>
public record SFTPUser(uint Uid) : SFTPIdentifier(Uid)
{
    /// <summary>
    /// The "root" user.
    /// </summary>
    public static readonly SFTPUser Root = new(0);
}
