using System;
using JustSFTP.Server.Enums;

namespace JustSFTP.Server.Models;

public record SFTPData(byte[] Data)
{
    public Status Status { get; init; } = Status.Ok;
    public static readonly SFTPData EOF = new(Array.Empty<byte>()) { Status = Status.EndOfFile };
}