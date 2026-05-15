namespace COCOBOLOERPNEW.DTOs;

// ============================
// CashBox - List
// ============================
public class CashBoxListDto
{
    public int CashBoxId { get; set; }
    public string CashBoxName { get; set; } = "";
    public string? Description { get; set; }
    public string? CashBoxKind { get; set; }     // Cash / Bank / Wallet / Other
    public string? KindAr => CashBoxKinds.All.GetValueOrDefault(CashBoxKind ?? "Other", "أخرى");
    public string? Icon { get; set; }
    public string? Color { get; set; }
    public decimal OpeningBalance { get; set; }
    public DateTime? OpeningDate { get; set; }
    public bool IsActive { get; set; }
    public bool IsDefault { get; set; }

    // محسوب
    public decimal CurrentBalance { get; set; }
    public decimal TotalIn { get; set; }
    public decimal TotalOut { get; set; }
    public int TransactionsCount { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime? CreatedAt { get; set; }
}

// ============================
// CashBox - Form
// ============================
public class CashBoxFormDto
{
    public int CashBoxId { get; set; }
    public string CashBoxName { get; set; } = "";
    public string? Description { get; set; }
    public string CashBoxKind { get; set; } = "Cash";
    public string? Icon { get; set; }
    public string? Color { get; set; }
    public decimal OpeningBalance { get; set; }
    public DateTime? OpeningDate { get; set; } = DateTime.Today;
    public bool IsActive { get; set; } = true;
    public bool IsDefault { get; set; }
}

// ============================
// CashBox Transaction
// ============================
public class CashBoxTransactionDto
{
    public int CashboxTransactionId { get; set; }
    public int CashBoxId { get; set; }
    public string CashBoxName { get; set; } = "";
    public string? CashBoxColor { get; set; }
    public DateTime TransactionDate { get; set; }
    public string TransactionType { get; set; } = "";  // In / Out
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? CreatedAt { get; set; }

    // الربط بالمصدر
    public int? PaymentId { get; set; }
    public int? ReferenceId { get; set; }
    public string? ReferenceType { get; set; }
    public string? ReferenceTypeAr { get; set; }
    public string? ReferenceColor { get; set; }
    public string? SourceTitle { get; set; }
    public string? SourceUrl { get; set; }
    public string? PartyName { get; set; }
    public string? PersonalAccountName { get; set; }
}

public class CashBoxTransactionFilterDto
{
    public string? SearchText { get; set; }
    public int? CashBoxId { get; set; }
    public string? TransactionType { get; set; }   // In / Out / All
    public string? ReferenceType { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public decimal? AmountFrom { get; set; }
    public decimal? AmountTo { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public string SortBy { get; set; } = "TransactionDate";
    public bool SortDescending { get; set; } = true;
}

// ============================
// Manual Operation Form
// ============================
public class CashBoxManualFormDto
{
    public string OperationType { get; set; } = ManualOperationTypes.ManualReceipt;
    public DateTime TransactionDate { get; set; } = DateTime.Now;
    public int? CashBoxId { get; set; }
    public int? ToCashBoxId { get; set; }
    public int? PersonalAccountId { get; set; }
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
    public string? Recipient { get; set; }
}

// ============================
// Dashboard
// ============================
public class CashBoxDashboardDto
{
    public decimal TotalBalance { get; set; }
    public decimal TodayIn { get; set; }
    public decimal TodayOut { get; set; }
    public decimal TodayNet => TodayIn - TodayOut;

    public decimal MonthIn { get; set; }
    public decimal MonthOut { get; set; }
    public decimal MonthNet => MonthIn - MonthOut;

    public int CashBoxesCount { get; set; }
    public int ActiveCashBoxesCount { get; set; }

    public List<CashBoxListDto> CashBoxes { get; set; } = new();
    public List<DailyMovementDto> Last30Days { get; set; } = new();
    public List<TypeBreakdownDto> TypeBreakdown { get; set; } = new();
    public List<RecentTransactionDto> RecentTransactions { get; set; } = new();

    public decimal TotalCreditors { get; set; }
    public decimal TotalDebtors { get; set; }
    public List<PersonalAccountSummaryDto> TopPersonalAccounts { get; set; } = new();
}

public class DailyMovementDto
{
    public DateTime Date { get; set; }
    public decimal In { get; set; }
    public decimal Out { get; set; }
    public decimal Net => In - Out;
}

public class TypeBreakdownDto
{
    public string ReferenceType { get; set; } = "";
    public string ReferenceTypeAr { get; set; } = "";
    public decimal Total { get; set; }
    public int Count { get; set; }
    public string? Color { get; set; }
}

public class RecentTransactionDto
{
    public int TransactionId { get; set; }
    public DateTime TransactionDate { get; set; }
    public string CashBoxName { get; set; } = "";
    public string TransactionType { get; set; } = "";
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public string? ReferenceType { get; set; }
    public string? ReferenceTypeAr => CashBoxRefTypes.All.GetValueOrDefault(ReferenceType ?? "", "-");
}

// ============================
// CashBox Summary
// ============================
public class CashBoxBalanceSummaryDto
{
    public int CashBoxId { get; set; }
    public string CashBoxName { get; set; } = "";
    public string? CashBoxKind { get; set; }
    public string? Color { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal TotalIn { get; set; }
    public decimal TotalOut { get; set; }
    public decimal CurrentBalance => OpeningBalance + TotalIn - TotalOut;
    public int InCount { get; set; }
    public int OutCount { get; set; }
    public bool IsActive { get; set; }
}

// ============================
// ثوابت
// ============================
public static class CashBoxKinds
{
    public const string Cash = "Cash";
    public const string Bank = "Bank";
    public const string Wallet = "Wallet";
    public const string Other = "Other";

    public static readonly Dictionary<string, string> All = new()
    {
        { Cash,   "خزينة نقدية" },
        { Bank,   "حساب بنكي" },
        { Wallet, "محفظة إلكترونية" },
        { Other,  "أخرى" }
    };

    public static readonly Dictionary<string, string> DefaultIcons = new()
    {
        { Cash,   "AttachMoney" },
        { Bank,   "AccountBalance" },
        { Wallet, "PhoneAndroid" },
        { Other,  "Wallet" }
    };

    public static readonly Dictionary<string, string> DefaultColors = new()
    {
        { Cash,   "#10b981" },
        { Bank,   "#3b82f6" },
        { Wallet, "#f59e0b" },
        { Other,  "#94a3b8" }
    };
}

public static class ManualOperationTypes
{
    public const string ManualReceipt = "ManualReceipt";
    public const string ManualPayment = "ManualPayment";
    public const string LoanIn = "LoanIn";
    public const string LoanRepayment = "LoanRepayment";
    public const string Transfer = "Transfer";
    public const string OpeningBalance = "OpeningBalance";

    public static readonly Dictionary<string, string> All = new()
    {
        { ManualReceipt,   "سند قبض يدوي" },
        { ManualPayment,   "سند صرف يدوي" },
        { LoanIn,          "قرض دخل من شخص" },
        { LoanRepayment,   "تسديد قرض لشخص" },
        { Transfer,        "تحويل بين خزن" },
        { OpeningBalance,  "رصيد افتتاحي" }
    };
}

public static class CashBoxRefTypes
{
    public const string SaleInvoice = "SaleInvoice";
    public const string PurchaseInvoice = "PurchaseInvoice";
    public const string Expense = "Expense";
    public const string Payroll = "Payroll";
    public const string Loan = "Loan";
    public const string TransferIn = "TransferIn";
    public const string TransferOut = "TransferOut";
    public const string AdvanceCharge = "Charge";
    public const string ManualReceipt = "ManualReceipt";
    public const string ManualPayment = "ManualPayment";
    public const string OpeningBalance = "OpeningBalance";

    public static readonly Dictionary<string, string> All = new()
    {
        { SaleInvoice,      "فاتورة مبيعات" },
        { PurchaseInvoice,  "فاتورة مشتريات" },
        { Expense,          "مصروف" },
        { Payroll,          "راتب" },
        { Loan,             "قرض" },
        { TransferIn,       "تحويل وارد" },
        { TransferOut,      "تحويل صادر" },
        { AdvanceCharge,    "رسوم معاينة" },
        { ManualReceipt,    "سند قبض يدوي" },
        { ManualPayment,    "سند صرف يدوي" },
        { OpeningBalance,   "رصيد افتتاحي" }
    };

    public static readonly Dictionary<string, string> Colors = new()
    {
        { SaleInvoice,      "#10b981" },
        { PurchaseInvoice,  "#ef4444" },
        { Expense,          "#f59e0b" },
        { Payroll,          "#8b5cf6" },
        { Loan,             "#3b82f6" },
        { TransferIn,       "#06b6d4" },
        { TransferOut,      "#06b6d4" },
        { AdvanceCharge,    "#84cc16" },
        { ManualReceipt,    "#22c55e" },
        { ManualPayment,    "#dc2626" },
        { OpeningBalance,   "#6366f1" }
    };

    public static readonly Dictionary<string, string> Icons = new()
    {
        { SaleInvoice,      "Receipt" },
        { PurchaseInvoice,  "ShoppingCart" },
        { Expense,          "MoneyOff" },
        { Payroll,          "Payments" },
        { Loan,             "AccountBalance" },
        { TransferIn,       "CallReceived" },
        { TransferOut,      "CallMade" },
        { AdvanceCharge,    "Savings" },
        { ManualReceipt,    "AddCard" },
        { ManualPayment,    "RemoveCircle" },
        { OpeningBalance,   "Stars" }
    };
}
