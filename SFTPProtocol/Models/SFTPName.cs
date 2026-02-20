using System.IO;

namespace JustSFTP.Protocol.Models;

public record SFTPName(string Name, string LongName, SFTPAttributes Attributes)
{
    public SFTPName(string name, SFTPAttributes attributes)
        : this(name, attributes.GetLongFileName(name), attributes) { }

    public static SFTPName FromFileSystemInfo(FileSystemInfo fileSystemInfo) =>
        new(fileSystemInfo.Name, SFTPAttributes.FromFileSystemInfo(fileSystemInfo));

    public static SFTPName FromString(string Name, bool IsDirectory = false) =>
        new(Name, IsDirectory ? SFTPAttributes.DummyDirectory : SFTPAttributes.DummyFile);

    public override string ToString()
    {
        return LongName;
    }
}
