using QuestPDF.Infrastructure;
using Invoxa.Web.Data;
using Invoxa.Web.Services;
using Microsoft.EntityFrameworkCore;


var builder = WebApplication.CreateBuilder(args);

QuestPDF.Settings.License = LicenseType.Community;


builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();

builder.Services.AddDbContext<AppDbContext>(options =>
{
    var cs = builder.Configuration.GetConnectionString("DefaultConnection");
    options.UseSqlite(cs);
});

builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<IPdfGenerator, QuestPdfGenerator>();
builder.Services.AddScoped<IWhatsAppSender, TwilioWhatsAppSender>();
builder.Services.AddScoped<IPaymentLinkService, StripePaymentLinkService>();
builder.Services.AddSingleton<ICurrentUser, CookieCurrentUser>();
builder.Services.AddScoped<IInvoiceImportService, InvoiceImportService>();

// Background automation (reminders + recurring invoices)
builder.Services.AddSingleton<InvoiceAutomationWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<InvoiceAutomationWorker>());

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    // Schema check for new columns. If DB is from an older version, recreate (dev/MVP).
    try
    {
        _ = db.Companies.Select(c => c.AddressLine1).FirstOrDefault();
        _ = db.Companies.Select(c => c.InvoiceTemplateKey).FirstOrDefault();
        _ = db.Companies.Select(c => c.LogoContentType).FirstOrDefault();
        _ = db.Companies.Select(c => c.TaxRate).FirstOrDefault();
        _ = db.Companies.Select(c => c.TaxEnabled).FirstOrDefault();
        _ = db.Companies.Select(c => c.TaxPresetKey).FirstOrDefault();
        _ = db.Companies.Select(c => c.TermsAndConditions).FirstOrDefault();
        _ = db.Companies.Select(c => c.ThankYouNote).FirstOrDefault();
        _ = db.Companies.Select(c => c.SmtpHost).FirstOrDefault();
        _ = db.Companies.Select(c => c.EmailFromAddress).FirstOrDefault();
        _ = db.Invoices.Select(i => i.ShipToName).FirstOrDefault();
        _ = db.Companies.Select(c => c.InvoicePrefix).FirstOrDefault();
        _ = db.Companies.Select(c => c.NextInvoiceNumber).FirstOrDefault();
        _ = db.Invoices.Select(i => i.PublicToken).FirstOrDefault();

        // New SaaS features
        _ = db.Clients.Select(c => c.PortalToken).FirstOrDefault();
        _ = db.Invoices.Select(i => i.PaymentLink).FirstOrDefault();
        _ = db.Invoices.Select(i => i.RecurrenceEnabled).FirstOrDefault();
        _ = db.Invoices.Select(i => i.NextOccurrenceDate).FirstOrDefault();
        _ = db.Invoices.Select(i => i.IsRecurringTemplate).FirstOrDefault();
        _ = db.UserAccounts.Select(u => u.Email).FirstOrDefault();
        _ = db.UserAccounts.Select(u => u.Role).FirstOrDefault();
        _ = db.UserAccounts.Select(u => u.PasswordResetToken).FirstOrDefault();
        _ = db.UserAccounts.Select(u => u.PasswordResetTokenExpiryUtc).FirstOrDefault();
        _ = db.Companies.Select(c => c.ReminderDaysBeforeDue).FirstOrDefault();
        _ = db.Companies.Select(c => c.AutomationIntervalValue).FirstOrDefault();
        _ = db.Companies.Select(c => c.AutomationIntervalUnit).FirstOrDefault();
        _ = db.EmailTemplates.Select(e => e.TemplateKey).FirstOrDefault();
        _ = db.Companies.Select(c => c.ApprovalStatus).FirstOrDefault();
        _ = db.Companies.Select(c => c.PlanKey).FirstOrDefault();
        _ = db.Companies.Select(c => c.InvoiceLimit).FirstOrDefault();
        _ = db.Companies.Select(c => c.ClientLimit).FirstOrDefault();
        _ = db.Companies.Select(c => c.StripePublishableKey).FirstOrDefault();
        _ = db.Companies.Select(c => c.StripeSecretKey).FirstOrDefault();
        _ = db.Companies.Select(c => c.StripeCurrency).FirstOrDefault();
        _ = db.Companies.Select(c => c.OverdueReminderEnabled).FirstOrDefault();
        _ = db.Companies.Select(c => c.OverdueReminderIntervalValue).FirstOrDefault();
        _ = db.Companies.Select(c => c.OverdueReminderIntervalUnit).FirstOrDefault();
        _ = db.Companies.Select(c => c.DefaultCurrency).FirstOrDefault();
        _ = db.Companies.Select(c => c.PrimaryColor).FirstOrDefault();
        _ = db.Companies.Select(c => c.TableHeaderColor).FirstOrDefault();
        _ = db.Companies.Select(c => c.PdfFontSize).FirstOrDefault();
        _ = db.Companies.Select(c => c.PdfTitleStyle).FirstOrDefault();
        _ = db.Companies.Select(c => c.ShowHsnSac).FirstOrDefault();
        _ = db.Companies.Select(c => c.ShowSgst).FirstOrDefault();
        _ = db.Companies.Select(c => c.ShowCgst).FirstOrDefault();
        _ = db.Companies.Select(c => c.ShowIgst).FirstOrDefault();
        _ = db.Companies.Select(c => c.ShowCess).FirstOrDefault();
        _ = db.Companies.Select(c => c.ShowTerms).FirstOrDefault();
        _ = db.Companies.Select(c => c.ShowNotes).FirstOrDefault();
        _ = db.Companies.Select(c => c.ShowPaymentDetails).FirstOrDefault();
        _ = db.Companies.Select(c => c.PaymentDetails).FirstOrDefault();
        _ = db.Companies.Select(c => c.CustomColumn1Name).FirstOrDefault();
        _ = db.Companies.Select(c => c.CustomColumn2Name).FirstOrDefault();
        _ = db.Companies.Select(c => c.CustomColumn3Name).FirstOrDefault();
        _ = db.Companies.Select(c => c.CustomColumn4Name).FirstOrDefault();
        _ = db.Companies.Select(c => c.ShowSignature).FirstOrDefault();
        _ = db.Companies.Select(c => c.SignatureLabel).FirstOrDefault();
        _ = db.Companies.Select(c => c.SignatureName).FirstOrDefault();
        _ = db.Companies.Select(c => c.AllowyFarmerCompanyName).FirstOrDefault();
        _ = db.Companies.Select(c => c.AllowyFarmerBankIfscCode).FirstOrDefault();
        _ = db.Companies.Select(c => c.AllowyFarmerFooterNote).FirstOrDefault();
    }
    catch
    {
        db.Database.EnsureDeleted();
        db.Database.EnsureCreated();
    }

    SeedData.EnsureSeeded(db);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}
else
{
    // In Development, show detailed exceptions in the browser.
    app.UseDeveloperExceptionPage();
}

app.UseStaticFiles();
app.UseRouting();

// Simple cookie-based route protection for Phase 1 SaaS flow.
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;
    var allowAnonymous =
        path.Equals("/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/Account", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/Public", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/i/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/c/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/Payments/Success", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/Payments/Cancel", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/css", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/js", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/lib", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/api/company/logo", StringComparison.OrdinalIgnoreCase);

    if (!allowAnonymous && !CurrentUserContext.IsAuthenticated(context))
    {
        context.Response.Redirect("/Account/Login?message=Please login to continue.");
        return;
    }

    await next();
});


// Company logo (optional)
app.MapGet("/api/company/logo", async (AppDbContext db) =>
{
    var company = await db.Companies.OrderBy(c => c.CreatedAtUtc).FirstOrDefaultAsync();
    if (company?.LogoBytes == null || company.LogoBytes.Length == 0)
        return Results.NotFound();

    var ct = string.IsNullOrWhiteSpace(company.LogoContentType) ? "image/png" : company.LogoContentType!;
    return Results.File(company.LogoBytes, ct);
});

// AI import: Upload old invoice PDF -> extract fields/items
app.MapPost("/api/ai/extract-invoice", async (HttpRequest request, IInvoiceImportService importer, CancellationToken ct) =>
{
    if (!request.HasFormContentType) return Results.BadRequest(new { error = "Expected multipart/form-data" });
    var form = await request.ReadFormAsync(ct);
    var file = form.Files["file"] ?? form.Files.FirstOrDefault();
    if (file == null || file.Length == 0) return Results.BadRequest(new { error = "No file uploaded" });

    var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
    if (ext != ".pdf")
    {
        return Results.BadRequest(new { error = "Only PDF supported for now. (Image OCR can be added next.)" });
    }

    await using var stream = file.OpenReadStream();
    var result = await importer.ExtractFromPdfAsync(stream, ct);
    return Results.Ok(result);
});

app.MapRazorPages();
app.Run();
