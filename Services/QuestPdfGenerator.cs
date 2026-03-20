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
        var key = (company.InvoiceTemplateKey ?? "default_modern").Trim().ToLowerInvariant();
        return key switch
        {
            "clean_minimal" => GenerateTemplate(invoice, company, false, false, false),
            "gst_pro" => GenerateTemplate(invoice, company, true, false, false),
            "premium_classic" => GenerateTemplate(invoice, company, false, true, false),
            "corporate_violet" => GenerateTemplate(invoice, company, false, false, true),
            "alloy_farmer" => GenerateAlloyFarmerTemplate(invoice, company),
            _ => GenerateTemplate(invoice, company, false, false, false)
        };
    }


    private static byte[] GenerateAlloyFarmerTemplate(Invoice invoice, Company company)
    {
        var companyName = FirstValue(company.AllowyFarmerCompanyName, company.Name, "Allowyfarmer");
        var companyAddress1 = FirstValue(company.AllowyFarmerAddressLine1, company.AddressLine1);
        var companyAddress2 = FirstValue(company.AllowyFarmerAddressLine2, company.AddressLine2);
        var companyCity = FirstValue(company.AllowyFarmerCityLine, JoinComma(company.City, company.Country));
        var companyWebsite = company.AllowyFarmerWebsite;
        var companyPhone = company.AllowyFarmerPhone;
        var companyGst = FirstValue(company.AllowyFarmerGstNumber, company.VatNumber);
        var invoicePrefix = FirstValue(company.AllowyFarmerInvoicePrefix, "Invoice No.");
        var deliveryNote = FirstValue(company.AllowyFarmerDefaultDeliveryNote, "NA");
        var modeTerms = FirstValue(company.AllowyFarmerDefaultModeTerms, "As per terms and conditions");
        var defaultHsn = FirstValue(company.AllowyFarmerDefaultHsnCode, "94029090");

        var bankBeneficiary = company.AllowyFarmerBankBeneficiary;
        var bankAccountNumber = company.AllowyFarmerBankAccountNumber;
        var bankAccountType = company.AllowyFarmerBankAccountType;
        var bankName = company.AllowyFarmerBankName;
        var bankBranch = company.AllowyFarmerBankBranch;
        var bankIfsc = company.AllowyFarmerBankIfscCode;
        var footerNote = FirstValue(company.AllowyFarmerFooterNote,
            "Note: Any other taxes / checkpost charges if any other than mentioned applicable will be borne by the Customer & is not included in our price");

        var subtotal = invoice.Subtotal;
        var taxAmount = InvoiceMoney.GetTaxAmount(invoice, company);
        var grandTotal = InvoiceMoney.GetGrandTotal(invoice, company);
        var taxLabel = string.IsNullOrWhiteSpace(company.TaxLabel) ? "GST" : company.TaxLabel!;
        var taxRate = company.TaxRate <= 0 ? 18m : company.TaxRate;

        var buyerName = invoice.Client?.Name ?? string.Empty;
        var buyerAddress1 = invoice.Client?.AddressLine1;
        var buyerAddress2 = invoice.Client?.AddressLine2;
        var buyerCity = JoinComma(invoice.Client?.City, invoice.Client?.Country);
        var buyerPhone = invoice.Client?.Phone;
        var buyerGst = invoice.Client?.VatNumber;

        var deliveryName = string.IsNullOrWhiteSpace(invoice.ShipToName) ? buyerName : invoice.ShipToName!;
        var deliveryAddress1 = string.IsNullOrWhiteSpace(invoice.ShipToAddressLine1) ? buyerAddress1 : invoice.ShipToAddressLine1;
        var deliveryAddress2 = string.IsNullOrWhiteSpace(invoice.ShipToAddressLine2) ? buyerAddress2 : invoice.ShipToAddressLine2;
        var deliveryCity = string.IsNullOrWhiteSpace(invoice.ShipToCity) && string.IsNullOrWhiteSpace(invoice.ShipToCountry)
            ? buyerCity
            : JoinComma(invoice.ShipToCity, invoice.ShipToCountry);
        var deliveryPhone = buyerPhone;
        var deliveryGst = buyerGst;

        var itemDescription = BuildItemDescription(invoice);
        var qtyText = invoice.Items.Any() ? string.Join(" + ", invoice.Items.Select(i => FormatDecimal(i.Quantity))) : "1";
        var rateText = invoice.Items.Any() ? string.Join(" + ", invoice.Items.Select(i => FormatMoneyPlain(i.UnitPrice))) : "0";
        var amountText = FormatMoneyPlain(subtotal);

        var words = NumberToWords((long)Math.Round(grandTotal));
        var amountInWords = $"Rs. {words} Rupee only";

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(18);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Content().Border(1).BorderColor(Colors.Black).Column(col =>
                {
                    col.Item().Element(CellBorder).Height(14);
                    col.Item().Element(CellBorder).Height(14).AlignCenter().AlignMiddle().Text("T A X   I N V O I C E").Bold();
                    col.Item().Element(CellBorder).Height(14);

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(6);
                            cols.RelativeColumn(3);
                            cols.RelativeColumn(3);
                        });

                        table.Cell().Element(CellBorder).Padding(3).Column(x =>
                        {
                            x.Item().Text(companyName).Bold();
                            if (!string.IsNullOrWhiteSpace(companyAddress1)) x.Item().Text(companyAddress1);
                            if (!string.IsNullOrWhiteSpace(companyAddress2)) x.Item().Text(companyAddress2);
                            if (!string.IsNullOrWhiteSpace(companyCity)) x.Item().Text(companyCity);
                            if (!string.IsNullOrWhiteSpace(companyWebsite)) x.Item().Text($"Website - {companyWebsite}");
                            x.Item().Height(12);
                            if (!string.IsNullOrWhiteSpace(companyPhone)) x.Item().Text($"Phone No: {companyPhone}");
                            x.Item().Height(24);
                            if (!string.IsNullOrWhiteSpace(companyGst)) x.Item().Text($"GST: {companyGst}").Bold();
                        });

                        table.Cell().Element(CellBorder).Padding(3).Column(x =>
                        {
                            x.Item().Text(invoicePrefix).Bold();
                            x.Item().Text(invoice.InvoiceNumber).Bold();
                            x.Item().Text("Delivery Note").Bold();
                            x.Item().Text(deliveryNote).Bold();
                            x.Item().Text("HSN Code").Bold();
                            x.Item().Text(defaultHsn).Bold();
                            x.Item().Text("Buyer's Order No.").Bold();
                            x.Item().Text(string.IsNullOrWhiteSpace(invoice.Notes) ? "-" : invoice.Notes!).Bold();
                        });

                        table.Cell().Element(CellBorder).Padding(0).Table(inner =>
                        {
                            inner.ColumnsDefinition(c => c.RelativeColumn());
                            inner.Cell().Element(CellBorder).Padding(3).Column(x =>
                            {
                                x.Item().Text("Dated").Bold();
                                x.Item().Text(invoice.IssueDate.ToString("dd.MM.yy")).Bold();
                            });
                            inner.Cell().Element(CellBorder).Padding(3).Column(x =>
                            {
                                x.Item().Text("Mode/Terms of Payment").Bold();
                                x.Item().Text(modeTerms).Bold();
                            });
                        });

                        table.Cell().Element(CellBorder).Padding(3).Text("Billing Address :").Bold();
                        table.Cell().Element(CellBorder).Padding(3).Text("Delivery Address :").Bold();
                        table.Cell().Element(CellBorder).Padding(3).Text("");

                        table.Cell().Element(CellBorder).Padding(3).Column(x =>
                        {
                            x.Item().Text(buyerName).Bold();
                            if (!string.IsNullOrWhiteSpace(buyerAddress1)) x.Item().Text(buyerAddress1);
                            if (!string.IsNullOrWhiteSpace(buyerAddress2)) x.Item().Text(buyerAddress2);
                            if (!string.IsNullOrWhiteSpace(buyerCity)) x.Item().Text(buyerCity);
                            if (!string.IsNullOrWhiteSpace(buyerPhone)) x.Item().Text($"Telephone No: {buyerPhone}");
                            if (!string.IsNullOrWhiteSpace(buyerGst)) x.Item().Text($"GST No: {buyerGst}").Bold();
                        });
                        table.Cell().Element(CellBorder).Padding(3).Column(x =>
                        {
                            x.Item().Text(deliveryName).Bold();
                            if (!string.IsNullOrWhiteSpace(deliveryAddress1)) x.Item().Text(deliveryAddress1);
                            if (!string.IsNullOrWhiteSpace(deliveryAddress2)) x.Item().Text(deliveryAddress2);
                            if (!string.IsNullOrWhiteSpace(deliveryCity)) x.Item().Text(deliveryCity);
                            if (!string.IsNullOrWhiteSpace(deliveryPhone)) x.Item().Text($"Telephone No: {deliveryPhone}");
                            if (!string.IsNullOrWhiteSpace(deliveryGst)) x.Item().Text($"GST No: {deliveryGst}").Bold();
                        });
                        table.Cell().Element(CellBorder).Text("");
                    });

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(8);
                            cols.RelativeColumn(2);
                            cols.RelativeColumn(2);
                            cols.RelativeColumn(3);
                        });

                        table.Cell().Element(CellBorder).Padding(3).AlignCenter().Text("Particulars").Bold();
                        table.Cell().Element(CellBorder).Padding(3).AlignCenter().Text("Qty").Bold();
                        table.Cell().Element(CellBorder).Padding(3).AlignCenter().Text("Rate per Unit").Bold();
                        table.Cell().Element(CellBorder).Padding(3).AlignCenter().Text("Amount").Bold();

                        table.Cell().Element(CellBorder).MinHeight(118).Padding(3).Text(itemDescription);
                        table.Cell().Element(CellBorder).MinHeight(118).AlignCenter().AlignMiddle().Text(qtyText).Bold();
                        table.Cell().Element(CellBorder).MinHeight(118).AlignCenter().AlignMiddle().Text(rateText).Bold();
                        table.Cell().Element(CellBorder).MinHeight(118).AlignRight().Padding(3).Text(amountText).Bold();

                        table.Cell().Element(CellBorder).Padding(3).AlignCenter().Text("Total value").Bold();
                        table.Cell().Element(CellBorder).Padding(3).Text("");
                        table.Cell().Element(CellBorder).Padding(3).Text("");
                        table.Cell().Element(CellBorder).Padding(3).AlignRight().Text(FormatMoneyPlain(subtotal)).Bold();
                    });

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(6);
                            cols.RelativeColumn(4);
                        });

                        table.Cell().Element(CellBorder).Padding(3).Column(x =>
                        {
                            x.Item().Text("Amount Chargeable (in words)").Bold();
                            x.Item().Text(amountInWords).FontColor(Colors.Red.Medium);
                        });

                        table.Cell().Element(CellBorder).Padding(0).Table(inner =>
                        {
                            inner.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(2);
                                c.RelativeColumn(1);
                            });

                            AddTotalsRow(inner, "Assessible Value", FormatMoneyPlain(subtotal));
                            AddTotalsRow(inner, "Freight", "-");
                            AddTotalsRow(inner, "Total taxable value", FormatMoneyPlain(subtotal));
                            AddTotalsRow(inner, $"{taxLabel} @ {taxRate:0.##}%", FormatMoneyPlain(taxAmount));
                            AddTotalsRow(inner, "Grand total", FormatMoneyPlain(grandTotal), true);
                            AddTotalsRow(inner, "", "");
                            AddTotalsRow(inner, "", "");

                            void AddTotalsRow(TableDescriptor t, string left, string right, bool bold = false)
                            {
                                var leftCell = t.Cell().Element(CellBorder).Padding(3);
                                var rightCell = t.Cell().Element(CellBorder).Padding(3).AlignRight();
                                if (bold)
                                {
                                    leftCell.Text(left).Bold();
                                    rightCell.Text(right).Bold();
                                }
                                else
                                {
                                    leftCell.Text(left);
                                    rightCell.Text(right);
                                }
                            }
                        });

                        table.Cell().Element(CellBorder).MinHeight(128).Padding(3).Column(x =>
                        {
                            x.Item().Text("Our Bank Details").Bold();
                            if (!string.IsNullOrWhiteSpace(bankBeneficiary)) x.Item().Text($"Beneficiary name - {bankBeneficiary}");
                            x.Item().Height(6);
                            if (!string.IsNullOrWhiteSpace(bankAccountNumber)) x.Item().Text($"Bank Account No: {bankAccountNumber}");
                            x.Item().Height(6);
                            if (!string.IsNullOrWhiteSpace(bankAccountType)) x.Item().Text($"Type Of Account: {bankAccountType}");
                            x.Item().Height(6);
                            if (!string.IsNullOrWhiteSpace(bankName)) x.Item().Text($"Bank Name: {bankName}");
                            x.Item().Height(6);
                            if (!string.IsNullOrWhiteSpace(bankBranch)) x.Item().Text($"Branch: {bankBranch}");
                            x.Item().Height(6);
                            if (!string.IsNullOrWhiteSpace(bankIfsc)) x.Item().Text($"RTGS/ NEFT IFSC Code : {bankIfsc}");
                        });

                        table.Cell().Element(CellBorder).MinHeight(128).Padding(3).Column(x =>
                        {
                            x.Item().PaddingTop(12).AlignCenter().Text($"for {companyName}").Bold();
                            x.Item().Height(52);
                            x.Item().AlignCenter().Text("Authorised Signatory").Bold();
                        });
                    });

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cols => cols.RelativeColumn());
                        table.Cell().Element(CellBorder).MinHeight(44).Padding(3).Text(footerNote);
                    });
                });

                static IContainer CellBorder(IContainer container) =>
                    container.Border(1).BorderColor(Colors.Black);
            });
        });

        return doc.GeneratePdf();
    }

    private static byte[] GenerateTemplate(Invoice invoice, Company company, bool gstMode, bool premium, bool corporate)
    {
        var accent = NormalizeColor(company.PrimaryColor, corporate ? "6D28D9" : premium ? Colors.Amber.Darken2 : Colors.Blue.Darken2);
        var headerColor = NormalizeColor(company.TableHeaderColor, corporate ? "6D28D9" : accent);
        var lightBox = corporate ? "F5F3FF" : premium ? "FFF7ED" : "F8FAFC";
        var symbol = InvoiceMoney.GetCurrencySymbol(company);
        var subtotal = invoice.Subtotal;
        var taxAmount = InvoiceMoney.GetTaxAmount(invoice, company);
        var grandTotal = InvoiceMoney.GetGrandTotal(invoice, company);
        var col1 = company.CustomColumn1Name ?? "Item";
        var col2 = company.CustomColumn2Name ?? "Qty";
        var col3 = company.CustomColumn3Name ?? "Unit Price";
        var col4 = company.CustomColumn4Name ?? "Total";
        var fontSize = company.PdfFontSize <= 0 ? 11 : company.PdfFontSize;
        var defaultTerms = string.IsNullOrWhiteSpace(company.TermsAndConditions) ? "Full payment is due upon receipt of this invoice." : company.TermsAndConditions!;
        var terms = invoice.Status == InvoiceStatus.Paid
            ? "Payment received successfully. This invoice has been settled in full."
            : invoice.Status == InvoiceStatus.Overdue
                ? "Payment is overdue. Kindly arrange payment at the earliest."
                : defaultTerms;
        var thankYou = invoice.Status == InvoiceStatus.Paid
            ? "Thank you. Payment has been received."
            : string.IsNullOrWhiteSpace(company.ThankYouNote) ? "Thanks for your business." : company.ThankYouNote!;
        var signatureLabel = string.IsNullOrWhiteSpace(company.SignatureLabel) ? "Authorized Signature" : company.SignatureLabel!;
        var showHsn = gstMode || company.ShowHsnSac;
        var canShowQr = invoice.Status != InvoiceStatus.Paid && !string.IsNullOrWhiteSpace(invoice.PaymentLink);
        var qrBytes = canShowQr ? QrCodeService.GeneratePng(invoice.PaymentLink!) : null;

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(28);
                page.Size(PageSizes.A4);
                page.DefaultTextStyle(x => x.FontSize(fontSize));

                var logoWidth = company.LogoWidth is > 0 ? Math.Clamp(company.LogoWidth.Value, 60, 320) : 160;
                var logoHeight = company.LogoHeight is > 0 ? Math.Clamp(company.LogoHeight.Value, 20, 180) : (int?)null;
                var logoFitMode = (company.LogoFitMode ?? "contain").Trim().ToLowerInvariant();

                page.Header().Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        if (company.LogoBytes != null && company.LogoBytes.Length > 0)
                        {
                            var logoItem = col.Item().Width(logoWidth);
                            if (logoHeight.HasValue)
                                logoItem = logoItem.Height(logoHeight.Value);
                            if (logoFitMode == "fit")
                                logoItem.Image(company.LogoBytes).FitArea();
                            else
                                logoItem.Image(company.LogoBytes).FitWidth();
                        }

                        col.Item().PaddingTop(5).Text(company.Name).FontSize(corporate ? 24 : premium ? 20 : 18).Bold().FontColor(accent);
                        col.Item().Text("Invoice").FontSize(14).FontColor(corporate ? accent : premium ? Colors.Amber.Lighten3 : Colors.Grey.Darken2);
                    });

                    row.ConstantItem(220).AlignRight().Column(col =>
                    {
                        col.Item().Text($"Invoice #: {invoice.InvoiceNumber}").Bold();
                        col.Item().Text($"Issue: {invoice.IssueDate:yyyy-MM-dd}");
                        col.Item().Text($"Due: {invoice.DueDate:yyyy-MM-dd}");
                        col.Item().Text($"Status: {invoice.Status}");
                    });
                });

                page.Content().PaddingTop(16).Column(col =>
                {
                    if (corporate)
                    {
                        col.Item().Row(r =>
                        {
                            r.RelativeItem().Background(lightBox).Padding(12).Border(1).BorderColor(Colors.Grey.Lighten2).Column(box =>
                            {
                                box.Item().Text("Billed By").FontColor(accent).FontSize(16);
                                box.Item().PaddingTop(3).Text(company.Name).Bold();
                                var companyAddr = string.Join("\n", new[]
                                {
                                    company.Country,
                                    string.Join(", ", new[] { company.City, company.AddressLine1 }.Where(s => !string.IsNullOrWhiteSpace(s))),
                                    !string.IsNullOrWhiteSpace(company.EmailFromAddress) ? $"Email: {company.EmailFromAddress}" : null
                                }.Where(s => !string.IsNullOrWhiteSpace(s)));
                                if (!string.IsNullOrWhiteSpace(companyAddr)) box.Item().Text(companyAddr);
                            });

                            r.ConstantItem(12);

                            r.RelativeItem().Background(lightBox).Padding(12).Border(1).BorderColor(Colors.Grey.Lighten2).Column(box =>
                            {
                                box.Item().Text("Billed To").FontColor(accent).FontSize(16);
                                box.Item().PaddingTop(3).Text(invoice.Client?.Name ?? "Client").Bold();
                                var clientAddr = string.Join("\n", new[]
                                {
                                    invoice.Client?.Country,
                                    string.Join(", ", new[] { invoice.Client?.City, invoice.Client?.AddressLine1 }.Where(s => !string.IsNullOrWhiteSpace(s))),
                                    !string.IsNullOrWhiteSpace(invoice.Client?.Email) ? $"Email: {invoice.Client!.Email}" : null
                                }.Where(s => !string.IsNullOrWhiteSpace(s)));
                                if (!string.IsNullOrWhiteSpace(clientAddr)) box.Item().Text(clientAddr);
                            });
                        });
                    }
                    else
                    {
                        col.Item().Text($"Bill To: {invoice.Client?.Name}").Bold();
                        if (!string.IsNullOrWhiteSpace(invoice.Client?.Email)) col.Item().Text($"Email: {invoice.Client!.Email}");
                        var clientAddr = string.Join("\n", new[]
                        {
                            invoice.Client?.AddressLine1,
                            invoice.Client?.AddressLine2,
                            string.Join(", ", new[] { invoice.Client?.City, invoice.Client?.Country }.Where(s => !string.IsNullOrWhiteSpace(s)))
                        }.Where(s => !string.IsNullOrWhiteSpace(s)));
                        if (!string.IsNullOrWhiteSpace(clientAddr)) col.Item().Text(clientAddr);
                    }

                    col.Item().PaddingTop(14).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            if (showHsn) columns.RelativeColumn(2);
                            columns.RelativeColumn(6);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                        });

                        table.Header(header =>
                        {
                            if (showHsn)
                                header.Cell().Element(HeaderCell).Text("HSN/SAC").FontColor(Colors.White).Bold();
                            header.Cell().Element(HeaderCell).Text(col1).FontColor(Colors.White).Bold();
                            header.Cell().Element(HeaderCell).AlignRight().Text(col2).FontColor(Colors.White).Bold();
                            header.Cell().Element(HeaderCell).AlignRight().Text(col3).FontColor(Colors.White).Bold();
                            header.Cell().Element(HeaderCell).AlignRight().Text(col4).FontColor(Colors.White).Bold();
                        });

                        foreach (var item in invoice.Items)
                        {
                            if (showHsn)
                                table.Cell().Element(Cell).Text(string.IsNullOrWhiteSpace(item.HsnSac) ? "9983" : item.HsnSac);
                            table.Cell().Element(Cell).Text(item.Description);
                            table.Cell().Element(Cell).AlignRight().Text(item.Quantity.ToString());
                            table.Cell().Element(Cell).AlignRight().Text($"{symbol}{item.UnitPrice:0.00}");
                            table.Cell().Element(Cell).AlignRight().Text($"{symbol}{item.LineTotal:0.00}");
                        }

                        IContainer HeaderCell(IContainer c) => c.Background(headerColor).Padding(7);
                        IContainer Cell(IContainer c) => c.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(7);
                    });

                    col.Item().PaddingTop(12).AlignRight().Width(280).Column(t =>
                    {
                        t.Item().Row(r =>
                        {
                            r.RelativeItem().Text("Subtotal");
                            r.ConstantItem(130).AlignRight().Text($"{symbol}{subtotal:0.00}");
                        });

                        if (company.TaxEnabled && company.TaxRate > 0)
                        {
                            if ((gstMode || company.TaxLabel?.ToUpperInvariant() == "GST") && company.ShowSgst && company.ShowCgst)
                            {
                                var half = Math.Round(taxAmount / 2m, 2, MidpointRounding.AwayFromZero);
                                t.Item().Row(r => { r.RelativeItem().Text("SGST"); r.ConstantItem(130).AlignRight().Text($"{symbol}{half:0.00}"); });
                                t.Item().Row(r => { r.RelativeItem().Text("CGST"); r.ConstantItem(130).AlignRight().Text($"{symbol}{half:0.00}"); });
                            }
                            else
                            {
                                t.Item().Row(r =>
                                {
                                    r.RelativeItem().Text($"{company.TaxLabel} ({company.TaxRate:0.##}%)");
                                    r.ConstantItem(130).AlignRight().Text($"{symbol}{taxAmount:0.00}");
                                });
                            }
                        }

                        t.Item().PaddingTop(4).Row(r =>
                        {
                            r.RelativeItem().Text(corporate ? $"Total ({(company.DefaultCurrency ?? "").ToUpperInvariant()})" : "Total").Bold();
                            r.ConstantItem(130).AlignRight().Text($"{symbol}{grandTotal:0.00}").Bold();
                        });
                    });

                    if (invoice.Status != InvoiceStatus.Paid && company.ShowPaymentDetails && !string.IsNullOrWhiteSpace(company.PaymentDetails))
                    {
                        col.Item().PaddingTop(14).Text("Payment Details").Bold();
                        col.Item().Text(company.PaymentDetails);
                    }

                    if (invoice.Status == InvoiceStatus.Paid)
                    {
                        var paymentSummary = ExtractPaymentSummary(invoice.Notes);
                        if (!string.IsNullOrWhiteSpace(paymentSummary))
                        {
                            col.Item().PaddingTop(12).Background(Colors.Green.Lighten5).Border(1).BorderColor(Colors.Green.Lighten2).Padding(10).Column(x =>
                            {
                                x.Item().Text("Payment Slip / Payment Summary").Bold().FontColor(Colors.Green.Darken2);
                                x.Item().PaddingTop(4).Text(paymentSummary);
                            });
                        }
                    }

                    if (company.ShowTerms)
                    {
                        col.Item().PaddingTop(12).Text("Terms & Conditions").Bold();
                        col.Item().Text(terms);
                    }

                    if (company.ShowNotes && !string.IsNullOrWhiteSpace(invoice.Notes))
                    {
                        var cleanNotes = RemovePaymentSummary(invoice.Notes);
                        if (!string.IsNullOrWhiteSpace(cleanNotes))
                            col.Item().PaddingTop(10).Text($"Notes: {cleanNotes}");
                    }

                    if (qrBytes != null)
                    {
                        col.Item().PaddingTop(18).AlignRight().Width(220).Row(r =>
                        {
                            r.RelativeItem().AlignMiddle().Text("Scan to pay").SemiBold();
                            r.ConstantItem(84).Height(84).Image(qrBytes);
                        });
                    }

                    if (company.ShowSignature)
                    {
                        col.Item().PaddingTop(26).AlignRight().Width(180).Column(sig =>
                        {
                            sig.Item().Height(20);
                            sig.Item().LineHorizontal(1).LineColor(Colors.Grey.Darken2);
                            sig.Item().PaddingTop(4).AlignCenter().Text(signatureLabel).Bold();
                            if (!string.IsNullOrWhiteSpace(company.SignatureName))
                                sig.Item().AlignCenter().Text(company.SignatureName);
                        });
                    }

                    col.Item().PaddingTop(10).Text(thankYou).Italic();
                });

                page.Footer().AlignCenter().Text($"{company.Name} • Generated with Invoxa • {company.TemplateDisplayName ?? company.InvoiceTemplateKey}")
                    .FontSize(9).FontColor(premium ? Colors.Amber.Darken2 : Colors.Grey.Darken1);
            });
        });

        return doc.GeneratePdf();
    }

    private static string ExtractPaymentSummary(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return string.Empty;

        const string prefix = "---- PAYMENT SUMMARY ----";
        var markerIndex = notes.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
            return string.Empty;

        return notes[(markerIndex + prefix.Length)..].Trim();
    }

    private static string RemovePaymentSummary(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return string.Empty;

        const string prefix = "---- PAYMENT SUMMARY ----";
        var markerIndex = notes.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
            return notes.Trim();

        return notes[..markerIndex].TrimEnd();
    }

    private static string NormalizeColor(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var text = value.Trim().TrimStart('#').ToUpperInvariant();
        return text.Length == 6 ? text : fallback;
    }


    private static string FirstValue(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value!.Trim();
        }
        return string.Empty;
    }

    private static string JoinComma(params string?[] values)
        => string.Join(", ", values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()));

    private static string BuildItemDescription(Invoice invoice)
    {
        if (invoice.Items == null || invoice.Items.Count == 0)
            return "-";

        return string.Join("\n", invoice.Items.Select(i =>
        {
            var parts = new List<string> { i.Description };
            if (!string.IsNullOrWhiteSpace(i.HsnSac))
                parts.Add($"HSN: {i.HsnSac}");
            return string.Join(" ", parts);
        }));
    }

    private static string FormatDecimal(decimal value)
        => value == decimal.Truncate(value) ? decimal.Truncate(value).ToString("0") : value.ToString("0.##");

    private static string FormatMoneyPlain(decimal value)
        => value.ToString("0.00");

    private static string NumberToWords(long number)
    {
        if (number == 0) return "Zero";
        if (number < 0) return "Minus " + NumberToWords(Math.Abs(number));

        string[] unitsMap = { "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten",
            "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen" };
        string[] tensMap = { "Zero", "Ten", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety" };

        string ConvertBelowThousand(long n)
        {
            var parts = new List<string>();
            if (n >= 100)
            {
                parts.Add(unitsMap[n / 100]);
                parts.Add("Hundred");
                n %= 100;
            }

            if (n >= 20)
            {
                parts.Add(tensMap[n / 10]);
                if ((n % 10) > 0)
                    parts.Add(unitsMap[n % 10]);
            }
            else if (n > 0)
            {
                parts.Add(unitsMap[n]);
            }

            return string.Join(" ", parts);
        }

        var segments = new List<string>();

        void AppendSegment(long divisor, string label)
        {
            if (number >= divisor)
            {
                var part = number / divisor;
                segments.Add(ConvertBelowThousand(part));
                segments.Add(label);
                number %= divisor;
            }
        }

        AppendSegment(10000000, "Crore");
        AppendSegment(100000, "Lakh");
        AppendSegment(1000, "Thousand");

        if (number > 0)
            segments.Add(ConvertBelowThousand(number));

        return string.Join(" ", segments.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

}
