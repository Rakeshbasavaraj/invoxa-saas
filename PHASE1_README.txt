Phase 1 updates included:
- workspace/company isolation by logged-in user
- landing page + pricing on home when logged out
- production appsettings template
- client delete button with safe guard (blocks delete if invoices exist)
- minimum Super Admin panel for seeded owner account

Super Admin demo login:
- Email: admin@invoxa.local
- Password: Admin@123

Super Admin pages:
- /Owner/Index
- /Owner/Companies
- /Owner/Users
- /Owner/Invoices

Note:
- Newly registered users are normal company admins.
- Only the seeded demo owner account is SuperAdmin.


Phase 1.1 added: plan selection on signup, company approval by Super Admin, login blocked until approved, and plan limits for clients/invoices.


V7 fixes:
- Professional plan-limit panels added on Create Invoice and Add Client pages
- Public invoice page now shows a clear payment-status message if Stripe is not configured
- Stripe payment link service now fails gracefully and supports 3-decimal currencies like KWD
- Manual due reminder emails now use a richer HTML design with Pay Now + View Invoice links
- Automation reminder flow is more resilient and logs per-invoice failures instead of stopping the whole run
- Automation duplicate-check is now once per day instead of a rolling 20-hour window


PUBLIC INVOICE PAGE INCLUDED
- Open any invoice public page at /i/{token}
- From invoice list, use the link icon to open the public invoice page
- Public page is read-only
- Client can download PDF
- Client can pay online using Stripe payment link
- After successful payment, invoice shows Paid and pay button is hidden
