# Just SFTP Server

This library implements the [V3 version of the SFTP protocol](https://datatracker.ietf.org/doc/html/draft-ietf-secsh-filexfer-02), and just it - you are free to use any transport/encryption layer (e.g. using a library like [Microsoft's DevTunnels SSH](https://github.com/microsoft/dev-tunnels-ssh)), or OpenSSH's daemon - see [JustSFTP.Host](./SFTPHost/README.md).

The `SFTPServer` class takes 2 [`Streams`](https://docs.microsoft.com/en-us/dotnet/api/system.io.stream) and optionally an `ISFTPHandler` on which the SFTP commands will be invoked. This package comes with a `DefaultSFTPHandler` which provides basic I/O on the hosts's filesystem (based on a rootdirectory), but it should be pretty easy to implement your own `ISFTPHandler` so you can, for example, implement a virtual filesystem.


## Implementing an `ISFTPHandler`

Implementing an `ISFTPHandler` should be pretty straightforward, simply implement the `ISFTPHandler` interface.
Throw a `HandlerException` to reflect a status code back tothe client.
Exceptions that are not inherited from the `HandlerException` will be returned to the client as [`Failure` (`SSH_FX_FAILURE`)](https://datatracker.ietf.org/doc/html/draft-ietf-secsh-filexfer-02#page-20).

For an example implementation you can have a look at the `DefaultSFTPHandler`.

## Known issues and limitations

* This library only implements SFTP v3 (and presumably v2 although untested); higher versions are not supported. Clients connecting with a higher version will be negotiated down to v3.

* The [`SymLink`](https://datatracker.ietf.org/doc/html/draft-ietf-secsh-filexfer-02#section-6.10) command has been implemented with the `linkpath` and `targetpath` swapped; We may or may not interpret the RFC incorrectly (Update: [We didn't](https://datatracker.ietf.org/doc/html/draft-ietf-secsh-filexfer-09#:~:text=many%0A%20%20%20%20%20%20implementation%20implemented%20SYMLINK%20with%20the%20arguments%20reversed)) or the clients which were used to test the `SymLink` command (WinSCP, Cyberduck and the 'native' sftp commandline executable) had the arguments swapped. The `SymLink` and `ReadLink` methods are only available from .Net 6.0 upwards.

* The `DefaultSFTPHandler` is not well tested nor fully featured, and serves mostly as an example implementation of `ISFTPHandler`.
