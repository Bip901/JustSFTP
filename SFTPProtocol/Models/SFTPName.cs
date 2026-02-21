using System.IO;

namespace JustSFTP.Protocol.Models;

/// <summary>
/// A record describing a file or directory.
/// </summary>
/// <param name="Name">The name of the file or directory, including the extension.</param>
/// <param name="LongName">The line that would describe this item in the unix `ls -l` command.</param>
/// <param name="Attributes">The filesystem attributes of this item.</param>
public record SFTPName(string Name, string LongName, SFTPAttributes Attributes)
{
    /// <summary>
    /// Creates a new <see cref="SFTPName"/> with an auto-generated <see cref="LongName"/>.
    /// </summary>
    public SFTPName(string name, SFTPAttributes attributes)
        : this(name, attributes.GetLongFileName(name), attributes) { }

    /// <summary>
    /// Constructs a new <see cref="SFTPName"/> instance that matches the given <see cref="FileSystemInfo"/>.
    /// </summary>
    public static SFTPName FromFileSystemInfo(FileSystemInfo fileSystemInfo)
    {
        return new(fileSystemInfo.Name, SFTPAttributes.FromFileSystemInfo(fileSystemInfo));
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return LongName;
    }
}
