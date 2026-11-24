using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace COCOBOLOERPNEW.Models;

[Table("ProductionPlans")]
public class ProductionPlan
{
    [Key]
    public int PlanId { get; set; }
    public int ExecutionOrderId { get; set; }
    public string Status { get; set; } = "Draft";
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string? SavedBy { get; set; }
    public DateTime? SavedAt { get; set; }
}

[Table("ProductionStages")]
public class ProductionStage
{
    [Key]
    public int StageId { get; set; }
    public int PlanId { get; set; }
    public string StageName { get; set; } = "";
    public int DurationDays { get; set; }
    public DateTime? PlannedStart { get; set; }
    public DateTime? PlannedEnd { get; set; }
    public DateTime? ActualStart { get; set; }
    public DateTime? ActualEnd { get; set; }
    public string Status { get; set; } = "Pending";
    public string QualityStatus { get; set; } = "Pending";
    public string? QualityBy { get; set; }
    public DateTime? QualityAt { get; set; }
    public int? WorkerUserId { get; set; }
    [MaxLength(200)]
    public string? WorkerName { get; set; }
    public int? QualityUserId { get; set; }
    [MaxLength(200)]
    public string? QualityUserName { get; set; }
    [MaxLength(1000)]
    public string? Instructions { get; set; }
    [MaxLength(500)]
    public string? Note { get; set; }
    [MaxLength(20)]
    public string? SampleType { get; set; }
    public int SortOrder { get; set; }
}

[Table("ProductionStageDependencies")]
public class ProductionStageDependency
{
    [Key]
    public int DependencyId { get; set; }
    public int StageId { get; set; }
    public int DependsOnStageId { get; set; }
}

[Table("ProductionMaterialIssues")]
public class ProductionMaterialIssue
{
    [Key]
    public int IssueId { get; set; }
    public int StageId { get; set; }
    [MaxLength(200)]
    public string MaterialName { get; set; } = "";
    [Column(TypeName = "decimal(18,3)")]
    public decimal Quantity { get; set; }
    [MaxLength(50)]
    public string? Unit { get; set; }
    [MaxLength(500)]
    public string? Note { get; set; }
    public string IssuedBy { get; set; } = "";
    public DateTime IssuedAt { get; set; }
}

[Table("ProductionExcuses")]
public class ProductionExcuse
{
    [Key]
    public int ExcuseId { get; set; }
    public int StageId { get; set; }
    [MaxLength(500)]
    public string Note { get; set; } = "";
    public string RequestedBy { get; set; } = "";
    public DateTime RequestedAt { get; set; }
    public string Status { get; set; } = "Pending";
    public string? ClosedBy { get; set; }
    public DateTime? ClosedAt { get; set; }
}

[Table("ProductionStageDateRequests")]
public class ProductionStageDateRequest
{
    [Key]
    public int RequestId { get; set; }
    public int StageId { get; set; }
    public DateTime OldStart { get; set; }
    public DateTime OldEnd { get; set; }
    public DateTime NewStart { get; set; }
    public DateTime NewEnd { get; set; }
    [MaxLength(500)]
    public string Reason { get; set; } = "";
    public string RequestedBy { get; set; } = "";
    public DateTime RequestedAt { get; set; }
    public string Status { get; set; } = "Pending";
    public string? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
}

[Table("ProductionPlanMeetings")]
public class ProductionPlanMeeting
{
    [Key]
    public int MeetingId { get; set; }
    public int ExecutionOrderId { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

[Table("ProductionPlanAttendees")]
public class ProductionPlanAttendee
{
    [Key]
    public int AttendeeId { get; set; }
    public int MeetingId { get; set; }
    public int UserId { get; set; }
    [MaxLength(200)]
    public string EmployeeName { get; set; } = "";
    [MaxLength(100)]
    public string? Department { get; set; }
    [MaxLength(100)]
    public string? JobTitle { get; set; }
    public string Status { get; set; } = "Invited";
    [MaxLength(500)]
    public string? Note { get; set; }
    public DateTime? DecidedAt { get; set; }
}

[Table("ProductionPlanMaterialRequests")]
public class ProductionPlanMaterialRequest
{
    [Key]
    public int RequestId { get; set; }
    public int MeetingId { get; set; }
    [MaxLength(200)]
    public string MaterialName { get; set; } = "";
    [Column(TypeName = "decimal(18,3)")]
    public decimal Quantity { get; set; }
    [MaxLength(50)]
    public string? Unit { get; set; }
    public int? OwnerUserId { get; set; }
    [MaxLength(200)]
    public string OwnerName { get; set; } = "";
    [MaxLength(500)]
    public string? Note { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

[Table("ProductionPlanSampleBatches")]
public class ProductionPlanSampleBatch
{
    [Key]
    public int BatchId { get; set; }
    public int ExecutionOrderId { get; set; }
    public DateTime SentAt { get; set; }
    public DateTime DueAt { get; set; }
    [MaxLength(20)]
    public string Status { get; set; } = "Pending";
    [MaxLength(100)]
    public string SentBy { get; set; } = "";
    public DateTime? DecidedAt { get; set; }
    [MaxLength(100)]
    public string? DecidedBy { get; set; }
    [MaxLength(500)]
    public string? Note { get; set; }
}
