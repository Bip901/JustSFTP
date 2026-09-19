using System;

namespace JustSFTP.Protocol.Enums;

/// <summary>
/// Represents a full POSIX file mode (<c>st_mode</c>).
/// See <c>st_mode</c> in <c>man inode</c>.
/// </summary>
[Flags]
public enum PosixFileMode : uint
{
    /// <summary>
    /// Zero value.
    /// </summary>
    None = 0,

    #region Permissions (0000777 octal)
    /// <summary>
    /// S_IXOTH - others (not in group) have execute permission.
    /// </summary>
    OtherExecute = 0x01,

    /// <summary>
    /// S_IWOTH - others (not in group) have write permission.
    /// </summary>
    OtherWrite = 0x02,

    /// <summary>
    /// S_IROTH - others (not in group) have read permission.
    /// </summary>
    OtherRead = 0x04,

    /// <summary>
    /// S_IXGRP - group has execute permission.
    /// </summary>
    GroupExecute = 0x08,

    /// <summary>
    /// S_IWGRP - group has write permission.
    /// </summary>
    GroupWrite = 0x10,

    /// <summary>
    /// S_IRGRP - group has read permission.
    /// </summary>
    GroupRead = 0x20,

    /// <summary>
    /// S_IXUSR - owner has execute permission.
    /// </summary>
    UserExecute = 0x40,

    /// <summary>
    /// S_IWUSR - owner has write permission.
    /// </summary>
    UserWrite = 0x80,

    /// <summary>
    /// S_IRUSR - owner has read permission.
    /// </summary>
    UserRead = 0x100,
    #endregion

    #region Special bits (0007000 octal)
    /// <summary>
    /// S_ISVTX - sticky bit.
    /// A file in that directory can be renamed or deleted only by the owner of the file, by the owner of the directory, and by a privileged process.
    /// </summary>
    Sticky = 0x200,

    /// <summary>
    /// S_ISGID - set-group-ID bit.
    /// </summary>
    SetGID = 0x400,

    /// <summary>
    /// S_ISUID - set-user-ID bit.
    /// </summary>
    SetUID = 0x800,
    #endregion

    #region File type constants (0170000 octal)
    /// <summary>
    /// S_IFMT - bit mask for the file type bit field.
    /// </summary>
    FileTypeMask = 0xF000,

    /// <summary>
    /// S_IFIFO - FIFO.
    /// </summary>
    FIFO = 0x1000,

    /// <summary>
    /// S_IFCHR - character device.
    /// </summary>
    CharacterDevice = 0x2000,

    /// <summary>
    /// S_IFDIR - directory.
    /// </summary>
    Directory = 0x4000,

    /// <summary>
    /// S_IFBLK - block device.
    /// </summary>
    BlockDevice = 0x6000,

    /// <summary>
    /// S_IFREG - regular file.
    /// </summary>
    RegularFile = 0x8000,

    /// <summary>
    /// S_IFLNK - symbolic link.
    /// </summary>
    SymbolicLink = 0xA000,

    /// <summary>
    /// S_IFSOCK - socket.
    /// </summary>
    Socket = 0xC000,
    #endregion

    /// <summary>
    /// A regular file with permissions rw-r--r--.
    /// </summary>
    DefaultFile = UserRead | UserWrite | GroupRead | OtherRead | RegularFile,

    /// <summary>
    /// A regular directory with permissions rwxr-xr-x.
    /// </summary>
    DefaultDirectory =
        UserRead | UserWrite | UserExecute | GroupRead | GroupExecute | OtherRead | OtherExecute | Directory,
}
