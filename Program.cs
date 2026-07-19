using MedLoop.NextGen.Components;
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
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// The B2B portal: Blazor Server, added alongside the existing API
// controllers in this same project rather than a separate frontend stack.
// Pages call AppDbContext/services directly (not through the REST API) —
// see the README for why, and the refactor this implies once the portal
// is confirmed working end-to-end.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

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
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapIdentityApi<ApplicationUser>();
app.MapControllers();

// Plain HTTP form-post endpoints for the portal's login/register/logout,
// rather than Blazor interactive EditForms. This sidesteps a known class
// of Blazor Server timing issue (SignInManager/HttpContext.SignInAsync
// needs to write to the HTTP response, which isn't possible once an
// interactive SignalR circuit has already been established) — these pages
// render as plain static HTML forms instead.
//
// KNOWN GAP, called out rather than silently skipped: .DisableAntiforgery()
// is used here because the plain forms don't currently include an
// antiforgery token. This should be fixed (via Blazor's <AntiforgeryToken />
// component plus validating it here) before this goes to production — the
// legacy app's audit flagged missing CSRF protection as a real, exploited
// pattern, and this is the same class of gap, just in new code.
app.MapPost("/account/login-form", async (HttpContext httpContext, SignInManager<ApplicationUser> signInManager) =>
{
    var form = await httpContext.Request.ReadFormAsync();
    var email = form["email"].ToString();
    var password = form["password"].ToString();
    var returnUrl = form["returnUrl"].ToString();

    var result = await signInManager.PasswordSignInAsync(email, password, isPersistent: true, lockoutOnFailure: false);
    if (!result.Succeeded)
    {
        return Results.Redirect($"/account/login?error=1&returnUrl={Uri.EscapeDataString(returnUrl)}");
    }

    return Results.Redirect(string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl);
}).DisableAntiforgery();

app.MapPost("/account/register-form", async (
    HttpContext httpContext,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    AppDbContext db) =>
{
    var form = await httpContext.Request.ReadFormAsync();
    var email = form["email"].ToString();
    var password = form["password"].ToString();
    var displayName = form["displayName"].ToString();
    var pharmacyName = form["pharmacyName"].ToString();
    var returnUrl = form["returnUrl"].ToString();

    // Registering with a pharmacy name creates a new pharmacy and joins it
    // as staff; leaving it blank registers a plain consumer account (used
    // by the B2C take-back/loyalty flow) — no separate account-type toggle
    // needed.
    string? pharmacyId = null;

    if (!string.IsNullOrWhiteSpace(pharmacyName))
    {
        var pharmacy = new Pharmacy { Name = pharmacyName };
        db.Pharmacies.Add(pharmacy);
        await db.SaveChangesAsync();
        pharmacyId = pharmacy.Id;
    }

    var user = new ApplicationUser
    {
        UserName = email,
        Email = email,
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? email : displayName,
        PharmacyId = pharmacyId
    };

    var result = await userManager.CreateAsync(user, password);
    if (!result.Succeeded)
    {
        var errors = string.Join("; ", result.Errors.Select(e => e.Description));
        return Results.Redirect($"/account/register?error={Uri.EscapeDataString(errors)}");
    }

    await signInManager.SignInAsync(user, isPersistent: true);
    return Results.Redirect(string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl);
}).DisableAntiforgery();

app.MapPost("/account/logout", async (SignInManager<ApplicationUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.Redirect("/");
}).DisableAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
