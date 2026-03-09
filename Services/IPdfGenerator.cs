using Invoxa.Web.Domain;

namespace Invoxa.Web.Services;

public interface IPdfGenerator
{
    byte[] GenerateInvoicePdf(Invoice invoice, Company company);
}
