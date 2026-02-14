using System;
using JustSFTP.Protocol.Enums;

namespace JustSFTP.Protocol.Models;

public record SFTPData(byte[] Data)
{
    public Status Status { get; init; } = Status.Ok;
    public static readonly SFTPData EOF = new(Array.Empty<byte>()) { Status = Status.EndOfFile };
}