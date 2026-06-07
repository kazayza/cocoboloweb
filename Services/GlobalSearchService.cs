using Microsoft.EntityFrameworkCore;
using MudBlazor;
using COCOBOLOERPNEW.Models;
using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

// ===========================
// نموذج نتيجة البحث
// ===========================
public class SearchResult
{
    public string Title      { get; set; } = "";
    public string SubTitle   { get; set; } = "";
    public string CategoryAr { get; set; } = "";
    public string Icon       { get; set; } = "";
    public string Color      { get; set; } = "";
    public string Url        { get; set; } = "";
}

// ===========================
// السيرفس
// ===========================
public class GlobalSearchService
{
    private readonly db24804Context _db;

    public GlobalSearchService(db24804Context db) => _db = db;

    public async Task<List<SearchResult>> SearchAsync(
        string query,
        IEnumerable<string> userPermissions,
        int maxPerCategory = 5)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
            return new();

        var q     = query.Trim();
        var perms = userPermissions.ToHashSet();
        var all   = new List<SearchResult>();

        // ── 1. المنتجات ──────────────────────────────────────────
        // صلاحية: frm_ProductList:View
        // البحث في: اسم المنتج + رقمه + اسم العميل المرتبط به
        if (perms.Contains("frm_ProductList:View"))
        {
            var products = await (
                from p in _db.Products.AsNoTracking()
                join c in _db.Parties.AsNoTracking()
                    on p.Customer equals c.PartyId into pc
                from customer in pc.DefaultIfEmpty()
                where p.ProductName.Contains(q) ||
                      p.ProductId.ToString() == q ||
                      (customer != null && customer.PartyName.Contains(q))
                orderby p.ProductId descending
                select new
                {
                    p.ProductId,
                    p.ProductName,
                    p.PricingType,
                    CustomerName = customer != null ? customer.PartyName : null
                }
            ).Take(maxPerCategory).ToListAsync();

            all.AddRange(products.Select(p => new SearchResult
            {
                Title      = p.ProductName,
                SubTitle   = $"#{p.ProductId}" +
                             (p.CustomerName != null ? $" | {p.CustomerName}" : "") +
                             (p.PricingType   != null ? $" | {p.PricingType}"  : ""),
                CategoryAr = "المنتجات",
                Icon       = Icons.Material.Rounded.Inventory2,
                Color      = "#667eea",
                Url        = $"/products/form/{p.ProductId}"
            }));
        }

        // ── 2. العملاء ───────────────────────────────────────────
        // صلاحية: frm_PartiesList:View
        // البحث في: اسم العميل + تليفونه + رقمه
        if (perms.Contains("frm_PartiesList:View"))
        {
            var customers = await _db.Parties
                .AsNoTracking()
                .Where(p => p.PartyName.Contains(q) ||
                            (p.Phone  != null && p.Phone.Contains(q))  ||
                            (p.Phone2 != null && p.Phone2.Contains(q)) ||
                            p.PartyId.ToString() == q)
                .OrderByDescending(p => p.PartyId)
                .Take(maxPerCategory)
                .Select(p => new { p.PartyId, p.PartyName, p.Phone })
                .ToListAsync();

            all.AddRange(customers.Select(c => new SearchResult
            {
                Title      = c.PartyName,
                SubTitle   = c.Phone ?? $"#{c.PartyId}",
                CategoryAr = "العملاء",
                Icon       = Icons.Material.Rounded.Groups,
                Color      = "#10b981",
                Url        = $"/customers/form/{c.PartyId}"
            }));
        }

        // ── 3. فواتير المبيعات ───────────────────────────────────
        // صلاحية: frm_PartiesInvoices:View
        // البحث في: اسم العميل + رقم الفاتورة + ReferenceNumber
        if (perms.Contains("frm_PartiesInvoices:View"))
        {
            var salesInvoices = await (
                from t in _db.Transactions.AsNoTracking()
                join p in _db.Parties.AsNoTracking()
                    on t.PartyId equals p.PartyId
                where (t.TransactionType == TransactionTypes.Sale ||
                       t.TransactionType == TransactionTypes.SaleReturn) &&
                      (p.PartyName.Contains(q)                               ||
                       t.TransactionId.ToString() == q                       ||
                       (t.ReferenceNumber != null && t.ReferenceNumber.Contains(q)) ||
                       (p.Phone  != null && p.Phone.Contains(q))             ||
                       (p.Phone2 != null && p.Phone2.Contains(q)))
                orderby t.TransactionId descending
                select new
                {
                    t.TransactionId,
                    t.TransactionDate,
                    t.GrandTotal,
                    t.TransactionType,
                    t.ReferenceNumber,
                    PartyName = p.PartyName
                }
            ).Take(maxPerCategory).ToListAsync();

            all.AddRange(salesInvoices.Select(i => new SearchResult
            {
                Title      = i.PartyName,
                SubTitle   = $"فاتورة #{i.TransactionId}" +
                             (i.ReferenceNumber != null ? $" | {i.ReferenceNumber}" : "") +
                             $" | {i.TransactionDate:yyyy/MM/dd} | {i.GrandTotal:N0} ج.م",
                CategoryAr = "فواتير المبيعات",
                Icon       = Icons.Material.Rounded.Receipt,
                Color      = "#f59e0b",
                Url        = $"/sales/invoices/{i.TransactionId}"
            }));
        }

        // ── 4. فواتير المشتريات ──────────────────────────────────
        // صلاحية: frm_SupplierInvoices:View
        // البحث في: اسم المورد + رقم الفاتورة
        if (perms.Contains("frm_SupplierInvoices:View"))
        {
            var purchaseInvoices = await (
                from t in _db.Transactions.AsNoTracking()
                join p in _db.Parties.AsNoTracking()
                    on t.PartyId equals p.PartyId
                where (t.TransactionType == TransactionTypes.Purchase ||
                       t.TransactionType == TransactionTypes.PurchaseReturn) &&
                      (p.PartyName.Contains(q)                               ||
                       t.TransactionId.ToString() == q                       ||
                       (t.ReferenceNumber != null && t.ReferenceNumber.Contains(q)) ||
                       (p.Phone  != null && p.Phone.Contains(q))             ||
                       (p.Phone2 != null && p.Phone2.Contains(q)))
                orderby t.TransactionId descending
                select new
                {
                    t.TransactionId,
                    t.TransactionDate,
                    t.GrandTotal,
                    t.ReferenceNumber,
                    PartyName = p.PartyName
                }
            ).Take(maxPerCategory).ToListAsync();

            all.AddRange(purchaseInvoices.Select(i => new SearchResult
            {
                Title      = i.PartyName,
                SubTitle   = $"مشتريات #{i.TransactionId}" +
                             (i.ReferenceNumber != null ? $" | {i.ReferenceNumber}" : "") +
                             $" | {i.TransactionDate:yyyy/MM/dd} | {i.GrandTotal:N0} ج.م",
                CategoryAr = "فواتير المشتريات",
                Icon       = Icons.Material.Rounded.ShoppingCart,
                Color      = "#8b5cf6",
                Url        = $"/sales/invoices/{i.TransactionId}"
            }));
        }

        // ── 5. عروض الأسعار ──────────────────────────────────────
        // صلاحية: frmQuotationsList:View
        // البحث في: اسم العميل + ReferenceNumber + تليفون العميل
        if (perms.Contains("frmQuotationsList:View"))
        {
            var quotations = await (
                from q2 in _db.Quotations.AsNoTracking()
                join p in _db.Parties.AsNoTracking()
                    on q2.PartyId equals p.PartyId
                where p.PartyName.Contains(q)                                    ||
                      (q2.ReferenceNumber != null && q2.ReferenceNumber.Contains(q)) ||
                      q2.QuotationId.ToString() == q                             ||
                      (p.Phone  != null && p.Phone.Contains(q))                  ||
                      (p.Phone2 != null && p.Phone2.Contains(q))
                orderby q2.QuotationId descending
                select new
                {
                    q2.QuotationId,
                    q2.ReferenceNumber,
                    q2.QuotationDate,
                    q2.GrandTotal,
                    q2.Status,
                    PartyName = p.PartyName
                }
            ).Take(maxPerCategory).ToListAsync();

            all.AddRange(quotations.Select(q2 => new SearchResult
            {
                Title      = q2.PartyName,
                SubTitle   = $"{q2.ReferenceNumber ?? $"#{q2.QuotationId}"}" +
                             $" | {q2.QuotationDate:yyyy/MM/dd}" +
                             $" | {q2.GrandTotal:N0} ج.م" +
                             $" | {GetQuotationStatusAr(q2.Status)}",
                CategoryAr = "عروض الأسعار",
                Icon       = Icons.Material.Rounded.RequestQuote,
                Color      = "#06b6d4",
                Url        = $"/quotations/{q2.QuotationId}"
            }));
        }

        // ── 6. الموظفين ──────────────────────────────────────────
        // صلاحية: frm_Employeeslist:View
        // البحث في: الاسم + تليفون + رقم قومي
        if (perms.Contains("frm_Employeeslist:View"))
        {
            var employees = await _db.Employees
                .AsNoTracking()
                .Where(e => e.FullName.Contains(q)                               ||
                            (e.MobilePhone  != null && e.MobilePhone.Contains(q))  ||
                            (e.MobilePhone2 != null && e.MobilePhone2.Contains(q)) ||
                            (e.NationalId   != null && e.NationalId.Contains(q))   ||
                            e.EmployeeId.ToString() == q)
                .OrderByDescending(e => e.EmployeeId)
                .Take(maxPerCategory)
                .Select(e => new { e.EmployeeId, e.FullName, e.JobTitle, e.MobilePhone })
                .ToListAsync();

            all.AddRange(employees.Select(e => new SearchResult
            {
                Title      = e.FullName,
                SubTitle   = (e.JobTitle ?? "") +
                             (e.MobilePhone != null ? $" | {e.MobilePhone}" : "") +
                             $" | #{e.EmployeeId}",
                CategoryAr = "الموظفين",
                Icon       = Icons.Material.Rounded.Badge,
                Color      = "#8b5cf6",
                Url        = $"/employees/{e.EmployeeId}"
            }));
        }

        // ── 7. الشكاوى ───────────────────────────────────────────
        // صلاحية: frm_Complaints_Main:View
        // البحث في: الموضوع + اسم العميل + تليفونه + رقم الفاتورة المرتبطة
        if (perms.Contains("frm_Complaints_Main:View"))
        {
            var complaints = await (
                from c in _db.Complaints.AsNoTracking()
                join p in _db.Parties.AsNoTracking()
                    on c.PartyId equals p.PartyId
                join t in _db.Transactions.AsNoTracking()
                    on c.TransactionId equals t.TransactionId into ct
                from trans in ct.DefaultIfEmpty()
                where c.Subject.Contains(q)                                       ||
                      c.ComplaintId.ToString() == q                               ||
                      p.PartyName.Contains(q)                                     ||
                      (p.Phone  != null && p.Phone.Contains(q))                   ||
                      (p.Phone2 != null && p.Phone2.Contains(q))                  ||
                      (trans != null && trans.TransactionId.ToString() == q)      ||
                      (trans != null && trans.ReferenceNumber != null &&
                       trans.ReferenceNumber.Contains(q))
                orderby c.ComplaintId descending
                select new
                {
                    c.ComplaintId,
                    c.Subject,
                    c.CreatedAt,
                    PartyName     = p.PartyName,
                    Phone         = p.Phone,
                    TransactionId = trans != null ? (int?)trans.TransactionId : null
                }
            ).Take(maxPerCategory).ToListAsync();

            all.AddRange(complaints.Select(c => new SearchResult
            {
                Title      = c.PartyName,
                SubTitle   = $"#{c.ComplaintId} | {c.Subject}" +
                             (c.TransactionId.HasValue ? $" | فاتورة #{c.TransactionId}" : "") +
                             $" | {c.CreatedAt:yyyy/MM/dd}",
                CategoryAr = "الشكاوى",
                Icon       = Icons.Material.Rounded.SupportAgent,
                Color      = "#ef4444",
                Url        = $"/complaints/{c.ComplaintId}"
            }));
        }

        // ── 8. المصروفات ─────────────────────────────────────────
        // صلاحية: frm_ExpensesList:View
        // البحث في: اسم المصروف + ملاحظاته
        if (perms.Contains("frm_ExpensesList:View"))
        {
            var expenses = await _db.Expenses
                .AsNoTracking()
                .Where(e => e.ExpenseName.Contains(q)                   ||
                            (e.Notes != null && e.Notes.Contains(q)) ||
                            e.ExpenseId.ToString() == q)
                .OrderByDescending(e => e.ExpenseId)
                .Take(maxPerCategory)
                .Select(e => new { e.ExpenseId, e.ExpenseName, e.Amount, e.ExpenseDate })
                .ToListAsync();

            all.AddRange(expenses.Select(e => new SearchResult
            {
                Title      = e.ExpenseName,
                SubTitle   = $"{e.ExpenseDate:yyyy/MM/dd} | {e.Amount:N0} ج.م",
                CategoryAr = "المصروفات",
                Icon       = Icons.Material.Rounded.AccountBalanceWallet,
                Color      = "#f97316",
                Url        = "/expenses"
            }));
        }

        return all;
    }

    // ── Helper ───────────────────────────────────────────────────
    private static string GetQuotationStatusAr(string status) => status switch
    {
        "Draft"     => "مسودة",
        "Sent"      => "مرسل",
        "Accepted"  => "مقبول",
        "Rejected"  => "مرفوض",
        "Expired"   => "منتهي",
        "Converted" => "محوّل لفاتورة",
        _           => status
    };
}
