using Invoxa.Web.Data;
using Invoxa.Web.Domain;
using Invoxa.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Invoxa.Web.Pages.Settings;

public class AllowyFarmerModel : PageModel
{
    private readonly AppDbContext _db;

    public AllowyFarmerModel(AppDbContext db)
    {
        _db = db;
    }

    [BindProperty] public string CompanyName { get; set; } = "Allowyfarmer";
    [BindProperty] public string? AddressLine1 { get; set; }
    [BindProperty] public string? AddressLine2 { get; set; }
    [BindProperty] public string? CityLine { get; set; }
    [BindProperty] public string? Website { get; set; }
    [BindProperty] public string? Phone { get; set; }
    [BindProperty] public string? GstNumber { get; set; }
    [BindProperty] public string? InvoicePrefixText { get; set; }
    [BindProperty] public string? DefaultDeliveryNote { get; set; }
    [BindProperty] public string? DefaultModeTerms { get; set; }
    [BindProperty] public string? DefaultHsnCode { get; set; }
    [BindProperty] public string? BankBeneficiary { get; set; }
    [BindProperty] public string? BankAccountNumber { get; set; }
    [BindProperty] public string? BankAccountType { get; set; }
    [BindProperty] public string? BankName { get; set; }
    [BindProperty] public string? BankBranch { get; set; }
    [BindProperty] public string? BankIfscCode { get; set; }
    [BindProperty] public string? FooterNote { get; set; }

    public string? Message { get; set; }

    public async Task OnGetAsync()
    {
        var company = await GetCompanyAsync();
        Load(company);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var company = await GetCompanyAsync();

        company.AllowyFarmerCompanyName = Clean(CompanyName) ?? "Allowyfarmer";
        company.AllowyFarmerAddressLine1 = Clean(AddressLine1);
        company.AllowyFarmerAddressLine2 = Clean(AddressLine2);
        company.AllowyFarmerCityLine = Clean(CityLine);
        company.AllowyFarmerWebsite = Clean(Website);
        company.AllowyFarmerPhone = Clean(Phone);
        company.AllowyFarmerGstNumber = Clean(GstNumber);
        company.AllowyFarmerInvoicePrefix = Clean(InvoicePrefixText) ?? "Invoice No.";
        company.AllowyFarmerDefaultDeliveryNote = Clean(DefaultDeliveryNote) ?? "NA";
        company.AllowyFarmerDefaultModeTerms = Clean(DefaultModeTerms) ?? "As per terms and conditions";
        company.AllowyFarmerDefaultHsnCode = Clean(DefaultHsnCode) ?? "94029090";
        company.AllowyFarmerBankBeneficiary = Clean(BankBeneficiary);
        company.AllowyFarmerBankAccountNumber = Clean(BankAccountNumber);
        company.AllowyFarmerBankAccountType = Clean(BankAccountType);
        company.AllowyFarmerBankName = Clean(BankName);
        company.AllowyFarmerBankBranch = Clean(BankBranch);
        company.AllowyFarmerBankIfscCode = Clean(BankIfscCode);
        company.AllowyFarmerFooterNote = Clean(FooterNote);

        company.InvoiceTemplateKey = "alloy_farmer";
        company.TemplateDisplayName = "Allowyfarmer";

        await _db.SaveChangesAsync();

        Load(company);
        Message = "Allowyfarmer settings saved.";
        return Page();
    }

    private async Task<Company> GetCompanyAsync()
    {
        var companyId = await CurrentUserContext.RequireCompanyIdAsync(HttpContext, _db);
        return await _db.Companies.FirstAsync(c => c.Id == companyId);
    }

    private void Load(Company company)
    {
        CompanyName = company.AllowyFarmerCompanyName ?? "Allowyfarmer";
        AddressLine1 = company.AllowyFarmerAddressLine1;
        AddressLine2 = company.AllowyFarmerAddressLine2;
        CityLine = company.AllowyFarmerCityLine;
        Website = company.AllowyFarmerWebsite;
        Phone = company.AllowyFarmerPhone;
        GstNumber = company.AllowyFarmerGstNumber;
        InvoicePrefixText = company.AllowyFarmerInvoicePrefix ?? "Invoice No.";
        DefaultDeliveryNote = company.AllowyFarmerDefaultDeliveryNote ?? "NA";
        DefaultModeTerms = company.AllowyFarmerDefaultModeTerms ?? "As per terms and conditions";
        DefaultHsnCode = company.AllowyFarmerDefaultHsnCode ?? "94029090";
        BankBeneficiary = company.AllowyFarmerBankBeneficiary;
        BankAccountNumber = company.AllowyFarmerBankAccountNumber;
        BankAccountType = company.AllowyFarmerBankAccountType;
        BankName = company.AllowyFarmerBankName;
        BankBranch = company.AllowyFarmerBankBranch;
        BankIfscCode = company.AllowyFarmerBankIfscCode;
        FooterNote = company.AllowyFarmerFooterNote ?? "Note: Any other taxes / checkpost charges if any other than mentioned applicable will be borne by the Customer & is not included in our price";
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
