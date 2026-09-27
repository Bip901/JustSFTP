namespace JustSFTP.Host;

public record SFTPServerOptions()
{
    public string Root { get; init; } = string.Empty;
}
