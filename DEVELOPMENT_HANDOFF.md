# SafeSpend Development Handoff

Updated: 2026-09-22

This document records the current implementation so work can continue from another development machine.

## Current application state

SafeSpend is an ASP.NET Core .NET 10 Razor Pages application using Going.Plaid 6.65.0. Plaid Sandbox credentials are read from user-secrets. Plaid access tokens remain server-side and are never rendered or logged.

The current flow is:

1. `/Plaid/Connect` creates a Plaid Link token.
2. Plaid Link returns a public token to the server.
3. The server exchanges the public token for an access token and Item ID.
4. `/accounts/get` loads connected checking and savings accounts and their balances.
5. `/transactions/sync` performs cursor-based pagination and persists added, modified, and removed transactions.
6. The dashboard displays the connected account balance, persisted recent transactions, paycheck schedule, bill schedules, and the cash-flow forecast.

## Important implementation locations

- `SafeSpend.Web/Pages/Plaid/Connect.cshtml` and `Connect.cshtml.cs`: Plaid connection UI, account display, and manual transaction sync action.
- `SafeSpend.Web/Services/Plaid/PlaidApi.cs`: Going.Plaid adapter for Link token creation, token exchange, accounts, and transaction sync.
- `SafeSpend.Web/Services/Plaid/PlaidLinkService.cs`: Plaid orchestration, cursor pagination, account filtering, and persistence coordination.
- `SafeSpend.Web/Services/Plaid/PlaidConnectionState.cs`: In-memory server-side connection state. It stores the access token privately and does not expose it to Razor or the browser.
- `SafeSpend.Web/Services/Plaid/SafeSpendDbContext.cs`: SQLite EF Core model for Plaid items, transactions, paycheck schedules, and bill schedules.
- `SafeSpend.Web/Services/Plaid/PlaidTransactionStore.cs`: Durable transaction and cursor storage.
- `SafeSpend.Web/Services/Plaid/SafeSpendDatabaseInitializer.cs`: Creates the SQLite database and adds schedule tables for existing local databases.
- `SafeSpend.Web/Services/Forecasting/`: Paycheck and bill schedule models, persistence, recurrence expansion, and forecast services.
- `SafeSpend.Web/Pages/Index.cshtml` and `Index.cshtml.cs`: Dashboard, schedule forms, account-backed forecast, and recent transactions.

The local SQLite database is `SafeSpend.Web/App_Data/safespend.db`. The `App_Data` directory is ignored by Git. It may be empty on a fresh machine and will be created on application startup.

## Tests and validation

The current test suite contains 10 passing tests covering:

- Plaid account filtering and mapping.
- Multi-page transaction sync and cursor persistence.
- Restoration of a persisted Plaid cursor.
- Durable transaction insert, update, removal, and cursor storage.
- Bill recurrence expansion.
- Paycheck and bill schedule persistence.

Validated commands:

```text
dotnet build SafeSpend.Web/SafeSpend.Web.csproj     # passes
dotnet test SafeSpend.slnx                          # 10 passed
git diff --check                                    # passes
```

`dotnet build SafeSpend.slnx` was also run as requested. In the current environment it exits during restore before project compilation because the installed SDK is missing `Microsoft.NET.SDK.WorkloadAutoImportPropsLocator` and `Microsoft.NET.SDK.WorkloadManifestTargetsLocator`. The individual web project build and the solution test command both compile successfully.

## Continuing on the home PC

1. Check out or copy this repository, then configure the .NET 10 SDK.
2. Restore the same Plaid Sandbox user-secrets on the home PC. Do not put credentials, access tokens, or the local SQLite database into Git.
3. Run:

   ```text
   dotnet restore SafeSpend.slnx
   dotnet build SafeSpend.Web/SafeSpend.Web.csproj
   dotnet test SafeSpend.slnx
   dotnet run --project SafeSpend.Web/SafeSpend.Web.csproj
   ```

4. Exercise the Plaid Sandbox flow at `/Plaid/Connect`: connect an Item, verify checking/savings balances, click sync, and confirm recent transactions appear on the dashboard.
5. Create a paycheck schedule and at least one recurring bill, then verify that the forecast uses the connected checking/savings balance and expands upcoming bill occurrences.

## Planned next steps

### 1. Make Plaid synchronization automatic

Add a hosted background service or an explicit application sync service that runs transaction sync after connection and on a controlled schedule. Keep the cursor in the database, preserve the current pagination behavior, and surface non-secret sync status to the UI.

### 2. Add durable connection and Item lifecycle handling

The current access token is intentionally held in process memory. A production-ready implementation should add protected storage for the token, support reconnecting after an application restart, and handle Item errors, consent expiration, and disconnect/removal flows without exposing secrets.

### 3. Add migrations

Replace the startup table-creation compatibility code with a real EF Core migration workflow once the schema settles. Keep the local `App_Data` database out of source control.

### 4. Use transaction history for forecasting improvements

The current forecast correctly starts from the live Plaid account balance and applies configured paycheck and bill schedules. The next forecasting improvement is to categorize persisted transactions and use transaction history to suggest recurring bills or validate configured schedules. Avoid subtracting synced transactions again from a current account balance, since that would double-count them.

### 5. Add production safeguards and UI polish

Add authorization and per-user ownership before supporting multiple users, improve error and sync status messaging, add pagination/filtering for transaction history, and add tests for Plaid API error responses and duplicate sync events.

## Working rules

- Never log, display, commit, or include in diagnostics any Plaid access token, public token, client secret, or user-secret value.
- Keep `SafeSpend.Web/App_Data/` ignored.
- Preserve the existing Plaid Link and Going.Plaid conventions when extending the integration.
