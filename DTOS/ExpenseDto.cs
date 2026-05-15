namespace COCOBOLOERPNEW.DTOs;

public class ExpenseListDto
{
    public int ExpenseId { get; set; }
    public string ExpenseName { get; set; } = "";
    public DateTime ExpenseDate { get; set; }
    public decimal Amount { get; set; }
    public bool IsAdvance { get; set; }
    public int? AdvanceMonths { get; set; }

    // ⭐ للمصروف المقدم
    public int? AdvanceParentExpenseId { get; set; }
    public int? AdvanceMonthIndex { get; set; }
    public bool IsAdvanceParent => AdvanceMonthIndex == 0 && IsAdvance;
    public bool IsAdvanceChild => AdvanceParentExpenseId.HasValue;

    public string? Notes { get; set; }
    public string? Recipient { get; set; }

    public int ExpenseGroupId { get; set; }
    public string? ExpenseGroupName { get; set; }
    public string? FullGroupPath { get; set; }

    public int CashBoxId { get; set; }
    public string? CashBoxName { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime? CreatedAt { get; set; }
}

public class ExpenseFormDto
{
    public int ExpenseId { get; set; }
    public string ExpenseName { get; set; } = "";
    public DateTime ExpenseDate { get; set; } = DateTime.Today;
    public int? ExpenseGroupId { get; set; }
    public int? CashBoxId { get; set; }
    public decimal Amount { get; set; }
    public bool IsAdvance { get; set; }
    public int? AdvanceMonths { get; set; } = 1;
    public string? Notes { get; set; }
    public string? Recipient { get; set; }
}

public class ExpenseFilterDto
{
    public string? SearchText { get; set; }
    public int? ExpenseGroupId { get; set; }
    public int? CashBoxId { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public decimal? AmountFrom { get; set; }
    public decimal? AmountTo { get; set; }
    public bool? IsAdvance { get; set; }
    public bool? OnlyParents { get; set; }   // عرض الأصل فقط (بدون أشهر فرعية)
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public string SortBy { get; set; } = "ExpenseDate";
    public bool SortDescending { get; set; } = true;
}

public class ExpenseStatsDto
{
    public int TotalCount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal TodayAmount { get; set; }
    public decimal MonthAmount { get; set; }
    public decimal YearAmount { get; set; }
    public List<ExpenseGroupStatsDto> GroupBreakdown { get; set; } = new();
}

public class ExpenseGroupStatsDto
{
    public int ExpenseGroupId { get; set; }
    public string GroupName { get; set; } = "";
    public decimal Total { get; set; }
    public int Count { get; set; }
    public decimal Percentage { get; set; }
}

public class ExpenseGroupDto
{
    public int ExpenseGroupId { get; set; }
    public string ExpenseGroupName { get; set; } = "";
    public int? ParentGroupId { get; set; }
    public string? ParentGroupName { get; set; }
    public int ChildrenCount { get; set; }
    public int ExpensesCount { get; set; }
    public decimal TotalAmount { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? CreatedAt { get; set; }
    public List<ExpenseGroupDto> Children { get; set; } = new();
}

public class ExpenseGroupFormDto
{
    public int ExpenseGroupId { get; set; }
    public string ExpenseGroupName { get; set; } = "";
    public int? ParentGroupId { get; set; }
}
