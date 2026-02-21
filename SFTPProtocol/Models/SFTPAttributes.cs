using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using JustSFTP.Protocol.Enums;

namespace JustSFTP.Protocol.Models;

/// <summary>
/// The ATTRS component of SSH_FXP_SETSTAT and SSH_FXP_ATTRS
/// </summary>
public record SFTPAttributes
{
    /// <summary>
    /// A seemingly empty file, owned by root, modified and accessed in the unix epoch.
    /// </summary>
    public static readonly SFTPAttributes DummyFile = new()
    {
        FileSize = 0,
        User = SFTPUser.Root,
        Group = SFTPGroup.Root,
        Permissions = Enums.Permissions.DefaultFile,
        LastAccessedTime = DateTimeOffset.UnixEpoch,
        LastModifiedTime = DateTimeOffset.UnixEpoch,
    };

    /// <summary>
    /// A dummy directory, owned by root, modified and accessed in the unix epoch.
    /// </summary>
    public static readonly SFTPAttributes DummyDirectory = DummyFile with
    {
        Permissions = Enums.Permissions.DefaultDirectory,
    };

    /// <summary>
    /// The size of the file in bytes.
    /// </summary>
    public ulong? FileSize { get; init; }

    /// <summary>
    /// The user that owns this file.
    /// </summary>
    /// <remarks>This should be null if and only if <see cref="Group"/> is null.</remarks>
    public SFTPUser? User { get; init; }

    /// <summary>
    /// The group that owns this file.
    /// </summary>
    /// <remarks>This should be null if and only if <see cref="User"/> is null.</remarks>
    public SFTPGroup? Group { get; init; }

    /// <summary>
    /// The unix file permissions of this file or directory.
    /// </summary>
    public Permissions? Permissions { get; init; }

    /// <summary>
    /// The last file read time, UTC.
    /// </summary>
    /// <remarks>This should be null if and only if <see cref="LastModifiedTime"/> is null.</remarks>
    public DateTimeOffset? LastAccessedTime { get; init; }

    /// <summary>
    /// The last file write time, UTC.
    /// </summary>
    /// <remarks>This should be null if and only if <see cref="LastAccessedTime"/> is null.</remarks>
    public DateTimeOffset? LastModifiedTime { get; init; }

    /// <summary>
    /// Additional attributes as per the <a href="https://datatracker.ietf.org/doc/html/draft-ietf-secsh-filexfer-02">SFTP v3 Specifications</a>.
    /// </summary>
    public IDictionary<string, string>? ExtendedAttributes { get; init; }

    /// <summary>
    /// The pflags calculated from the null properties of this object.
    /// </summary>
    public PFlags PFlags
    {
        get
        {
            PFlags result = PFlags.None;
            if (FileSize != null)
            {
                result |= PFlags.Size;
            }
            if (User != null && Group != null)
            {
                result |= PFlags.UidGid;
            }
            if (Permissions != null)
            {
                result |= PFlags.Permissions;
            }
            if (LastAccessedTime != null && LastModifiedTime != null)
            {
                result |= PFlags.AccessModifiedTime;
            }
            if (ExtendedAttributes != null)
            {
                result |= PFlags.Extended;
            }
            return result;
        }
    }

    /// <summary>
    /// Constructs a new <see cref="SFTPAttributes"/> instance that matches the given <see cref="FileSystemInfo"/>.
    /// </summary>
    public static SFTPAttributes FromFileSystemInfo(FileSystemInfo fileSystemInfo)
    {
        return new()
        {
            FileSize = fileSystemInfo switch
            {
                FileInfo => (ulong)((FileInfo)fileSystemInfo).Length,
                _ => 0,
            },
            User = SFTPUser.Root,
            Group = SFTPGroup.Root,
            Permissions = fileSystemInfo switch
            {
                DirectoryInfo => Enums.Permissions.DefaultDirectory,
                FileInfo => Enums.Permissions.DefaultFile,
                _ => Enums.Permissions.None,
            },
            LastAccessedTime = fileSystemInfo.LastAccessTimeUtc,
            LastModifiedTime = fileSystemInfo.LastWriteTimeUtc,
        };
    }

    /// <summary>
    /// Returns the file line that would be returned from the unix `ls -l` command.
    /// </summary>
    /// <param name="name">The file name itself.</param>
    public string GetLongFileName(string name)
    {
        const int HardLinksAmount = 1;
        string userName = User == null ? "???" : User.ToString();
        string groupName = Group == null ? "???" : Group.ToString();
        string lastModifiedTime = (LastModifiedTime ?? DateTimeOffset.UnixEpoch).ToString(
            "12:MMM dd HH:mm",
            CultureInfo.InvariantCulture
        );
        string permissionsString;
        if (Permissions == null)
        {
            permissionsString = "?";
        }
        else
        {
            Permissions permissions = Permissions.Value;
            permissionsString =
                (permissions.HasFlag(Enums.Permissions.Directory) ? "d" : "-")
                + AttrStr(
                    permissions.HasFlag(Enums.Permissions.UserRead),
                    permissions.HasFlag(Enums.Permissions.UserWrite),
                    permissions.HasFlag(Enums.Permissions.UserExecute)
                )
                + AttrStr(
                    permissions.HasFlag(Enums.Permissions.GroupRead),
                    permissions.HasFlag(Enums.Permissions.GroupWrite),
                    permissions.HasFlag(Enums.Permissions.GroupExecute)
                )
                + AttrStr(
                    permissions.HasFlag(Enums.Permissions.OtherRead),
                    permissions.HasFlag(Enums.Permissions.OtherWrite),
                    permissions.HasFlag(Enums.Permissions.OtherExecute)
                );
        }
        return $"{permissionsString} {HardLinksAmount, 3} {userName, -8} {groupName, -8} {FileSize ?? 0, 8} {lastModifiedTime} {name}".ToString(
            CultureInfo.InvariantCulture
        );
    }

    private static string AttrStr(bool read, bool write, bool execute) =>
        $"{(read ? "r" : "-")}{(write ? "w" : "-")}{(execute ? "x" : "-")}";
}
