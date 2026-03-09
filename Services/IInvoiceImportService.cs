namespace Invoxa.Web.Services;

public interface IInvoiceImportService
{
    Task<ExtractedInvoiceDto> ExtractFromPdfAsync(Stream pdfStream, CancellationToken ct = default);
}
