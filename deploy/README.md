# SafeSpend LXC deployment

The GitHub Actions workflow creates a framework-dependent release ZIP when a
tag such as `v1.0.0` is pushed. It publishes both a versioned asset and
`SafeSpend-latest.zip`, each with a SHA-256 checksum. The LXC needs the .NET 10
ASP.NET Core runtime, `curl`, `unzip`, and `systemd`.

The application data and Data Protection keys live in `/var/lib/safespend`.
That directory is outside the versioned release folders, so updating the
application does not reset the administrator account, Plaid connection, or
encrypted access-token data.

## Initial installation

Copy this `deploy` directory to the LXC, then run:

```bash
sudo ./install-safespend.sh
sudo editor /etc/safespend/update.env
sudo editor /etc/safespend/safespend.env
sudo chmod 600 /etc/safespend/*.env
sudo safespend-update
sudo systemctl status safespend
```

Set `AllowedHosts` to the public hostname used by the reverse proxy. The
reverse proxy must terminate HTTPS and forward `/api/plaid/webhook` to the
local application port. The configured `SafeSpend__PlaidWebhookUrl` must be
the public HTTPS URL, not the local `127.0.0.1` address.

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
