namespace COCOBOLOERPNEW.DTOs;

// ═══════════════════════════════════════════════════════════════
// Kanban Board
// ═══════════════════════════════════════════════════════════════
public class KanbanBoardDto
{
    public List<KanbanColumnDto> Columns { get; set; } = new();
}

public class KanbanColumnDto
{
    public int StageId { get; set; }
    public string StageName { get; set; } = "";
    public string StageNameAr { get; set; } = "";
    public string StageColor { get; set; } = "#94a3b8";
    public int StageOrder { get; set; }
    public int Count { get; set; }
    public decimal Value { get; set; }
    public List<KanbanCardDto> Cards { get; set; } = new();
}

public class KanbanCardDto
{
    public int OpportunityId { get; set; }
    public int PartyId { get; set; }
    public string ClientName { get; set; } = "";
    public string? Phone { get; set; }
    public decimal? ExpectedValue { get; set; }
    public int? EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public string? InterestedProduct { get; set; }
    public int? SourceId { get; set; }
    public string? SourceName { get; set; }
    public DateTime? NextFollowUpDate { get; set; }
    public int StageId { get; set; }
    public int InteractionsCount { get; set; }
    public int TasksCount { get; set; }
    public bool IsOverdue { get; set; }
}