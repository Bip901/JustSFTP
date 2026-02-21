# JustSFTP Host

This host is intended to be hosted in an SSH daemon; this can be done by editting the `sshd_config` file (usually in `/etc/ssh/` or, for Windows, in `%PROGRAMDATA%\ssh`) and pointing it to your own host executable.

In `sshd_config` you'll find the `Subsystem` entry which can be pointed to any executable:
```
# override default of no subsystems
#Subsystem	sftp	sftp-server.exe
#Subsystem	sftp	internal-sftp
Subsystem	sftp	/path/to/your/sftphost.exe
```

Then, whenever an SSH client connects and requests the "sftp" subsystem, `sshd` will launch the configured executable under the connected user's account. All communication is then done over `stdin` and `stdout`.
