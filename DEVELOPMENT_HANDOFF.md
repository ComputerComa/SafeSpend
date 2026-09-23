# SafeSpend Development Handoff

Updated: 2026-09-23

This document records the current implementation so work can continue from another development machine.

## Current application state

SafeSpend is an ASP.NET Core .NET 10 Razor Pages application using Going.Plaid 6.65.0. Plaid credentials are read from user-secrets. Plaid access tokens remain server-side and are never rendered or logged.

ASP.NET Core Identity is now enabled. Identity uses a separate ignored SQLite database at `SafeSpend.Web/App_Data/identity.db`, while the existing application data remains in `safespend.db`.

The current flow is:

1. `/Plaid/Connect` creates a Plaid Link token.
2. Plaid Link returns a public token to the server.
3. The server exchanges the public token for an access token and Item ID.
4. `/accounts/get` loads connected checking and savings accounts and their balances.
5. `/transactions/sync` performs cursor-based pagination and persists added, modified, and removed transactions.
6. The dashboard displays the connected account balance, persisted recent transactions, and the cash-flow forecast.
7. `/Setup` contains the Plaid connection entry point, paycheck schedule, and bill schedule forms.
8. If Identity has no users, `/Account/Setup` creates the first user and assigns the Administrator role. Once that user exists, setup redirects to login and there is no registration endpoint.
9. The Plaid access token is protected with ASP.NET Data Protection and stored in the user-keyed `PlaidConnections` table. Development keys are kept under ignored `App_Data` files; deployed keys are kept in the configured persistent data directory (normally `/var/lib/safespend`).
10. Disconnecting a bank calls Plaid Item removal, deletes the protected local connection, and deletes that Item's stored transactions and cursor.
11. `appsettings.Production.json` disables detailed errors and EF command logging, restricts allowed hosts to local addresses, and the application refuses Production startup unless Plaid is configured for Production.
12. Production uses `App_Data/Production` for its Identity database, application database, and Data Protection keys, keeping the existing Sandbox files under `App_Data` separate.
13. A hosted worker performs an initial sync, then periodic cursor-based transaction syncs (60 minutes by default). Webhook-triggered syncs are queued and deduplicated, while per-user sync coordination prevents cursor races with manual syncs.
14. `/api/plaid/webhook` verifies Plaid's `Plaid-Verification` ES256 signature and body hash using `/webhook_verification_key/get`. Transaction update webhooks queue a sync; Item recovery webhooks persist a non-secret `ActionRequired` status. Plaid Link uses update mode for an existing connection so the user can repair it.
15. `.github/workflows/release.yml` publishes a versioned application ZIP and a `SafeSpend-latest.zip` asset on `v*` tag pushes. The root `install.sh` is a curl-pipe bootstrap; `deploy/` contains the systemd unit, persistent-data configuration, checksum-verified updater, and automatic rollback.
16. Both SQLite databases now use checked-in EF Core migrations. On the first upgraded startup, a complete database created by the old `EnsureCreated` workflow is adopted as the initial migration without changing or deleting its data. Fresh databases are created by migrations, and later migrations are applied automatically before the web host starts.
17. Data Protection uses the stable application name `SafeSpend`. The updater records old release-directory discriminators and archives release-local key rings under persistent storage. Token recovery tries retained and archived key directories, then immediately re-encrypts a recovered token under the stable name. If the required key no longer exists, the Plaid connection page provides an explicit local reset and reconnect flow instead of crashing the dashboard or setup page.
18. Tagged release packages contain a `VERSION` file and embed the tag and commit in the assembly. The updater installs into tag-named directories, reports the resolved tag and commit, supports `--version` and non-secret `--diagnose` output, preserves customized systemd units by default, and rolls back a service that does not remain healthy.

## Important implementation locations

- `SafeSpend.Web/Pages/Plaid/Connect.cshtml` and `Connect.cshtml.cs`: Plaid connection UI, account display, and manual transaction sync action.
- `SafeSpend.Web/Services/Plaid/PlaidApi.cs`: Going.Plaid adapter for Link token creation, token exchange, accounts, and transaction sync.
- `SafeSpend.Web/Services/Plaid/PlaidLinkService.cs`: Plaid orchestration, cursor pagination, account filtering, and persistence coordination.
- `SafeSpend.Web/Services/Plaid/PlaidConnectionStore.cs`: User-keyed protected access-token storage and connection lifecycle.
- `SafeSpend.Web/Services/Identity/CurrentUserContext.cs`: Resolves the authenticated Identity user used to scope the Plaid connection.
- `SafeSpend.Web/Services/Plaid/SafeSpendDbContext.cs`: SQLite EF Core model for Plaid items, transactions, paycheck schedules, and bill schedules.
- `SafeSpend.Web/Services/Plaid/PlaidTransactionStore.cs`: Durable transaction and cursor storage.
- `SafeSpend.Web/Services/Plaid/PlaidTransactionSyncWorker.cs`: Periodic and queued background transaction sync.
- `SafeSpend.Web/Services/Plaid/PlaidWebhookVerifier.cs` and `PlaidWebhookService.cs`: Signed webhook verification, Item status handling, and sync queueing.
- `SafeSpend.Web/Services/Plaid/IPlaidConnectionStore.cs` and `PlaidConnectionStore.cs`: Protected, user-keyed Plaid connection persistence.
- `SafeSpend.Web/Data/Migrations/`: EF Core migrations and model snapshots for both the application and Identity contexts.
- `SafeSpend.Web/Data/LegacyDatabaseMigrationAdopter.cs`: Validates and baselines databases created by the previous `EnsureCreated` workflow.
- `SafeSpend.Web/Services/Plaid/SafeSpendDatabaseInitializer.cs` and `Services/Identity/IdentityDatabaseInitializer.cs`: Apply pending migrations at startup.
- `SafeSpend.Web/Services/Forecasting/`: Paycheck and bill schedule models, persistence, recurrence expansion, and forecast services.
- `SafeSpend.Web/Pages/Index.cshtml` and `Index.cshtml.cs`: Read-oriented dashboard, account-backed forecast, and recent transactions.
- `SafeSpend.Web/Pages/Setup.cshtml` and `Setup.cshtml.cs`: Plaid setup entry point, paycheck schedule, and bill schedule management.
- `SafeSpend.Web/Pages/Account/`: Login, one-time administrator setup, logout, and access-denied pages.
- `SafeSpend.Web/Services/Identity/`: Identity user, Identity database, setup service, and database initialization.
- `SafeSpend.Web/Pages/Shared/_Layout.cshtml`: Authenticated user indicator, sign-out form, and embedded application version.
- `.github/workflows/release.yml`: Release build, test, publish, ZIP, checksum, and GitHub Release workflow.
- `deploy/`: LXC installation, systemd, persistent data, and Arr-style update scripts.

The local SQLite database is `SafeSpend.Web/App_Data/safespend.db`. The `App_Data` directory is ignored by Git. It may be empty on a fresh machine and will be created on application startup.

## Tests and validation

The current test suite contains 22 passing tests covering:

- Plaid account filtering and mapping.
- Multi-page transaction sync and cursor persistence.
- Restoration of a persisted Plaid cursor.
- Durable transaction insert, update, removal, and cursor storage.
- Protected Plaid connection persistence and removal.
- Plaid Item removal and local transaction cleanup.
- Bill recurrence expansion.
- Paycheck and bill schedule persistence.
- First-user administrator creation and registration shutdown after setup.
- Plaid webhook signature verification, transaction sync queueing, and Item recovery status.
- Fresh EF Core migration-based database creation, safe legacy database adoption, data preservation, and rejection of incomplete legacy schemas.
- Recovery and re-encryption of a Plaid token protected with a legacy release-path Data Protection discriminator and a separate legacy key ring.
- Discovery of retained and archived legacy Data Protection key directories.
- Safe removal of an unrecoverable local Plaid connection without attempting a remote Plaid call with an unavailable token.

Validated commands:

```text
dotnet build SafeSpend.slnx                         # passes
dotnet test SafeSpend.slnx                          # 22 passed
git diff --check                                    # passes
```

## Continuing on the home PC

1. Check out or copy this repository, then configure the .NET 10 SDK.
2. Restore the Plaid user-secrets on the home PC. Do not put credentials, access tokens, or the local SQLite database into Git. For a production Item, configure the Production environment and Production secret:

   ```text
   dotnet user-secrets set "Plaid:Environment" "Production"
   dotnet user-secrets set "Plaid:ClientId" "<Plaid client id>"
   dotnet user-secrets set "Plaid:Secret" "<Plaid production secret>"
   ```

   The application refuses to start with `ASPNETCORE_ENVIRONMENT=Production` unless `Plaid:Environment=Production`. Production uses a separate `App_Data/Production` directory and will require a first administrator setup there. On Windows, Data Protection keys are protected with DPAPI. On Linux and macOS, protect the OS account and disk containing `App_Data`; the framework warns that those key files are not encrypted by an OS provider.
3. Run:

   ```text
   dotnet restore SafeSpend.slnx
   dotnet build SafeSpend.Web/SafeSpend.Web.csproj
   dotnet test SafeSpend.slnx
   dotnet run --project SafeSpend.Web/SafeSpend.Web.csproj
   ```

4. For production webhook delivery, configure the public HTTPS callback URL (for example `https://app.example.com/api/plaid/webhook`) without committing it:

   ```text
   dotnet user-secrets set "SafeSpend:PlaidWebhookUrl" "https://app.example.com/api/plaid/webhook"
   ```

   Production startup rejects a missing or non-HTTPS webhook URL. A local-only app can still use the periodic worker; Plaid cannot deliver production webhooks to an address that is not publicly reachable over HTTPS.
5. Exercise the connection flow at `/Plaid/Connect`: connect an Item, verify checking/savings balances, click sync, and confirm recent transactions appear on the dashboard. After connecting, the worker will sync on its schedule, and Plaid transaction webhooks will queue an earlier sync when configured.
6. Create a paycheck schedule and at least one recurring bill, then verify that the forecast uses the connected checking/savings balance and expands upcoming bill occurrences.
7. On a fresh Identity database, open `/Account/Setup`, create the administrator, verify redirect to the dashboard, sign out, and sign back in. Confirm that `/Account/Setup` no longer allows another account and that `/Account/Register` is unavailable.
8. For an LXC deployment, follow [`deploy/README.md`](deploy/README.md), configure `/etc/safespend/safespend.env`, and test `sudo safespend-update` with a tagged GitHub Release.

## Planned next steps

### 1. Add bill reminders via SMTP

Add reminder lead times to bill schedules, persist sent-reminder records to avoid duplicates, and send due reminders from a hosted worker. Store SMTP credentials only in user-secrets or `/etc/safespend/safespend.env`.

### 2. Use transaction history for forecasting improvements

The current forecast correctly starts from the live Plaid account balance and applies configured paycheck and bill schedules. The next forecasting improvement is to categorize persisted transactions and use transaction history to suggest recurring bills or validate configured schedules. Avoid subtracting synced transactions again from a current account balance, since that would double-count them.

### 3. Connect all data ownership to Identity

Plaid connections are now user-keyed, but schedules and transaction rows still rely on the single-user application boundary. Add user ownership keys to those records before enabling additional users or administrators.

### 4. Add production safeguards and UI polish

Add authorization and per-user ownership before supporting multiple users, improve error and sync status messaging, add pagination/filtering for transaction history, and add tests for Plaid API error responses and duplicate sync events.

## EF Core schema changes

After changing either EF model, create and review a migration from the repository root:

```text
dotnet tool restore
dotnet ef migrations add <MigrationName> --project SafeSpend.Web/SafeSpend.Web.csproj --startup-project SafeSpend.Web/SafeSpend.Web.csproj --context SafeSpendDbContext --output-dir Data/Migrations/SafeSpend
dotnet ef migrations add <MigrationName> --project SafeSpend.Web/SafeSpend.Web.csproj --startup-project SafeSpend.Web/SafeSpend.Web.csproj --context SafeSpendIdentityDbContext --output-dir Data/Migrations/Identity
```

Run only the command for the context that changed. Do not use `EnsureCreated`, manually edit a deployed SQLite schema, or remove an applied migration. Startup applies pending migrations before accepting requests. Back up `/var/lib/safespend` before deploying releases that contain schema changes because rolling application files back does not reverse a database migration.

## Working rules

- Never log, display, commit, or include in diagnostics any Plaid access token, public token, client secret, or user-secret value.
- Keep `SafeSpend.Web/App_Data/` ignored.
- Preserve the existing Plaid Link and Going.Plaid conventions when extending the integration.
