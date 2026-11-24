using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace COCOBOLOERPNEW.Models;

[Table("ExecutionOrders")]
public class ExecutionOrder
{
    [Key]
    public int ExecutionOrderId { get; set; }
    public int TransactionId { get; set; }
    public string Status { get; set; } = "Released";
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public int PeriodDays { get; set; }
    public DateTime DueDate { get; set; }
    public string? ChangeNote { get; set; }
    public string? ChangeRequestedBy { get; set; }
    public DateTime? ChangeRequestedAt { get; set; }
}

[Table("ExecutionOrderLines")]
public class ExecutionOrderLine
{
    [Key]
    public int LineId { get; set; }
    public int ExecutionOrderId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public int? AlternativeId { get; set; }
    public string? AlternativeName { get; set; }
    public decimal Quantity { get; set; }
    public int? PeriodDays { get; set; }
    public string? SpecSnapshot { get; set; }
    public string? ComponentSnapshot { get; set; }
}

[Table("ExecutionStages")]
public class ExecutionStage
{
    [Key]
    public int StageId { get; set; }
    public int ExecutionOrderId { get; set; }
    public string StageName { get; set; } = "";
    public int SortOrder { get; set; }
    public string Status { get; set; } = "Pending";
}
