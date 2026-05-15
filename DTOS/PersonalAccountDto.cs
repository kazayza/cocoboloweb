namespace COCOBOLOERPNEW.DTOs;

public class PersonalAccountListDto
{
    public int PersonalAccountId { get; set; }
    public string AccountName { get; set; } = "";
    public string AccountType { get; set; } = "";
    public string AccountTypeAr => PersonalAccountTypes.All.GetValueOrDefault(AccountType, AccountType);
    public string? AccountTypeIcon => PersonalAccountTypes.Icons.GetValueOrDefault(AccountType);
    public string? AccountTypeColor => PersonalAccountTypes.Colors.GetValueOrDefault(AccountType);
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? NationalId { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; }

    public decimal OpeningBalance { get; set; }
    public string OpeningType { get; set; } = "Credit";
    public decimal TotalIn { get; set; }
    public decimal TotalOut { get; set; }
    public decimal CurrentBalance { get; set; }

    public string BalanceStatus =>
        CurrentBalance > 0 ? "له على الشركة" :
        CurrentBalance < 0 ? "عليه للشركة" : "مسوى";

    public int TransactionsCount { get; set; }
    public DateTime? LastTransactionDate { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? CreatedAt { get; set; }
}

public class PersonalAccountFormDto
{
    public int PersonalAccountId { get; set; }
    public string AccountName { get; set; } = "";
    public string AccountType { get; set; } = PersonalAccountTypes.Other;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? NationalId { get; set; }
    public string? Notes { get; set; }
    public decimal OpeningBalance { get; set; }
    public DateTime? OpeningDate { get; set; } = DateTime.Today;
    public string OpeningType { get; set; } = "Credit";
    public bool IsActive { get; set; } = true;
}

public class PersonalAccountFilterDto
{
    public string? SearchText { get; set; }
    public string? AccountType { get; set; }
    public bool? IsActive { get; set; }
    public string? BalanceFilter { get; set; }   // Positive / Negative / Zero / All
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public class PersonalAccountStatementDto
{
    public PersonalAccountListDto Account { get; set; } = new();
    public List<PersonalAccountTransactionDto> Transactions { get; set; } = new();
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public decimal OpeningBalanceAtStart { get; set; }
    public decimal ClosingBalanceAtEnd { get; set; }
}

public class PersonalAccountTransactionDto
{
    public int CashboxTransactionId { get; set; }
    public DateTime TransactionDate { get; set; }
    public string Description { get; set; } = "";
    public decimal AmountIn { get; set; }
    public decimal AmountOut { get; set; }
    public decimal RunningBalance { get; set; }
    public string? CashBoxName { get; set; }
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
}

public class PersonalAccountSummaryDto
{
    public int PersonalAccountId { get; set; }
    public string AccountName { get; set; } = "";
    public string AccountType { get; set; } = "";
    public string AccountTypeAr => PersonalAccountTypes.All.GetValueOrDefault(AccountType, AccountType);
    public decimal CurrentBalance { get; set; }
    public string BalanceStatus =>
        CurrentBalance > 0 ? "له" :
        CurrentBalance < 0 ? "عليه" : "مسوى";
}

public class PersonalAccountTransactionFormDto
{
    public int PersonalAccountId { get; set; }
    public string OperationType { get; set; } = "LoanIn";
    public DateTime TransactionDate { get; set; } = DateTime.Now;
    public int? CashBoxId { get; set; }
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
}

public static class PersonalAccountTypes
{
    public const string Owner = "Owner";
    public const string Investor = "Investor";
    public const string Lender = "Lender";
    public const string Employee = "Employee";
    public const string Other = "Other";

    public static readonly Dictionary<string, string> All = new()
    {
        { Owner,    "صاحب الشركة / مالك" },
        { Investor, "مستثمر / شريك" },
        { Lender,   "مقرض" },
        { Employee, "موظف (سلفة)" },
        { Other,    "أخرى" }
    };

    public static readonly Dictionary<string, string> Icons = new()
    {
        { Owner,    "Stars" },
        { Investor, "Diamond" },
        { Lender,   "AccountBalance" },
        { Employee, "Person" },
        { Other,    "PersonOutline" }
    };

    public static readonly Dictionary<string, string> Colors = new()
    {
        { Owner,    "#d4af37" },
        { Investor, "#8b5cf6" },
        { Lender,   "#3b82f6" },
        { Employee, "#10b981" },
        { Other,    "#6b7280" }
    };
}
