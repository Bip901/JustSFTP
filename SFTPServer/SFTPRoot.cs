using System;
using System.IO;
using JustSFTP.Protocol;
using JustSFTP.Protocol.Enums;

namespace JustSFTP.Server;

/// <summary>
/// Maps the paths of an SFTP session onto a root directory in the local filesystem, so that a client
/// can never address anything outside of that root.
/// </summary>
/// <remarks>
/// This only guards the paths that clients send. A symbolic link, junction or mount point that already
/// exists inside the root is still followed by the operating system.
/// </remarks>
public sealed class SFTPRoot
{
    private readonly string rootPath;

    /// <summary>
    /// Creates a root for the given directory.
    /// </summary>
    /// <param name="rootPath">The path of the root directory to serve.</param>
    /// <exception cref="ArgumentException">If the path is null or empty.</exception>
    public SFTPRoot(string rootPath)
    {
        if (string.IsNullOrEmpty(rootPath))
        {
            throw new ArgumentException("The root path must not be empty.", nameof(rootPath));
        }
        this.rootPath = Path.GetFullPath(rootPath);
    }

    /// <summary>
    /// Resolves a client-supplied SFTP path to a physical path inside this root.
    /// </summary>
    /// <param name="sftpPath">The path as sent by the client.</param>
    /// <exception cref="HandlerException">
    /// <see cref="Status.PermissionDenied"/> if the path is not inside the root.
    /// </exception>
    public string GetPhysicalPath(string sftpPath)
    {
        string fullPath = Path.GetFullPath(Path.Join(rootPath, sftpPath));
        // A path outside the root either keeps a drive or UNC root of its own, or reaches outside
        // through a leading '..' segment.
        string relativePath = Path.GetRelativePath(rootPath, fullPath);
        if (
            Path.IsPathRooted(relativePath)
            || (
                relativePath.StartsWith("..", StringComparison.Ordinal)
                && (relativePath.Length == 2 || relativePath[2] == Path.DirectorySeparatorChar)
            )
        )
        {
            throw new HandlerException(Status.PermissionDenied, "The path is outside of the served root.");
        }
        return fullPath;
    }

    /// <summary>
    /// Normalizes a client-supplied SFTP path, e.g. '/a/./b/../c' becomes '/a/c'.
    /// </summary>
    /// <exception cref="HandlerException">
    /// <see cref="Status.PermissionDenied"/> if the path is not inside the root.
    /// </exception>
    public string Normalize(string sftpPath)
    {
        string relativePath = Path.TrimEndingDirectorySeparator(
            Path.GetRelativePath(rootPath, GetPhysicalPath(sftpPath))
        );
        if (relativePath == ".")
        {
            return "/";
        }
        return "/" + relativePath.Replace(Path.DirectorySeparatorChar, '/');
    }
}
