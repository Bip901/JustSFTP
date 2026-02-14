using System.Threading.Tasks;
using JustSFTP.Client;

namespace JustSFTP.Tests;

public class TestEndToEnd
{
    [Fact]
    public async Task TestInit()
    {
        await using DummyServer dummyServer = DummyServer.Run();
        using SFTPClient client = new(dummyServer.ClientReadStream, dummyServer.ClientWriteStream);
        uint negotiatedVersion = await client.InitAsync();
        Assert.Equal(3u, negotiatedVersion);
    }
}
