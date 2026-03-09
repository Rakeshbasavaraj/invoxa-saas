using Invoxa.Web.Domain;

namespace Invoxa.Web.Services;

public interface IPaymentLinkService
{
    /// <summary>
    /// Creates a payment link URL for an invoice. Returns null if not configured.
    /// </summary>
    Task<string?> CreatePaymentLinkAsync(Invoice invoice, Company company, CancellationToken ct = default);
}
