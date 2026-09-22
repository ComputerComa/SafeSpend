using Going.Plaid;
using Microsoft.EntityFrameworkCore;
using SafeSpend.Web.Services.Forecasting;
using SafeSpend.Web.Services.Plaid;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddSingleton<PaycheckForecastService>();
builder.Services.AddSingleton<CashFlowScheduleService>();
builder.Services.AddPlaid(builder.Configuration);
var dataDirectory = Path.Combine(
    builder.Environment.ContentRootPath,
    "App_Data");
Directory.CreateDirectory(dataDirectory);
var databasePath = Path.Combine(
    dataDirectory,
    "safespend.db");
builder.Services.AddPooledDbContextFactory<SafeSpendDbContext>(options =>
    options.UseSqlite($"Data Source={databasePath}"));
builder.Services.AddSingleton<PlaidConnectionState>();
builder.Services.AddSingleton<IPlaidApi, PlaidApi>();
builder.Services.AddSingleton<IPlaidTransactionStore, PlaidTransactionStore>();
builder.Services.AddSingleton<IForecastScheduleStore, ForecastScheduleStore>();
builder.Services.AddSingleton<PlaidLinkService>();
var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
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

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
