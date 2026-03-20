using Invoxa.Web.Domain;

namespace Invoxa.Web.Services;

public static class InvoiceMoney
{
    public static string GetCurrencyCode(Company? company)
    {
        var code = company?.DefaultCurrency;
        if (string.IsNullOrWhiteSpace(code))
            code = company?.StripeCurrency;
        return string.IsNullOrWhiteSpace(code) ? "USD" : code.Trim().ToUpperInvariant();
    }

    public static string GetCurrencySymbol(Company? company)
    {
        return GetCurrencySymbol(GetCurrencyCode(company));
    }

    public static string GetCurrencySymbol(string? code)
    {
        return (code ?? "USD").Trim().ToUpperInvariant() switch
        {
            "INR" => "₹",
            "KWD" => "KD ",
            "USD" => "$",
            _ => ((code ?? "USD").Trim().ToUpperInvariant() + " ")
        };
    }

    public static decimal GetTaxAmount(Invoice invoice, Company? company)
    {
        if (company == null || !company.TaxEnabled || company.TaxRate <= 0)
            return 0m;
        return Math.Round(invoice.Subtotal * company.TaxRate / 100m, 2, MidpointRounding.AwayFromZero);
    }

    public static decimal GetGrandTotal(Invoice invoice, Company? company)
    {
        return invoice.Subtotal + GetTaxAmount(invoice, company);
    }

    public static string FormatAmount(decimal amount, Company? company)
    {
        return $"{GetCurrencySymbol(company)}{amount:0.00}";
    }
}
