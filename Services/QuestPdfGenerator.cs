using Invoxa.Web.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Invoxa.Web.Services;

public class QuestPdfGenerator : IPdfGenerator
{
    public byte[] GenerateInvoicePdf(Invoice invoice, Company company)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var key = (company.InvoiceTemplateKey ?? "classic").Trim().ToLowerInvariant();

        return key switch
        {
            "professional_blue" => GenerateProfessionalBlue(invoice, company),
            _ => GenerateClassic(invoice, company)
        };
    }

    private static byte[] GenerateClassic(Invoice invoice, Company company)
    {
        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(30);
                page.Size(PageSizes.A4);
                page.DefaultTextStyle(x => x.FontSize(12));

                page.Header().Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        // Optional logo (no placeholder if missing)
                        if (company.LogoBytes != null && company.LogoBytes.Length > 0)
                        {
                            col.Item().Height(45).Image(company.LogoBytes).FitArea();
                            col.Item().PaddingTop(4);
                        }

                        col.Item().Text(company.Name).FontSize(18).Bold().FontColor(Colors.Blue.Darken2);
                        col.Item().Text("Invoice").FontSize(14).SemiBold().FontColor(Colors.Grey.Darken2);

                        if (!string.IsNullOrWhiteSpace(company.AddressLine1)) col.Item().Text(company.AddressLine1);
                        if (!string.IsNullOrWhiteSpace(company.AddressLine2)) col.Item().Text(company.AddressLine2);

                        var cityLine = string.Join(", ", new[] { company.City, company.Country }.Where(s => !string.IsNullOrWhiteSpace(s)));
                        if (!string.IsNullOrWhiteSpace(cityLine)) col.Item().Text(cityLine);

                        if (!string.IsNullOrWhiteSpace(company.VatNumber)) col.Item().Text($"VAT: {company.VatNumber}");
                    });

                    row.ConstantItem(220).Column(col =>
                    {
                        col.Item().Text($"Invoice #: {invoice.InvoiceNumber}").Bold();
                        col.Item().Text($"Issue: {invoice.IssueDate:yyyy-MM-dd}");
                        col.Item().Text($"Due: {invoice.DueDate:yyyy-MM-dd}");
                        col.Item().Text($"Status: {invoice.Status}");
                    });
                });

                page.Content().Column(col =>
                {
                    col.Item().PaddingTop(15).Text($"Bill To: {invoice.Client?.Name}").Bold();

                    if (!string.IsNullOrWhiteSpace(invoice.Client?.Email))
                        col.Item().Text($"Email: {invoice.Client!.Email}");
                    if (!string.IsNullOrWhiteSpace(invoice.Client?.Phone))
                        col.Item().Text($"Phone: {invoice.Client!.Phone}");

                    // Client address (optional)
                    var clientAddr = string.Join("\n", new[]
                    {
                        invoice.Client?.AddressLine1,
                        invoice.Client?.AddressLine2,
                        string.Join(", ", new[] { invoice.Client?.City, invoice.Client?.Country }.Where(s => !string.IsNullOrWhiteSpace(s)))
                    }.Where(s => !string.IsNullOrWhiteSpace(s)));

                    if (!string.IsNullOrWhiteSpace(clientAddr))
                        col.Item().Text(clientAddr);

                    // Ship To (optional)
                    var shipAddr = string.Join("\n", new[]
                    {
                        invoice.ShipToName,
                        invoice.ShipToAddressLine1,
                        invoice.ShipToAddressLine2,
                        string.Join(", ", new[] { invoice.ShipToCity, invoice.ShipToCountry }.Where(s => !string.IsNullOrWhiteSpace(s)))
                    }.Where(s => !string.IsNullOrWhiteSpace(s)));

                    if (!string.IsNullOrWhiteSpace(shipAddr))
                    {
                        col.Item().PaddingTop(8).Text("Ship To:").Bold();
                        col.Item().Text(shipAddr);
                    }

                    col.Item().PaddingTop(15).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(6);
                            columns.RelativeColumn(1);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(HeaderCell).Text("Item").Bold();
                            header.Cell().Element(HeaderCell).AlignRight().Text("Qty").Bold();
                            header.Cell().Element(HeaderCell).AlignRight().Text("Unit").Bold();
                            header.Cell().Element(HeaderCell).AlignRight().Text("Total").Bold();
                        });

                        foreach (var item in invoice.Items)
                        {
                            table.Cell().Element(Cell).Text(item.Description);
                            table.Cell().Element(Cell).AlignRight().Text(item.Quantity.ToString());
                            table.Cell().Element(Cell).AlignRight().Text(item.UnitPrice.ToString("0.00"));
                            table.Cell().Element(Cell).AlignRight().Text(item.LineTotal.ToString("0.00"));
                        }

                        static IContainer HeaderCell(IContainer c) =>
                            c.Background(Colors.Grey.Lighten3).Padding(6).Border(1).BorderColor(Colors.Grey.Lighten2);

                        static IContainer Cell(IContainer c) =>
                            c.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(6);
                    });

                    {
    var subtotal = invoice.Subtotal;
    var taxEnabled = company.TaxEnabled && company.TaxRate > 0;
    var taxRate = taxEnabled ? company.TaxRate : 0m;
    var taxLabel = string.IsNullOrWhiteSpace(company.TaxLabel) ? "Tax" : company.TaxLabel!;
    var taxAmount = subtotal * taxRate / 100m;
    var grandTotal = subtotal + taxAmount;

    col.Item().PaddingTop(10).AlignRight().Text($"Sub Total: {subtotal:0.00}").FontSize(12).SemiBold();
    if (taxEnabled)
        col.Item().AlignRight().Text($"{taxLabel} ({taxRate:0.##}%): {taxAmount:0.00}").FontSize(12);

    col.Item().AlignRight().Text($"Total: {grandTotal:0.00}").FontSize(14).Bold();
}

                    if (!string.IsNullOrWhiteSpace(invoice.Notes))
                        col.Item().PaddingTop(10).Text($"Notes: {invoice.Notes}");
                });

                page.Footer().AlignCenter()
                    .Text("Developed by BasMan Technology • Invoxa")
                    .FontSize(10).FontColor(Colors.Grey.Darken1);
            });
        });

        return doc.GeneratePdf();
    }

    private static byte[] GenerateProfessionalBlue(Invoice invoice, Company company)
    {
        var blue = Colors.Blue.Darken2;
        var lightBlue = Colors.Blue.Lighten5;
        var border = Colors.Grey.Lighten2;

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(28);
                page.Size(PageSizes.A4);
                page.DefaultTextStyle(x => x.FontSize(11));

                page.Header().Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        // Optional logo (no placeholder if missing)
                        if (company.LogoBytes != null && company.LogoBytes.Length > 0)
                        {
                            col.Item().Height(52).Image(company.LogoBytes).FitArea();
                            col.Item().PaddingTop(4);
                        }

                        col.Item().Text(company.Name).FontSize(16).Bold();
                        var addrLines = new[]
                        {
                            company.AddressLine1,
                            company.AddressLine2,
                            string.Join(", ", new[] { company.City, company.Country }.Where(s => !string.IsNullOrWhiteSpace(s)))
                        }.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

                        foreach (var l in addrLines)
                            col.Item().Text(l!).FontSize(10).FontColor(Colors.Grey.Darken2);

                        if (!string.IsNullOrWhiteSpace(company.VatNumber))
                            col.Item().Text($"VAT: {company.VatNumber}").FontSize(10).FontColor(Colors.Grey.Darken2);
                    });

                    row.ConstantItem(200).AlignRight().Column(col =>
                    {
                        col.Item().Text("INVOICE").FontSize(22).Bold().FontColor(blue);
                        col.Item().Text($"Invoice #: {invoice.InvoiceNumber}").Bold();
                        col.Item().Text($"Invoice Date: {invoice.IssueDate:yyyy-MM-dd}");
                        col.Item().Text($"Due Date: {invoice.DueDate:yyyy-MM-dd}");
                        col.Item().Text($"Status: {invoice.Status}");
                    });
                });

                page.Content().PaddingTop(14).Column(col =>
                {
                    // Bill To / Ship To block (Ship To optional)
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn();
                            c.RelativeColumn();
                        });

                        t.Cell().Element(Box(border)).Column(c =>
                        {
                            c.Item().Text("Bill To").Bold();
                            c.Item().Text(invoice.Client?.Name ?? "");
                            if (!string.IsNullOrWhiteSpace(invoice.Client?.Email)) c.Item().Text(invoice.Client!.Email!).FontSize(10);
                            if (!string.IsNullOrWhiteSpace(invoice.Client?.Phone)) c.Item().Text(invoice.Client!.Phone!).FontSize(10);

                            var clientAddr = new[]
                            {
                                invoice.Client?.AddressLine1,
                                invoice.Client?.AddressLine2,
                                string.Join(", ", new[] { invoice.Client?.City, invoice.Client?.Country }.Where(s => !string.IsNullOrWhiteSpace(s)))
                            }.Where(s => !string.IsNullOrWhiteSpace(s));

                            foreach (var l in clientAddr)
                                c.Item().Text(l!).FontSize(10);
                        });

                        t.Cell().Element(Box(border)).Column(c =>
                        {
                            c.Item().Text("Ship To").Bold();

                            var shipLines = new[]
                            {
                                invoice.ShipToName,
                                invoice.ShipToAddressLine1,
                                invoice.ShipToAddressLine2,
                                string.Join(", ", new[] { invoice.ShipToCity, invoice.ShipToCountry }.Where(s => !string.IsNullOrWhiteSpace(s)))
                            }.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

                            if (shipLines.Count == 0)
                            {
                                c.Item().Text("(optional)").FontSize(10).FontColor(Colors.Grey.Darken1);
                            }
                            else
                            {
                                foreach (var l in shipLines)
                                    c.Item().Text(l!).FontSize(10);
                            }
                        });

                        static Func<IContainer, IContainer> Box(string borderColor) =>
                            x => x.Border(1).BorderColor(borderColor).Padding(10);
                    });

                    col.Item().PaddingTop(12);

                    // Items table with blue header
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(24); // #
                            columns.RelativeColumn(6);  // item + desc
                            columns.RelativeColumn(1);  // qty
                            columns.RelativeColumn(2);  // rate
                            columns.RelativeColumn(2);  // amount
                        });

                        table.Header(h =>
                        {
                            h.Cell().Element(BlueHeader).AlignCenter().Text("#").FontColor(Colors.White).Bold();
                            h.Cell().Element(BlueHeader).Text("Item & Description").FontColor(Colors.White).Bold();
                            h.Cell().Element(BlueHeader).AlignRight().Text("Qty").FontColor(Colors.White).Bold();
                            h.Cell().Element(BlueHeader).AlignRight().Text("Rate").FontColor(Colors.White).Bold();
                            h.Cell().Element(BlueHeader).AlignRight().Text("Amount").FontColor(Colors.White).Bold();
                        });

                        int idx = 1;
                        foreach (var item in invoice.Items)
                        {
                            table.Cell().Element(RowCell).AlignCenter().Text(idx.ToString());
                            table.Cell().Element(RowCell).Column(c =>
                            {
                                c.Item().Text(item.Description);
                            });
                            table.Cell().Element(RowCell).AlignRight().Text(item.Quantity.ToString());
                            table.Cell().Element(RowCell).AlignRight().Text(item.UnitPrice.ToString("0.00"));
                            table.Cell().Element(RowCell).AlignRight().Text(item.LineTotal.ToString("0.00"));
                            idx++;
                        }

                        IContainer BlueHeader(IContainer c) => c.Background(blue).PaddingVertical(6).PaddingHorizontal(8).Border(1).BorderColor(Colors.Grey.Lighten2);

                        IContainer RowCell(IContainer c) => c.BorderBottom(1).BorderColor(border).PaddingVertical(8).PaddingHorizontal(8);
                    });

                    col.Item().PaddingTop(10);

                    // Totals box (last page naturally)
                    col.Item().AlignRight().Width(220).Background(lightBlue).Border(1).BorderColor(border).Padding(10).Column(tot =>
                    {
                        var subtotal = invoice.Subtotal;
                        var taxEnabled = company.TaxEnabled && company.TaxRate > 0;
                        var taxRate = taxEnabled ? company.TaxRate : 0m;
                        var taxLabel = string.IsNullOrWhiteSpace(company.TaxLabel) ? "Tax" : company.TaxLabel!;
                        var taxAmount = subtotal * taxRate / 100m;
                        var grandTotal = subtotal + taxAmount;

                        tot.Item().Row(r =>
                        {
                            r.RelativeItem().Text("Sub Total");
                            r.ConstantItem(90).AlignRight().Text(subtotal.ToString("0.00")).Bold();
                        });

                        if (taxEnabled)
                        {
                            tot.Item().Row(r =>
                            {
                                r.RelativeItem().Text($"{taxLabel} ({taxRate:0.##}%)").FontColor(Colors.Grey.Darken1);
                                r.ConstantItem(90).AlignRight().Text(taxAmount.ToString("0.00")).FontColor(Colors.Grey.Darken1);
                            });
                        }

                        tot.Item().PaddingTop(4).Row(r =>
                        {
                            r.RelativeItem().Text("Total").Bold();
                            r.ConstantItem(90).AlignRight().Text(grandTotal.ToString("0.00")).Bold();
                        });

                        tot.Item().PaddingTop(6).Row(r =>
                        {
                            r.RelativeItem().Text("Balance Due").Bold();
                            r.ConstantItem(90).AlignRight().Text(grandTotal.ToString("0.00")).Bold();
                        });
                    });

                    if (!string.IsNullOrWhiteSpace(invoice.Notes))
                    {
                        col.Item().PaddingTop(10).Text("Notes").Bold();
                        col.Item().Text(invoice.Notes).FontColor(Colors.Grey.Darken2);
                    }

                    var thankYou = string.IsNullOrWhiteSpace(company.ThankYouNote) ? "Thanks for your business." : company.ThankYouNote!;
                    var terms = string.IsNullOrWhiteSpace(company.TermsAndConditions) ? "Full payment is due upon receipt of this invoice." : company.TermsAndConditions!;

                    col.Item().PaddingTop(12).Text(thankYou).FontColor(Colors.Grey.Darken2);

                    col.Item().PaddingTop(6).Text("Terms & Conditions").Bold().FontSize(10);
                    col.Item().Text(terms).FontSize(10).FontColor(Colors.Grey.Darken2);
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Developed by BasMan Technology • Invoxa").FontSize(9).FontColor(Colors.Grey.Darken1);
                    t.Span("   |   ");
                    t.Span("Page ");
                    t.CurrentPageNumber();
                    t.Span(" of ");
                    t.TotalPages();
                });
            });
        });

        return doc.GeneratePdf();
    }
}
