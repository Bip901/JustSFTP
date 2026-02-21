using System;
using System.IO;

namespace JustSFTP.Protocol.Enums;

/// <summary>
/// pflags
/// </summary>
[Flags]
public enum AccessFlags : uint
{
    /// <summary>
    /// SSH_FXF_READ
    /// </summary>
    Read = 0x01,

    /// <summary>
    /// SSH_FXF_WRITE
    /// </summary>
    Write = 0x02,

    /// <summary>
    /// SSH_FXF_APPEND
    /// </summary>
    Append = 0x04,

    /// <summary>
    /// SSH_FXF_CREAT
    /// </summary>
    Create = 0x08,

    /// <summary>
    /// SSH_FXF_TRUNC
    /// </summary>
    Truncate = 0x10,

    /// <summary>
    /// SSH_FXF_EXCL
    /// </summary>
    Exclusive = 0x20,

    /// <summary>
    /// SSH_FXF_TEXT
    /// </summary>
    Text = 0x40,
}

/// <summary>
/// Helper methods for <see cref="AccessFlags"/>.
/// </summary>
public static class AccessFlagsExtensionMethods
{
    /// <summary>
    /// Returns the <see cref="AccessFlags"/> flags that best represent the given file mode and access.
    /// </summary>
    public static AccessFlags ToAccessFlags(this FileMode fileMode, FileAccess fileAccess)
    {
        AccessFlags flags = 0;
        if (fileAccess.HasFlag(FileAccess.Read))
        {
            flags |= AccessFlags.Read;
        }
        if (fileAccess.HasFlag(FileAccess.Write))
        {
            flags |= AccessFlags.Write;
        }
        switch (fileMode)
        {
            case FileMode.CreateNew:
                flags |= AccessFlags.Create | AccessFlags.Exclusive;
                break;
            case FileMode.Create:
                flags |= AccessFlags.Create | AccessFlags.Truncate;
                break;
            case FileMode.Open:
                break;
            case FileMode.OpenOrCreate:
                flags |= AccessFlags.Create;
                break;
            case FileMode.Truncate:
                flags |= AccessFlags.Truncate;
                break;
            case FileMode.Append:
                flags |= AccessFlags.Append;
                break;
            default:
                throw new NotSupportedException();
        }
        return flags;
    }

    /// <summary>
    /// Returns the <see cref="FileMode"/> flags that best represent the given access flags.
    /// </summary>
    public static FileMode ToFileMode(this AccessFlags flags)
    {
        if (flags.HasFlag(AccessFlags.Append))
        {
            return FileMode.Append;
        }
        if (flags.HasFlag(AccessFlags.Create))
        {
            if (flags.HasFlag(AccessFlags.Exclusive))
            {
                return FileMode.CreateNew;
            }
            else if (flags.HasFlag(AccessFlags.Truncate))
            {
                return FileMode.Create;
            }
            else
            {
                return FileMode.OpenOrCreate;
            }
        }
        else if (flags.HasFlag(AccessFlags.Truncate))
        {
            return FileMode.Truncate;
        }
        return FileMode.Open;
    }

    /// <summary>
    /// Returns the <see cref="FileAccess"/> flags that best represent the given access flags.
    /// </summary>
    public static FileAccess ToFileAccess(this AccessFlags flags)
    {
        FileAccess fileAccess = FileAccess.Read;
        if (flags.HasFlag(AccessFlags.Write))
        {
            fileAccess |= FileAccess.Write;
        }
        return fileAccess;
    }
}
