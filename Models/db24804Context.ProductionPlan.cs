using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Models;

public partial class db24804Context
{
    public virtual DbSet<ProductionPlan> ProductionPlans { get; set; }
    public virtual DbSet<ProductionStage> ProductionStages { get; set; }
    public virtual DbSet<ProductionStageDependency> ProductionStageDependencies { get; set; }
    public virtual DbSet<ProductionMaterialIssue> ProductionMaterialIssues { get; set; }
    public virtual DbSet<ProductionExcuse> ProductionExcuses { get; set; }
    public virtual DbSet<ProductionStageDateRequest> ProductionStageDateRequests { get; set; }
    public virtual DbSet<ProductionPlanMeeting> ProductionPlanMeetings { get; set; }
    public virtual DbSet<ProductionPlanAttendee> ProductionPlanAttendees { get; set; }
    public virtual DbSet<ProductionPlanMaterialRequest> ProductionPlanMaterialRequests { get; set; }
    public virtual DbSet<ProductionPlanSampleBatch> ProductionPlanSampleBatches { get; set; }
}
