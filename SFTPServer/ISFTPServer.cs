using System.Threading;
using System.Threading.Tasks;

namespace SFTP;

public interface ISFTPServer
{
    Task Run(CancellationToken cancellationToken = default);
}