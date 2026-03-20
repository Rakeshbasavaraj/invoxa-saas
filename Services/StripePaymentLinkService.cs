using Invoxa.Web.Domain;
using Stripe;
using Stripe.Checkout;

namespace Invoxa.Web.Services;

public class StripePaymentLinkService : IPaymentLinkService
{
    private readonly IConfiguration _cfg;

    public StripePaymentLinkService(IConfiguration cfg)
    {
        _cfg = cfg;
    }

    public async Task<string?> CreatePaymentLinkAsync(Invoxa.Web.Domain.Invoice invoice, Company company, CancellationToken ct = default)
    {
        try
        {
            var companySecret = company.StripeSecretKey?.Trim();
            var configSecret = _cfg["Stripe:SecretKey"]?.Trim();
            var envSecret = Environment.GetEnvironmentVariable("Stripe__SecretKey")?.Trim();
            var secret = !string.IsNullOrWhiteSpace(companySecret)
                ? companySecret
                : !string.IsNullOrWhiteSpace(configSecret)
                    ? configSecret
                    : envSecret;

            Console.WriteLine("=== STRIPE DEBUG START ===");
            Console.WriteLine($"Company StripeSecretKey exists: {!string.IsNullOrWhiteSpace(companySecret)}");
            Console.WriteLine($"Config Stripe:SecretKey exists: {!string.IsNullOrWhiteSpace(configSecret)}");
            Console.WriteLine($"Env Stripe__SecretKey exists: {!string.IsNullOrWhiteSpace(envSecret)}");
            Console.WriteLine($"Invoice Total: {InvoiceMoney.GetGrandTotal(invoice, company)}");
            Console.WriteLine($"Invoice PublicToken exists: {!string.IsNullOrWhiteSpace(invoice.PublicToken)}");
            Console.WriteLine($"Company StripeCurrency: {company.StripeCurrency}");
            Console.WriteLine($"Config Stripe:Currency: {_cfg["Stripe:Currency"]}");

            if (string.IsNullOrWhiteSpace(secret))
            {
                Console.WriteLine("Stripe secret key is missing.");
                return null;
            }

            if (InvoiceMoney.GetGrandTotal(invoice, company) <= 0)
            {
                Console.WriteLine("Invoice total must be greater than zero.");
                return null;
            }

            if (string.IsNullOrWhiteSpace(invoice.PublicToken))
            {
                Console.WriteLine("Invoice public token is missing.");
                return null;
            }

            StripeConfiguration.ApiKey = secret;

            var currency = (!string.IsNullOrWhiteSpace(company.StripeCurrency)
                ? company.StripeCurrency
                : _cfg["Stripe:Currency"] ?? "usd").Trim().ToLowerInvariant();

            var publicBase = (_cfg["App:BaseUrl"] ?? Environment.GetEnvironmentVariable("App__BaseUrl") ?? string.Empty).Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(publicBase))
            {
                publicBase = "http://localhost:5000";
            }

            var companyName = !string.IsNullOrWhiteSpace(company.Name) ? company.Name : "Invoxa";
            var invoiceNumber = string.IsNullOrWhiteSpace(invoice.InvoiceNumber) ? "Invoice" : invoice.InvoiceNumber;
            var customerEmail = string.IsNullOrWhiteSpace(invoice.Client?.Email) ? null : invoice.Client!.Email!.Trim();

            var options = new SessionCreateOptions
            {
                Mode = "payment",
                SuccessUrl = $"{publicBase}/Payments/Success?invoiceId={invoice.Id}&token={invoice.PublicToken}&session_id={{CHECKOUT_SESSION_ID}}",
                CancelUrl = $"{publicBase}/Payments/Cancel?token={invoice.PublicToken}",
                CustomerEmail = customerEmail,
                Metadata = new Dictionary<string, string>
                {
                    ["invoiceId"] = invoice.Id.ToString(),
                    ["invoiceNumber"] = invoice.InvoiceNumber ?? string.Empty,
                    ["publicToken"] = invoice.PublicToken,
                    ["companyId"] = company.Id.ToString()
                },
                LineItems = new List<SessionLineItemOptions>
                {
                    new SessionLineItemOptions
                    {
                        Quantity = 1,
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            Currency = currency,
                            UnitAmount = ToMinorUnits(InvoiceMoney.GetGrandTotal(invoice, company), currency),
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = $"Invoice {invoiceNumber}",
                                Description = $"Payment for invoice {invoiceNumber} - {companyName}"
                            }
                        }
                    }
                }
            };

            var service = new SessionService();
            var session = await service.CreateAsync(options, cancellationToken: ct);

            Console.WriteLine("Stripe session created successfully.");
            Console.WriteLine($"Session URL: {session.Url}");
            Console.WriteLine("=== STRIPE DEBUG END ===");

            return session.Url;
        }
        catch (StripeException ex)
        {
            Console.WriteLine("=== STRIPE ERROR ===");
            Console.WriteLine(ex.ToString());
            Console.WriteLine("====================");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine("=== GENERAL PAYMENT ERROR ===");
            Console.WriteLine(ex.ToString());
            Console.WriteLine("=============================");
            return null;
        }
    }

    private static long ToMinorUnits(decimal amount, string currency)
    {
        var threeDecimalCurrencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bhd", "jod", "kwd", "omr", "tnd"
        };

        var factor = threeDecimalCurrencies.Contains(currency) ? 1000m : 100m;
        return (long)Math.Round(amount * factor, MidpointRounding.AwayFromZero);
    }
}
