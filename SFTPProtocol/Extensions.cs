namespace JustSFTP.Protocol;

/// <summary>
/// Lists names of extensions supported by this library.
/// </summary>
public static class Extensions
{
    /// <summary>
    /// The extension name of the posix-rename extension. See <see cref="Models.Requests.Extended.SFTPPosixRenameRequest"/>.
    /// </summary>
    public const string POSIX_RENAME = "posix-rename@openssh.com";

    /// <summary>
    /// The extension name of the open-dir-eager extension. See <see cref="Models.Requests.Extended.SFTPOpenDirEagerRequest"/>.
    /// </summary>
    public const string OPEN_DIR_EAGER = "open-dir-eager@justsftp";
}
