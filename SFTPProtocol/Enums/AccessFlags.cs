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
    /// Returns the <see cref="FileMode"/> flags that best represent the given access flags.
    /// </summary>
    public static FileMode ToFileMode(this AccessFlags flags)
    {
        FileMode filemode = FileMode.Open;
        if (flags.HasFlag(AccessFlags.Append))
        {
            filemode = FileMode.Append;
        }
        else if (flags.HasFlag(AccessFlags.Create))
        {
            filemode = FileMode.OpenOrCreate;
        }
        else if (flags.HasFlag(AccessFlags.Truncate))
        {
            filemode = FileMode.CreateNew;
        }
        else if (flags.HasFlag(AccessFlags.Exclusive))
        {
            throw new NotImplementedException();
        }
        else if (flags.HasFlag(AccessFlags.Text))
        {
            throw new NotImplementedException();
        }
        return filemode;
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
