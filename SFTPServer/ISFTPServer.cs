using System.Threading;
using System.Threading.Tasks;

namespace JustSFTP.Server;

public interface ISFTPServer
{
    Task Run(CancellationToken cancellationToken = default);
}