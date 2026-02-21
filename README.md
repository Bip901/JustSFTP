# Just SFTP

[![JustSFTP.Protocol on NuGet](https://img.shields.io/nuget/v/JustSFTP.Protocol?label=JustSFTP.Protocol%20NuGet)](https://www.nuget.org/packages/JustSFTP.Protocol/)
[![JustSFTP.Server on NuGet](https://img.shields.io/nuget/v/JustSFTP.Server?label=JustSFTP.Server%20NuGet)](https://www.nuget.org/packages/JustSFTP.Server/)
[![JustSFTP.Client on NuGet](https://img.shields.io/nuget/v/JustSFTP.Client?label=JustSFTP.Client%20NuGet)](https://www.nuget.org/packages/JustSFTP.Client/)

This project implements the [V3 version of the SFTP protocol](https://datatracker.ietf.org/doc/html/draft-ietf-secsh-filexfer-02), and just it - you are free to use any transport/encryption layer (e.g. using a library like [Microsoft's DevTunnels SSH](https://github.com/microsoft/dev-tunnels-ssh)), or OpenSSH's daemon - see [JustSFTP.Host](./SFTPHost/README.md).


## Contents of this repo

* [SFTP Server](./SFTPServer/README.md)
    * [SFTP Host](./SFTPHost/README.md)
* [SFTP Client](./SFTPClient/README.md)
* [SFTP Protocol Shared Library](./SFTPProtocol/README.md)

## License

Licensed under MIT license. See [LICENSE](./LICENSE) for details.
