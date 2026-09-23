# SafeSpend LXC deployment

The GitHub Actions workflow creates a framework-dependent release ZIP when a
tag such as `v1.0.0` is pushed. It publishes both a versioned asset and
`SafeSpend-latest.zip`, each with a SHA-256 checksum. The LXC needs the .NET 10
ASP.NET Core runtime, `curl`, `unzip`, `logrotate`, and `systemd`.

The application data and Data Protection keys live in `/var/lib/safespend`.
That directory is outside the versioned release folders, so updating the
application does not reset the administrator account, Plaid connection, or
encrypted access-token data.

Application output is written to `/var/log/safespend/stdout.log` and
`/var/log/safespend/stderr.log`. The installer adds a logrotate policy that
rotates them daily or at 50 MB, retains 14 rotations, and compresses older
files.

## Initial installation

From the LXC, run the bootstrap installer:

```bash
curl -fsSL https://raw.githubusercontent.com/ComputerComa/SafeSpend/master/install.sh | sudo bash
```

It downloads the latest release, verifies its checksum, and installs the
service and updater. Then configure the LXC:

```bash
sudo editor /etc/safespend/update.env
sudo editor /etc/safespend/safespend.env
sudo chmod 600 /etc/safespend/*.env
sudo safespend-update
sudo systemctl status safespend
```

The same installer can be run from a checked-out repository with
`sudo ./install.sh`.

Set `AllowedHosts` to the public hostname used by the reverse proxy. The
reverse proxy must terminate HTTPS and forward `/api/plaid/webhook` to the
local application port. The configured `SafeSpend__PlaidWebhookUrl` must be
the public HTTPS URL, not the local `127.0.0.1` address.

For initial LAN-only setup, set `ASPNETCORE_URLS` to
`http://0.0.0.0:5080` and set `AllowedHosts` to the LXC address plus local
hosts, for example `192.168.1.50;localhost;127.0.0.1`. Restart the service,
then browse to `http://192.168.1.50:5080/Account/Setup`. Replace the temporary
LAN settings with the reverse-proxy hostname before exposing the application,
and set `SafeSpend__BehindProxy=true` only when that proxy is active.

The deploy scripts default to `ComputerComa/SafeSpend`; change
`SAFESPEND_REPOSITORY` if the repository is moved or forked.

## Updates and rollback

The normal update downloads the latest GitHub release, verifies its checksum,
extracts it into a new release directory, atomically switches
`/opt/safespend/current`, and restarts systemd:

```bash
sudo safespend-update
```

To install a specific tag:

```bash
sudo safespend-update v1.0.0
```

If the new service fails to start, the script restores the previous release
automatically. The last three release directories are retained by default.

## Creating a release

From a clean checkout:

```bash
git tag v1.0.0
git push origin v1.0.0
```

The tag workflow restores, tests, publishes, packages, and creates the GitHub
Release. It never packages user-secrets, SQLite data, or Plaid access tokens.
