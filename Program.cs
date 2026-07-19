using MedLoop.NextGen.Components;
using MedLoop.NextGen.Data;
using MedLoop.NextGen.Jobs;
using MedLoop.NextGen.Models;
using MedLoop.NextGen.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Quartz;
using QuestPDF.Infrastructure;
using System.Text.Json.Serialization.Metadata;

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

// Singleton, not Scoped: MapIdentityApi<TUser>() resolves IEmailSender<TUser>
// from the root service provider once at startup (not per-request), which
// throws for a Scoped registration ("Cannot resolve scoped service ... from
// root provider") — caught by actually running the app, not something that
// shows up at compile time. GmailEmailSender has no Scoped dependencies
// (just IConfiguration), so Singleton is safe here.
builder.Services.AddSingleton<IEmailSender, GmailEmailSender>();
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
// instead of silently going nowhere. Singleton for the same root-provider
// resolution reason as IEmailSender above.
builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityEmailSenderAdapter>();

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

// Both settings found by actually exercising the API end-to-end, not by
// inspection — neither shows up as a compile error.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // 1) System.Text.Json serializes enums (ProductListingStatus,
        // BidStatus, OrderStatus, etc.) as raw numbers by default, e.g.
        // {"status":3} instead of {"status":"ForRedistribution"} —
        // unreadable for any API consumer.
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());

        // 2) EF Core's navigation-property fixup means a Bid returned with
        // its NegotiationRounds included has, on each round, a populated
        // `Bid` back-reference to the very same Bid instance — a genuine
        // reference cycle. Confirmed by actually calling POST /api/bids:
        // the response never terminated (curl gave up with "transfer
        // closed with outstanding read data remaining") because the
        // serializer kept expanding the cycle instead of failing cleanly.
        // The same shape of bug would hit any entity pair with
        // bidirectional navigation properties serialized together
        // (Pharmacy/Product, PosSale/PosSaleItem, etc.), not just Bid —
        // IgnoreCycles fixes all of them at once by emitting null the
        // second time an object would repeat, instead of recursing or
        // throwing.
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;

        // 3) ApplicationUser extends IdentityUser directly, and several
        // endpoints return it as a nested navigation property (Bid.BuyerUser,
        // BidNegotiationRound.ProposedByUser, PosSale.CashierUser, etc.).
        // Confirmed by actually calling PUT /api/bids/{id}/counter: the
        // response included the other party's full PasswordHash,
        // SecurityStamp, and ConcurrencyStamp in plain JSON — a real
        // credential leak to anyone on the other side of a negotiation, not
        // a hypothetical. This strips those three fields off ApplicationUser
        // specifically, everywhere it's serialized, without touching the EF
        // mapping or requiring every controller to hand-build a DTO.
        var identitySensitiveFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PasswordHash", "SecurityStamp", "ConcurrencyStamp"
        };
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(typeInfo =>
        {
            if (typeInfo.Type != typeof(ApplicationUser))
            {
                return;
            }

            foreach (var property in typeInfo.Properties)
            {
                if (identitySensitiveFields.Contains(property.Name))
                {
                    property.ShouldSerialize = static (_, _) => false;
                }
            }
        });
        options.JsonSerializerOptions.TypeInfoResolver = resolver;
    });

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

    // Seeds a ready-to-use seller/buyer/admin account plus two demo listings
    // on first run, so the portal isn't empty until someone registers
    // manually. Idempotent — see DbSeeder for the per-record existence checks.
    await DbSeeder.SeedAsync(scope.ServiceProvider);
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
