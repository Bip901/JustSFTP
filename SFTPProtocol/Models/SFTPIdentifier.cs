namespace JustSFTP.Protocol.Models;

/// <summary>
/// A user or group.
/// </summary>
/// <param name="Id">The uid or gid.</param>
public abstract record SFTPIdentifier(uint Id)
{
    /// <summary>
    /// Returns the name of the user or group.
    /// </summary>
    public string Name => Id == 0 ? "root" : $"user_{Id}";
}
