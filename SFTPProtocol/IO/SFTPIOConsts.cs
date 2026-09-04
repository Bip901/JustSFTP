using System.Text;
using JustSFTP.Protocol.Models.Responses;

namespace JustSFTP.Protocol.IO;

/// <summary>
/// Constants related to the SFTP protocol's IO.
/// </summary>
public static class SFTPIOConsts
{
    /// <summary>
    /// The SFTP v3 spec actually only defines the string encoding for the <see cref="SFTPStatus.ErrorMessage"/> field (UTF-8).
    /// All other strings are undefined, thus we have to follow the de-facto standard.
    /// OpenSSH is the most common SFTP server and sends strings as raw binary data.
    /// Since it usually runs on Linux, which usually uses UTF-8 (no BOM), it is the de-facto standard.
    /// </summary>
    public static readonly Encoding StringEncoding = new UTF8Encoding(false);

    /// <summary>
    /// Maximum packet that we are willing to accept.
    /// Value mirrors OpenSSH's SFTP_MAX_MSG_LENGTH.
    /// </summary>
    public const int MaxMessageLength = 256 * 1024;
}
