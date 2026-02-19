namespace JustSFTP.Client;

/// <summary>
/// Trace event IDs for the entire <see cref="JustSFTP.Client"/> library.
/// </summary>
public static class TraceEventIds
{
    public const int SFTPClient_InitSuccess = 1;
    public const int SFTPClient_SendingRequest = 2;
    public const int SFTPClient_DroppingResponse = 3;
    public const int SFTPClient_ReceivedResponse = 4;
}
