using MedLoop.NextGen.Data;
using MedLoop.NextGen.Jobs;
using MedLoop.NextGen.Models;
using MedLoop.NextGen.Services;
using Microsoft.AspNetCore.Identity;
using Quartz;
using QuestPDF.Infrastructure;

// See the licensing note on QuestPdfInvoiceService: Community is free only
// under QuestPDF's revenue/org-type conditions — confirm eligibility before
// this ships to production.
QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// Swap this for a real gateway implementation (e.g. Mastercard/MCB) via a
// single line here once one is wired up — no controller code needs to
// change, since OrdersController depends only on IPaymentGateway.
builder.Services.AddScoped<IPaymentGateway, MockPaymentGateway>();
builder.Services.AddScoped<IEmailSender, GmailEmailSender>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IInvoiceService, QuestPdfInvoiceService>();
builder.Services.AddScoped<ILoyaltyService, LoyaltyService>();

builder.Services
    .AddIdentityApiEndpoints<ApplicationUser>(options =>
    {
        options.Password.RequiredLength = 12;
        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>();

// Registered AFTER AddIdentityApiEndpoints on purpose: that call internally
// registers a no-op IEmailSender<ApplicationUser> via TryAdd, and this
// explicit registration needs to be the one that wins so account
// confirmation / password reset emails actually get sent through Gmail
// instead of silently going nowhere.
builder.Services.AddScoped<IEmailSender<ApplicationUser>, IdentityEmailSenderAdapter>();

var nearExpiryJobKey = new JobKey("NearExpiryNotificationJob");
var markExpiredJobKey = new JobKey("MarkExpiredListingsJob");

builder.Services.AddQuartz(q =>
{
    q.UseMicrosoftDependencyInjectionJobFactory();

    q.AddJob<NearExpiryNotificationJob>(opts => opts.WithIdentity(nearExpiryJobKey).StoreDurably());
    q.AddTrigger(opts => opts
        .ForJob(nearExpiryJobKey)
        .WithIdentity("NearExpiryNotificationTrigger")
        .WithCronSchedule("0 0 8 * * ?")); // daily at 08:00 UTC

    q.AddJob<MarkExpiredListingsJob>(opts => opts.WithIdentity(markExpiredJobKey).StoreDurably());
    q.AddTrigger(opts => opts
        .ForJob(markExpiredJobKey)
        .WithIdentity("MarkExpiredListingsTrigger")
        .WithCronSchedule("0 30 0 * * ?")); // daily at 00:30 UTC
});

builder.Services.AddQuartzHostedService(opts => opts.WaitForJobsToComplete = true);

// Required alongside q.AddJob<T>() above: UseMicrosoftDependencyInjectionJobFactory
// resolves job instances directly from the container, so each job type also
// needs its own explicit DI registration (Scoped, so it gets a fresh
// AppDbContext per run — same pattern the legacy app used for its Quartz jobs).
builder.Services.AddScoped<NearExpiryNotificationJob>();
builder.Services.AddScoped<MarkExpiredListingsJob>();

builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // Convenience only: auto-apply migrations in dev so `docker compose up` just works.
    // Production should run migrations as an explicit release step instead of on every boot.
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapIdentityApi<ApplicationUser>();
app.MapControllers();

app.Run();
