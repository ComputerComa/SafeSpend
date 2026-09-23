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
9. The Plaid access token is protected with ASP.NET Data Protection and stored in the user-keyed `PlaidConnections` table. Data Protection keys are kept under ignored `App_Data` files so the connection survives application restarts on the same machine.
10. Disconnecting a bank calls Plaid Item removal, deletes the protected local connection, and deletes that Item's stored transactions and cursor.
11. `appsettings.Production.json` disables detailed errors and EF command logging, restricts allowed hosts to local addresses, and the application refuses Production startup unless Plaid is configured for Production.
12. Production uses `App_Data/Production` for its Identity database, application database, and Data Protection keys, keeping the existing Sandbox files under `App_Data` separate.
13. A hosted worker performs an initial sync, then periodic cursor-based transaction syncs (60 minutes by default). Webhook-triggered syncs are queued and deduplicated, while per-user sync coordination prevents cursor races with manual syncs.
14. `/api/plaid/webhook` verifies Plaid's `Plaid-Verification` ES256 signature and body hash using `/webhook_verification_key/get`. Transaction update webhooks queue a sync; Item recovery webhooks persist a non-secret `ActionRequired` status. Plaid Link uses update mode for an existing connection so the user can repair it.
15. `.github/workflows/release.yml` publishes a versioned application ZIP and a `SafeSpend-latest.zip` asset on `v*` tag pushes. The `deploy/` directory contains the LXC installer, systemd unit, persistent-data configuration, checksum-verified updater, and automatic rollback.

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
- `SafeSpend.Web/Services/Plaid/SafeSpendDatabaseInitializer.cs`: Creates the SQLite database and adds schedule tables for existing local databases.
- `SafeSpend.Web/Services/Forecasting/`: Paycheck and bill schedule models, persistence, recurrence expansion, and forecast services.
- `SafeSpend.Web/Pages/Index.cshtml` and `Index.cshtml.cs`: Read-oriented dashboard, account-backed forecast, and recent transactions.
- `SafeSpend.Web/Pages/Setup.cshtml` and `Setup.cshtml.cs`: Plaid setup entry point, paycheck schedule, and bill schedule management.
- `SafeSpend.Web/Pages/Account/`: Login, one-time administrator setup, logout, and access-denied pages.
- `SafeSpend.Web/Services/Identity/`: Identity user, Identity database, setup service, and database initialization.
- `SafeSpend.Web/Pages/Shared/_Layout.cshtml`: Authenticated user indicator and sign-out form.
- `.github/workflows/release.yml`: Release build, test, publish, ZIP, checksum, and GitHub Release workflow.
- `deploy/`: LXC installation, systemd, persistent data, and Arr-style update scripts.

The local SQLite database is `SafeSpend.Web/App_Data/safespend.db`. The `App_Data` directory is ignored by Git. It may be empty on a fresh machine and will be created on application startup.

## Tests and validation

The current test suite contains 16 passing tests covering:

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

Validated commands:

```text
dotnet build SafeSpend.Web/SafeSpend.Web.csproj     # passes
dotnet test SafeSpend.slnx                          # 16 passed
git diff --check                                    # passes
```

`dotnet build SafeSpend.slnx` was also run as requested. In the current environment it exits during restore before project compilation because the installed SDK is missing `Microsoft.NET.SDK.WorkloadAutoImportPropsLocator` and `Microsoft.NET.SDK.WorkloadManifestTargetsLocator`. The individual web project build and the solution test command both compile successfully.

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
5. Exercise the connection flow at `/Plaid/Connect`: connect an Item, verify checking/savings balances, click sync, and confirm recent transactions appear on the dashboard. The first connection after this change must be linked again because older versions only kept the access token in memory. After connecting, the worker will sync on its schedule, and Plaid transaction webhooks will queue an earlier sync when configured.
6. Create a paycheck schedule and at least one recurring bill, then verify that the forecast uses the connected checking/savings balance and expands upcoming bill occurrences.
7. On a fresh Identity database, open `/Account/Setup`, create the administrator, verify redirect to the dashboard, sign out, and sign back in. Confirm that `/Account/Setup` no longer allows another account and that `/Account/Register` is unavailable.
8. For an LXC deployment, follow [`deploy/README.md`](deploy/README.md), configure `/etc/safespend/safespend.env`, and test `sudo safespend-update` with a tagged GitHub Release.

## Planned next steps

### 1. Add migrations

Replace the startup table-creation compatibility code with a real EF Core migration workflow once the schema settles. Keep the local `App_Data` database out of source control.

### 2. Use transaction history for forecasting improvements

The current forecast correctly starts from the live Plaid account balance and applies configured paycheck and bill schedules. The next forecasting improvement is to categorize persisted transactions and use transaction history to suggest recurring bills or validate configured schedules. Avoid subtracting synced transactions again from a current account balance, since that would double-count them.

### 3. Connect all data ownership to Identity

Plaid connections are now user-keyed, but schedules and transaction rows still rely on the single-user application boundary. Add user ownership keys to those records before enabling additional users or administrators.

### 4. Add production safeguards and UI polish

Add authorization and per-user ownership before supporting multiple users, improve error and sync status messaging, add pagination/filtering for transaction history, and add tests for Plaid API error responses and duplicate sync events.

## Working rules

- Never log, display, commit, or include in diagnostics any Plaid access token, public token, client secret, or user-secret value.
- Keep `SafeSpend.Web/App_Data/` ignored.
- Preserve the existing Plaid Link and Going.Plaid conventions when extending the integration.
