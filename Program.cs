using MedLoop.NextGen.Data;
using MedLoop.NextGen.Models;
using MedLoop.NextGen.Services;
using Microsoft.AspNetCore.Identity;
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
