using System;

namespace JustSFTP.Server;

internal class SFTPHandle(byte[] Handle) : IEquatable<SFTPHandle>
{
    public byte[] Handle { get; } = Handle;

    public bool Equals(SFTPHandle? other)
    {
        return other != null && Handle.AsSpan().SequenceEqual(other.Handle);
    }

    public override bool Equals(object? obj)
    {
        if (obj is not SFTPHandle other)
        {
            return false;
        }
        return Equals(other);
    }

    public override int GetHashCode()
    {
        if (Handle.Length == 0)
        {
            return 0;
        }
        if (Handle.Length < sizeof(int))
        {
            return Handle[0];
        }
        return BitConverter.ToInt32(Handle);
    }
}
