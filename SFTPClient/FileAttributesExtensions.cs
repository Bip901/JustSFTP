using FileAbstractions;
using JustSFTP.Protocol.Enums;
using JustSFTP.Protocol.Models;

namespace JustSFTP.Client;

public static class FileAttributesExtensions
{
    public static FileAttributes ToFileAttributes(this SFTPAttributes sftpAttributes)
    {
        bool isDirectory = false;
        if (sftpAttributes.Permissions.HasValue)
        {
            isDirectory = sftpAttributes.Permissions.Value.HasFlag(PosixFileMode.Directory);
        }
        return new FileAttributes()
        {
            FileSize = sftpAttributes.FileSize,
            IsDirectory = isDirectory,
            LastModifiedTime = sftpAttributes.LastModifiedTime,
            LastAccessedTime = sftpAttributes.LastAccessedTime,
        };
    }

    public static SFTPAttributes ToSFTPAttributes(this FileAttributes attributes)
    {
        return attributes.ToSFTPAttributes(
            attributes.IsDirectory ? PosixFileMode.DefaultDirectory : PosixFileMode.DefaultFile
        );
    }

    public static SFTPAttributes ToSetStatSFTPAttributes(this FileAttributes attributes)
    {
        return attributes.ToSFTPAttributes(null);
    }

    private static SFTPAttributes ToSFTPAttributes(this FileAttributes attributes, PosixFileMode? permissions)
    {
        return new SFTPAttributes()
        {
            FileSize = attributes.FileSize,
            Permissions = permissions,
            // Both timestamps must be set to appear in the SFTP protocol. If only one of them is given,
            // supply a sensible fake value for the other one.
            LastModifiedTime =
                attributes.LastAccessedTime.HasValue && !attributes.LastModifiedTime.HasValue
                    ? attributes.LastAccessedTime
                    : attributes.LastModifiedTime,
            LastAccessedTime =
                attributes.LastModifiedTime.HasValue && !attributes.LastAccessedTime.HasValue
                    ? attributes.LastModifiedTime
                    : attributes.LastAccessedTime,
        };
    }
}
