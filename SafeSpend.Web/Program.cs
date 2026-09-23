using System.Globalization;
using Going.Plaid;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using SafeSpend.Web.Services.Forecasting;
using SafeSpend.Web.Services.Identity;
using SafeSpend.Web.Services.Plaid;

var builder = WebApplication.CreateBuilder(args);
// Keep local production runs able to use the same user-secrets store as
// development. Deployed production hosts should provide these values through
// their secret manager or environment variables.
builder.Configuration.AddUserSecrets<Program>(optional: true);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var culture = CultureInfo.GetCultureInfo("en-US");
    options.DefaultRequestCulture = new RequestCulture(culture);
    options.SupportedCultures = [culture];
    options.SupportedUICultures = [culture];
});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddEntityFrameworkStores<SafeSpendIdentityDbContext>()
    .AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.SlidingExpiration = true;
});
builder.Services.AddSingleton<PaycheckForecastService>();
builder.Services.AddSingleton<CashFlowScheduleService>();
builder.Services.AddPlaid(builder.Configuration);

if (builder.Environment.IsProduction() &&
    !string.Equals(
        builder.Configuration["Plaid:Environment"],
        "Production",
        StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        "Production hosting requires Plaid:Environment=Production.");
}

if (builder.Environment.IsProduction() &&
    (!Uri.TryCreate(
        builder.Configuration["SafeSpend:PlaidWebhookUrl"],
        UriKind.Absolute,
        out var plaidWebhookUri) ||
     !string.Equals(
         plaidWebhookUri.Scheme,
         Uri.UriSchemeHttps,
         StringComparison.OrdinalIgnoreCase)))
{
    throw new InvalidOperationException(
        "Production hosting requires a public HTTPS SafeSpend:PlaidWebhookUrl.");
}

builder.Services.AddHttpContextAccessor();
var dataDirectory = Path.Combine(
    builder.Environment.ContentRootPath,
    builder.Configuration["SafeSpend:DataDirectory"] ?? "App_Data");
Directory.CreateDirectory(dataDirectory);
var dataProtectionBuilder = builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataDirectory))
    .SetApplicationName("SafeSpend");
if (OperatingSystem.IsWindows())
{
    dataProtectionBuilder.ProtectKeysWithDpapi();
}
var databasePath = Path.Combine(
    dataDirectory,
    "safespend.db");
var identityDatabasePath = Path.Combine(
    dataDirectory,
    "identity.db");
builder.Services.AddPooledDbContextFactory<SafeSpendDbContext>(options =>
    options.UseSqlite($"Data Source={databasePath}"));
builder.Services.AddDbContext<SafeSpendIdentityDbContext>(options =>
    options.UseSqlite($"Data Source={identityDatabasePath}"));
builder.Services.AddSingleton<IPlaidApi, PlaidApi>();
builder.Services.AddSingleton<IPlaidSyncCoordinator, PlaidSyncCoordinator>();
builder.Services.AddSingleton<IPlaidSyncQueue, PlaidSyncQueue>();
builder.Services.AddScoped<IPlaidWebhookVerifier, PlaidWebhookVerifier>();
builder.Services.AddScoped<PlaidWebhookService>();
builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
builder.Services.AddSingleton<ILegacyPlaidAccessTokenProtector>(
    _ => new LegacyPlaidAccessTokenProtector(
        LegacyPlaidAccessTokenProtector.LoadKeyDirectories(
            dataDirectory,
            builder.Environment.ContentRootPath,
            builder.Configuration.GetSection(
                    "SafeSpend:LegacyDataProtectionKeyDirectories")
                .Get<string[]>() ?? []),
        LegacyPlaidAccessTokenProtector.LoadApplicationNames(
            Path.Combine(
                dataDirectory,
                "legacy-data-protection-applications"),
            builder.Configuration.GetSection(
                    "SafeSpend:LegacyDataProtectionApplicationNames")
                .Get<string[]>() ?? [],
            builder.Environment.ContentRootPath)));
builder.Services.AddScoped<IPlaidConnectionStore, PlaidConnectionStore>();
builder.Services.AddSingleton<IPlaidTransactionStore, PlaidTransactionStore>();
builder.Services.AddSingleton<IForecastScheduleStore, ForecastScheduleStore>();
builder.Services.AddScoped<PlaidLinkService>();
builder.Services.AddScoped<IdentitySetupService>();
builder.Services.AddHostedService<PlaidTransactionSyncWorker>();
var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var identityContext = scope.ServiceProvider
        .GetRequiredService<SafeSpendIdentityDbContext>();
    await IdentityDatabaseInitializer.InitializeAsync(identityContext);

    var contextFactory = scope.ServiceProvider
        .GetRequiredService<IDbContextFactory<SafeSpendDbContext>>();
    await using var context =
        await contextFactory.CreateDbContextAsync();
    await SafeSpendDatabaseInitializer.InitializeAsync(context);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnet-core-hsts.
    app.UseHsts();
}

if (builder.Configuration.GetValue<bool>("SafeSpend:BehindProxy"))
{
    app.UseForwardedHeaders();
}

app.UseRequestLocalization();
app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapPost(
        "/api/plaid/webhook",
        async (
            HttpRequest request,
            PlaidWebhookService webhookService,
            CancellationToken cancellationToken) =>
        {
            if (request.ContentLength is > 256_000)
            {
                return Results.BadRequest();
            }

            var body = await ReadWebhookBodyAsync(
                request.Body,
                cancellationToken);
            if (body is null)
            {
                return Results.BadRequest();
            }

            var result = await webhookService.HandleAsync(
                body,
                request.Headers["Plaid-Verification"].ToString(),
                cancellationToken);

            return result.IsValid
                ? Results.Ok()
                : Results.Unauthorized();
        })
    .AllowAnonymous();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();

static async Task<byte[]?> ReadWebhookBodyAsync(
    Stream body,
    CancellationToken cancellationToken)
{
    const int maximumBodyBytes = 256_000;
    await using var buffer = new MemoryStream();
    var chunk = new byte[8192];
    var totalBytes = 0;

    int bytesRead;
    while ((bytesRead = await body.ReadAsync(chunk, cancellationToken)) > 0)
    {
        totalBytes += bytesRead;
        if (totalBytes > maximumBodyBytes)
        {
            return null;
        }

        await buffer.WriteAsync(
            chunk.AsMemory(0, bytesRead),
            cancellationToken);
    }

    return buffer.ToArray();
}
