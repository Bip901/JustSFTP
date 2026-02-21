namespace JustSFTP.Protocol.Models;

/// <summary>
/// A unix user.
/// </summary>
public record SFTPUser : SFTPIdentifier
{
    /// <summary>
    /// The "root" user.
    /// </summary>
    public static readonly SFTPUser Root = new(0);

    /// <param name="uid">The user id.</param>
    public SFTPUser(uint uid)
        : base(uid) { }

    public override string ToString()
    {
        return base.ToString();
    }
}
