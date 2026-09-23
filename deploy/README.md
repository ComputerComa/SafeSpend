# SafeSpend LXC deployment

The GitHub Actions workflow creates a framework-dependent release ZIP when a
tag such as `v1.0.0` is pushed. It publishes both a versioned asset and
`SafeSpend-latest.zip`, each with a SHA-256 checksum. The LXC needs the .NET 10
ASP.NET Core runtime, `curl`, `unzip`, `logrotate`, and `systemd`.

Every release ZIP contains a `VERSION` file with the exact tag, commit SHA,
and UTC build time. The same tag and commit are embedded in the application
assembly and shown in the page footer. The updater installs releases into
tag-named directories such as `/opt/safespend/releases/v1.0.4` and reports the
resolved tag and commit even when invoked with `latest`.

The application data and Data Protection keys live in `/var/lib/safespend`.
That directory is outside the versioned release folders, so updating the
application does not reset the administrator account, Plaid connection, or
encrypted access-token data.

Before pruning old releases, the updater copies any release-local Data
Protection key rings into
`/var/lib/safespend/legacy-data-protection-keys`. SafeSpend searches those
archived key directories when recovering connections created by older builds.
Key contents are never included in updater output.

The application and Identity SQLite schemas are managed by EF Core migrations.
Pending migrations run during startup before the service accepts requests. The
first release using migrations safely adopts a complete database created by an
older SafeSpend release. Back up `/var/lib/safespend` before an update that
contains schema changes; switching the application symlink back does not undo
an applied database migration.

SafeSpend uses a stable ASP.NET Data Protection application name so encrypted
Plaid access tokens remain readable after the release symlink changes. During
an upgrade from an older release, the updater records retained release paths
in `/var/lib/safespend/legacy-data-protection-applications`. If an access token
uses one of those older path-based names, SafeSpend unlocks it and immediately
re-encrypts it with the stable name. Keep `SAFESPEND_DATA_DIRECTORY` in
`/etc/safespend/update.env` aligned with `SafeSpend__DataDirectory` in the
service environment.

If the key that encrypted an existing Plaid token has already been deleted,
the token cannot be decrypted. The dashboard and setup pages show a recovery
message instead of failing. The Plaid connection page then offers an explicit
local reset that removes the unreadable connection and its imported
transactions before reconnecting. It cannot remove the old remote Plaid Item
because doing so requires the unavailable token.

If upgrading from a release whose updater predates this behavior, run the
bootstrap command once after publishing the fixed release. This refreshes the
host-level updater as well as the application:

```bash
curl -fsSL https://raw.githubusercontent.com/ComputerComa/SafeSpend/master/install.sh | sudo bash
```

Newer updater versions replace `/usr/local/sbin/safespend-update` from each
verified release package automatically.

Existing systemd service files are preserved during installation and updates,
so host-specific changes such as the service account are not lost. Set
`SAFESPEND_REPLACE_SERVICE=true` in `/etc/safespend/update.env` only when you
intentionally want to restore the packaged service definition. Prefer a
systemd drop-in under `/etc/systemd/system/safespend.service.d/` for local
overrides because drop-ins remain separate from the packaged unit.

`SAFESPEND_USER`, `SAFESPEND_GROUP`, and `SAFESPEND_DATA_DIRECTORY` in
`/etc/safespend/update.env` must match the effective systemd service account
and `SafeSpend__DataDirectory`. The installer reconciles ownership throughout
that dedicated data directory so SQLite database, WAL, and Data Protection key
files remain writable after changing the service account.

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

To inspect the exact installed version without contacting GitHub:

```bash
sudo safespend-update --version
```

For non-secret deployment diagnostics, including the effective service account,
service state, release path, data directory, and Data Protection key-file
locations:

```bash
sudo safespend-update --diagnose
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
