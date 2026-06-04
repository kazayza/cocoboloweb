using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Models;

public partial class db24804Context : DbContext
{
    public db24804Context()
    {
    }

    public db24804Context(DbContextOptions<db24804Context> options)
        : base(options)
    {
    }

    public virtual DbSet<AdType> AdTypes { get; set; }

    public virtual DbSet<AdditionalCharge> AdditionalCharges { get; set; }

    public virtual DbSet<Attendance> Attendances { get; set; }

    public virtual DbSet<AuditLog> AuditLogs { get; set; }

    public virtual DbSet<BiometricLog> BiometricLogs { get; set; }

    public virtual DbSet<Calendar> Calendars { get; set; }

    public virtual DbSet<CashBox> CashBoxes { get; set; }

    public virtual DbSet<CashboxTransaction> CashboxTransactions { get; set; }

    public virtual DbSet<CommissionAssignment> CommissionAssignments { get; set; }

    public virtual DbSet<CompanyInfo> CompanyInfos { get; set; }

    public virtual DbSet<CompanyLocation> CompanyLocations { get; set; }

    public virtual DbSet<Complaint> Complaints { get; set; }

    public virtual DbSet<ComplaintFollowUp> ComplaintFollowUps { get; set; }
    public virtual DbSet<ComplaintAttachment> ComplaintAttachments { get; set; }

    public virtual DbSet<ComplaintType> ComplaintTypes { get; set; }

    public virtual DbSet<ContactSource> ContactSources { get; set; }

    public virtual DbSet<ContactStatus> ContactStatuses { get; set; }

    public virtual DbSet<CrmTask> CrmTasks { get; set; }

    public virtual DbSet<CustomerInteraction> CustomerInteractions { get; set; }

    public virtual DbSet<DailyExemption> DailyExemptions { get; set; }

    public virtual DbSet<Employee> Employees { get; set; }

    public virtual DbSet<EmployeeShift> EmployeeShifts { get; set; }

    public virtual DbSet<Expense> Expenses { get; set; }

    public virtual DbSet<ExpenseGroup> ExpenseGroups { get; set; }

    public virtual DbSet<InterestCategory> InterestCategories { get; set; }

    public virtual DbSet<LostReason> LostReasons { get; set; }

    public virtual DbSet<Notification> Notifications { get; set; }

    public virtual DbSet<Party> Parties { get; set; }
    public virtual DbSet<PartyContact> PartyContacts { get; set; }

    public virtual DbSet<PartyType> PartyTypes { get; set; }
    public virtual DbSet<PersonalAccount> PersonalAccounts { get; set; }

    public virtual DbSet<Payment> Payments { get; set; }

    public virtual DbSet<Payroll> Payrolls { get; set; }

    public virtual DbSet<PayrollDetail> PayrollDetails { get; set; }
    public virtual DbSet<PayrollRun>        PayrollRuns        { get; set; }

public virtual DbSet<AttendanceManual>  AttendanceManuals  { get; set; }

    public virtual DbSet<Permission> Permissions { get; set; }

    public virtual DbSet<PriceChangeRequest> PriceChangeRequests { get; set; }

    public virtual DbSet<PriceHistory> PriceHistories { get; set; }

    public virtual DbSet<PricingMargin> PricingMargins { get; set; }

    public virtual DbSet<Product> Products { get; set; }

    public virtual DbSet<ProductComponent> ProductComponents { get; set; }

    public virtual DbSet<ProductGroup> ProductGroups { get; set; }

    public virtual DbSet<ProductImage> ProductImages { get; set; }

    public virtual DbSet<ProductPricingStatus> ProductPricingStatuses { get; set; }

    public virtual DbSet<ProductPricingTransition> ProductPricingTransitions { get; set; }

    public virtual DbSet<Quotation> Quotations { get; set; }

    public virtual DbSet<QuotationDetail> QuotationDetails { get; set; }

    public virtual DbSet<ReferralSource> ReferralSources { get; set; }

    public virtual DbSet<SalaryHistory> SalaryHistories { get; set; }

    public virtual DbSet<SalesOpportunity> SalesOpportunities { get; set; }

    public virtual DbSet<SalesStage> SalesStages { get; set; }

    public virtual DbSet<ShortPermission> ShortPermissions { get; set; }

    public virtual DbSet<StockLevel> StockLevels { get; set; }

    public virtual DbSet<StockTransaction> StockTransactions { get; set; }

    public virtual DbSet<TaskType> TaskTypes { get; set; }

    public virtual DbSet<TempInvoicePrintDetail> TempInvoicePrintDetails { get; set; }

    public virtual DbSet<Transaction> Transactions { get; set; }

    public virtual DbSet<TransactionDetail> TransactionDetails { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<UserPermission> UserPermissions { get; set; }

    public virtual DbSet<VwComplaintsList> VwComplaintsLists { get; set; }

    public virtual DbSet<VwCrmTask> VwCrmTasks { get; set; }

    public virtual DbSet<VwCustomerInteraction> VwCustomerInteractions { get; set; }

    public virtual DbSet<VwPriceChangeRequest> VwPriceChangeRequests { get; set; }

    public virtual DbSet<VwSalesDeliveryStatus> VwSalesDeliveryStatuses { get; set; }

    public virtual DbSet<VwSalesOpportunity> VwSalesOpportunities { get; set; }

    public virtual DbSet<Warehouse> Warehouses { get; set; }
    public virtual DbSet<EmployeeLoan>     EmployeeLoans     { get; set; }
    public virtual DbSet<LoanInstallment>  LoanInstallments  { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // ✅ Connection String بتتحمل من appsettings.json فقط عبر DI
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseCollation("Arabic_CS_AI");

        modelBuilder.Entity<AdType>(entity =>
        {
            entity.Property(e => e.AdTypeId).HasColumnName("AdTypeID");
            entity.Property(e => e.AdTypeName).HasMaxLength(100);
            entity.Property(e => e.AdTypeNameAr).HasMaxLength(100);
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CreatedBy).HasMaxLength(50);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.LastUpdatedAt).HasColumnType("datetime");
            entity.Property(e => e.LastUpdatedBy).HasMaxLength(50);
        });
        modelBuilder.Entity<PersonalAccount>(entity =>
{
    entity.HasKey(e => e.PersonalAccountId);
    entity.Property(e => e.AccountName).IsRequired().HasMaxLength(200);
    entity.Property(e => e.AccountType).IsRequired().HasMaxLength(50);
    entity.Property(e => e.Phone).HasMaxLength(20);
    entity.Property(e => e.Email).HasMaxLength(100);
    entity.Property(e => e.NationalId).HasMaxLength(50);
    entity.Property(e => e.OpeningBalance)
          .HasColumnType("decimal(18, 2)")
          .HasDefaultValue(0m);
    entity.Property(e => e.OpeningType)
          .IsRequired()
          .HasMaxLength(20)
          .HasDefaultValue("Credit");
    entity.Property(e => e.IsActive).HasDefaultValue(true);
    entity.Property(e => e.CreatedBy).HasMaxLength(100);
    entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETDATE()");
    entity.Property(e => e.LastUpdatedBy).HasMaxLength(100);
});

        modelBuilder.Entity<AdditionalCharge>(entity =>
        {
            entity.HasKey(e => e.ChargeId).HasName("PK__dbo_Addi__17FC363B25389447");

            entity.Property(e => e.ChargeId).HasColumnName("ChargeID");
            entity.Property(e => e.ChargeAmount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.ChargeDescription).HasMaxLength(255);
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CreatedBy).HasMaxLength(50);
            entity.Property(e => e.Notes).HasMaxLength(255);
            entity.Property(e => e.PartyId).HasColumnName("PartyID");
            entity.Property(e => e.QuotationId).HasColumnName("QuotationID");
            entity.Property(e => e.TransactionId).HasColumnName("TransactionID");
        });

        modelBuilder.Entity<Attendance>(entity =>
        {
            entity.HasKey(e => e.AttendanceId).HasName("PK__Attendan__8B69263CAD35BB88");

            entity.ToTable("Attendance");

            entity.HasIndex(e => e.BiometricCode, "idx_attendance_biometric");

            entity.HasIndex(e => e.LogDate, "idx_attendance_date");

            entity.HasIndex(e => e.Status, "idx_attendance_status");

            entity.Property(e => e.AttendanceId).HasColumnName("AttendanceID");
            entity.Property(e => e.EarlyLeaveMinutes).HasDefaultValue(0);
            entity.Property(e => e.LateMinutes).HasDefaultValue(0);
            entity.Property(e => e.LogDate).HasColumnType("datetime");
            entity.Property(e => e.PenaltyHours)
                .HasDefaultValue(0m)
                .HasColumnType("decimal(5, 2)");
            entity.Property(e => e.Status).HasMaxLength(30);
            entity.Property(e => e.TimeIn).HasPrecision(0);
            entity.Property(e => e.TimeOut).HasPrecision(0);
            entity.Property(e => e.TotalHours).HasColumnType("decimal(5, 2)");
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.AuditId).HasName("PK__Audit_Lo__A17F23B89DFD6297");

            entity.ToTable("Audit_Log");

            entity.Property(e => e.AuditId).HasColumnName("AuditID");
            entity.Property(e => e.AccessUserName).HasMaxLength(100);
            entity.Property(e => e.ActionDate)
                .HasDefaultValueSql("(sysdatetime())")
                .HasColumnType("datetime");
            entity.Property(e => e.ActionType).HasMaxLength(10);
            entity.Property(e => e.AppName)
                .HasMaxLength(200)
                .HasDefaultValueSql("(app_name())");
            entity.Property(e => e.HostName)
                .HasMaxLength(200)
                .HasDefaultValueSql("(host_name())");
            entity.Property(e => e.LoginName)
                .HasMaxLength(200)
                .HasDefaultValueSql("(suser_sname())");
            entity.Property(e => e.PrimaryKeyValue).HasMaxLength(200);
            entity.Property(e => e.TableName).HasMaxLength(128);
        });

        modelBuilder.Entity<BiometricLog>(entity =>
        {
            entity.HasKey(e => e.BiometricLogId).HasName("PK__Biometri__8C1B86E269619508");

            entity.ToTable("BiometricLog");

            entity.Property(e => e.BiometricLogId).HasColumnName("BiometricLogID");
            entity.Property(e => e.LogDate).HasColumnType("datetime");
            entity.Property(e => e.LogTime).HasPrecision(0);
        });

        modelBuilder.Entity<Calendar>(entity =>
        {
            entity.HasKey(e => e.CalendarDate).HasName("PK__Calendar__BEFC44DA3BEBDA7A");

            entity.ToTable("Calendar");

            entity.Property(e => e.CalendarDate).HasColumnType("datetime");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.DayName).HasMaxLength(20);
            entity.Property(e => e.IsHoliday).HasDefaultValue(false);
        });

        modelBuilder.Entity<CashBox>(entity =>
        {
            entity.HasKey(e => e.CashBoxId).HasName("PK__CashBoxe__3E790512EDDA3B98");

            entity.Property(e => e.CashBoxId).HasColumnName("CashBoxID");
            entity.Property(e => e.CashBoxName).HasMaxLength(100);
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CreatedBy).HasMaxLength(50);
            entity.Property(e => e.Description).HasMaxLength(255);
            entity.Property(e => e.LastUpdatedAt).HasColumnType("datetime");
            entity.Property(e => e.LastUpdatedBy).HasMaxLength(50);
             entity.Property(e => e.OpeningBalance)
          .HasColumnType("decimal(18, 2)")
          .HasDefaultValue(0m);
    entity.Property(e => e.IsActive).HasDefaultValue(true);
    entity.Property(e => e.IsDefault).HasDefaultValue(false);
    entity.Property(e => e.CashBoxKind).HasMaxLength(50);
    entity.Property(e => e.Icon).HasMaxLength(50);
    entity.Property(e => e.Color).HasMaxLength(20);
        });

        modelBuilder.Entity<CashboxTransaction>(entity =>
        {
            entity.HasKey(e => e.CashboxTransactionId).HasName("PK__CashboxT__56AB615878878691");

            entity.Property(e => e.CashboxTransactionId).HasColumnName("CashboxTransactionID");
            entity.Property(e => e.Amount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.CashBoxId).HasColumnName("CashBoxID");
            entity.Property(e => e.CreatedAt).HasColumnType("datetime");
            entity.Property(e => e.CreatedBy).HasMaxLength(50);
            entity.Property(e => e.PaymentId).HasColumnName("PaymentID");
            entity.Property(e => e.ReferenceId).HasColumnName("ReferenceID");
            entity.Property(e => e.ReferenceType).HasMaxLength(20);
            entity.Property(e => e.TransactionDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.TransactionType).HasMaxLength(20);

            entity.HasOne(d => d.CashBox).WithMany(p => p.CashboxTransactions)
                .HasForeignKey(d => d.CashBoxId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CashboxTransactions_CashBoxes");

            entity.HasOne(d => d.Payment).WithMany(p => p.CashboxTransactions)
                .HasForeignKey(d => d.PaymentId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_CashboxTransactions_Payments");
        });

        modelBuilder.Entity<CommissionAssignment>(entity =>
        {
            entity.HasKey(e => e.AssignmentId).HasName("PK__Commissi__32499E57D185D43D");

            entity.HasIndex(e => new { e.CommissionYear, e.CommissionMonth }, "idx_commission_date");

            entity.HasIndex(e => e.EmployeeId, "idx_commission_emp");

            entity.Property(e => e.AssignmentId).HasColumnName("AssignmentID");
            entity.Property(e => e.ApprovedAt).HasColumnType("datetime");
            entity.Property(e => e.ApprovedBy).HasMaxLength(50);
            entity.Property(e => e.CommissionAmount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.CommissionRate).HasColumnType("decimal(8, 4)");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CreatedBy).HasMaxLength(50);
            entity.Property(e => e.EmployeeId).HasColumnName("EmployeeID");
            entity.Property(e => e.TransactionAmount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.TransactionId).HasColumnName("TransactionID");
        });

        modelBuilder.Entity<CompanyInfo>(entity =>
        {
            entity.HasKey(e => e.CompanyId).HasName("PK__CompanyI__2D971C4CAF021099");

            entity.ToTable("CompanyInfo");

            entity.Property(e => e.CompanyId).HasColumnName("CompanyID");
            entity.Property(e => e.Address).HasMaxLength(255);
            entity.Property(e => e.CompanyName).HasMaxLength(100);
            entity.Property(e => e.CurrentVersion).HasMaxLength(20);
            entity.Property(e => e.InfoEmail).HasMaxLength(100);
            entity.Property(e => e.LogoPath).HasMaxLength(255);
            entity.Property(e => e.Phone1).HasMaxLength(20);
            entity.Property(e => e.Phone2).HasMaxLength(20);
            entity.Property(e => e.SalesEmail).HasMaxLength(100);
            entity.Property(e => e.TocDropBox).HasColumnName("tocDropBox");
            entity.Property(e => e.Website).HasMaxLength(100);
        });

        modelBuilder.Entity<CompanyLocation>(entity =>
        {
            entity.HasKey(e => e.LocationId).HasName("PK__CompanyL__E7FEA47726D7189E");

            entity.Property(e => e.LocationId).HasColumnName("LocationID");
            entity.Property(e => e.AllowedRadius).HasDefaultValue(100);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Latitude).HasColumnType("decimal(10, 8)");
            entity.Property(e => e.LocationName).HasMaxLength(100);
            entity.Property(e => e.Longitude).HasColumnType("decimal(11, 8)");
        });

        modelBuilder.Entity<Complaint>(entity =>
        {
            entity.HasKey(e => e.ComplaintId).HasName("PK__Complain__740D89AF4C80D5B5");

            entity.Property(e => e.ComplaintId).HasColumnName("ComplaintID");
            entity.Property(e => e.ComplaintDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CreatedBy).HasMaxLength(50);
            entity.Property(e => e.Escalated).HasDefaultValue(false);
            entity.Property(e => e.EscalatedDate).HasColumnType("datetime");
            entity.Property(e => e.EscalatedTo).HasMaxLength(100);
            entity.Property(e => e.EscalationReason).HasMaxLength(500);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.OpportunityId).HasColumnName("OpportunityID");
            entity.Property(e => e.PartyId).HasColumnName("PartyID");
            entity.Property(e => e.Priority).HasDefaultValue((byte)2);
            entity.Property(e => e.SolvedDate).HasColumnType("datetime");
            entity.Property(e => e.Status).HasDefaultValue((byte)1);
            entity.Property(e => e.Subject).HasMaxLength(255);
            entity.Property(e => e.TypeId).HasColumnName("TypeID");
            entity.Property(e => e.TransactionId).HasColumnName("TransactionID");
            entity.Property(e => e.ProductId).HasColumnName("ProductID");

            entity.HasOne(d => d.AssignedToNavigation).WithMany(p => p.Complaints)
                .HasForeignKey(d => d.AssignedTo)
                .HasConstraintName("FK_Complaints_Employee");

            entity.HasOne(d => d.Opportunity).WithMany(p => p.Complaints)
                .HasForeignKey(d => d.OpportunityId)
                .HasConstraintName("FK_Complaints_Opportunity");

            entity.HasOne(d => d.Party).WithMany(p => p.Complaints)
                .HasForeignKey(d => d.PartyId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Complaints_Party");

            entity.HasOne(d => d.Type).WithMany(p => p.Complaints)
                .HasForeignKey(d => d.TypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Complaints_Type");
        });

        modelBuilder.Entity<ComplaintFollowUp>(entity =>
        {
            entity.HasKey(e => e.FollowUpId).HasName("PK__Complain__D507D658C60D9DA4");

            entity.Property(e => e.FollowUpId).HasColumnName("FollowUpID");
            entity.Property(e => e.ActionTaken).HasMaxLength(500);
            entity.Property(e => e.ComplaintId).HasColumnName("ComplaintID");
            entity.Property(e => e.FollowUpDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.NextFollowUpDate).HasColumnType("datetime");

            entity.HasOne(d => d.Complaint).WithMany(p => p.ComplaintFollowUps)
                .HasForeignKey(d => d.ComplaintId)
                .HasConstraintName("FK_ComplaintFollowUps_Complaint");

            entity.HasOne(d => d.FollowUpByNavigation).WithMany(p => p.ComplaintFollowUps)
                .HasForeignKey(d => d.FollowUpBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ComplaintFollowUps_Employee");
        });

        modelBuilder.Entity<ComplaintType>(entity =>
        {
            entity.HasKey(e => e.TypeId).HasName("PK__Complain__516F0395BEEF9CBB");

            entity.Property(e => e.TypeId).HasColumnName("TypeID");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.TypeName).HasMaxLength(100);
            entity.Property(e => e.TypeNameAr).HasMaxLength(100);
        });

        modelBuilder.Entity<ContactSource>(entity =>
        {
            entity.HasKey(e => e.SourceId);

            entity.Property(e => e.SourceId).HasColumnName("SourceID");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CreatedBy).HasMaxLength(50);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.LastUpdatedAt).HasColumnType("datetime");
            entity.Property(e => e.LastUpdatedBy).HasMaxLength(50);
            entity.Property(e => e.SourceIcon).HasMaxLength(10);
            entity.Property(e => e.SourceName).HasMaxLength(50);
            entity.Property(e => e.SourceNameAr).HasMaxLength(50);
        });

        modelBuilder.Entity<ContactStatus>(entity =>
        {
            entity.HasKey(e => e.StatusId);

            entity.ToTable("ContactStatus");

            entity.Property(e => e.StatusId).HasColumnName("StatusID");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CreatedBy).HasMaxLength(50);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.LastUpdatedAt).HasColumnType("datetime");
            entity.Property(e => e.LastUpdatedBy).HasMaxLength(50);
            entity.Property(e => e.StatusName).HasMaxLength(50);
            entity.Property(e => e.StatusNameAr).HasMaxLength(50);
        });

        modelBuilder.Entity<CrmTask>(entity =>
        {
            entity.HasKey(e => e.TaskId);

            entity.ToTable("CRM_Tasks", tb => tb.HasTrigger("TRG_Audit_CRM_Tasks"));

            entity.HasIndex(e => e.AssignedTo, "IX_CRM_Tasks_AssignedTo");

            entity.HasIndex(e => e.DueDate, "IX_CRM_Tasks_DueDate");

            entity.HasIndex(e => e.OpportunityId, "IX_CRM_Tasks_OpportunityID");

            entity.HasIndex(e => e.Status, "IX_CRM_Tasks_Status");

            entity.Property(e => e.TaskId).HasColumnName("TaskID");
            entity.Property(e => e.CompletedBy).HasMaxLength(50);
            entity.Property(e => e.CompletedDate).HasColumnType("datetime");
            entity.Property(e => e.CompletionNotes).HasMaxLength(500);
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CreatedBy).HasMaxLength(50);
            entity.Property(e => e.DueDate).HasColumnType("datetime");
            entity.Property(e => e.DueTime).HasPrecision(0);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.OpportunityId).HasColumnName("OpportunityID");
            entity.Property(e => e.PartyId).HasColumnName("PartyID");
            entity.Property(e => e.Priority)
                .HasMaxLength(20)
                .HasDefaultValue("Normal");
            entity.Property(e => e.ReminderEnabled).HasDefaultValue(true);
            entity.Property(e => e.ReminderMinutes).HasDefaultValue(30);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Pending");
            entity.Property(e => e.TaskDescription).HasMaxLength(500);
            entity.Property(e => e.TaskTypeId).HasColumnName("TaskTypeID");

            entity.HasOne(d => d.AssignedToNavigation).WithMany(p => p.CrmTasks)
                .HasForeignKey(d => d.AssignedTo)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Tasks_Employees");

            entity.HasOne(d => d.Opportunity).WithMany(p => p.CrmTasks)
                .HasForeignKey(d => d.OpportunityId)
                .HasConstraintName("FK_Tasks_Opportunities");

            entity.HasOne(d => d.Party).WithMany(p => p.CrmTasks)
                .HasForeignKey(d => d.PartyId)
                .HasConstraintName("FK_Tasks_Parties");

            entity.HasOne(d => d.TaskType).WithMany(p => p.CrmTasks)
                .HasForeignKey(d => d.TaskTypeId)
                .HasConstraintName("FK_Tasks_TaskTypes");
        });

        modelBuilder.Entity<CustomerInteraction>(entity =>
        {
            entity.HasKey(e => e.InteractionId);

            entity.ToTable(tb => tb.HasTrigger("TRG_Audit_CustomerInteractions"));

            entity.HasIndex(e => e.EmployeeId, "IX_CustomerInteractions_EmployeeID");

            entity.HasIndex(e => e.InteractionDate, "IX_CustomerInteractions_InteractionDate");

            entity.HasIndex(e => e.OpportunityId, "IX_CustomerInteractions_OpportunityID");

            entity.HasIndex(e => e.PartyId, "IX_CustomerInteractions_PartyID");

            entity.Property(e => e.InteractionId).HasColumnName("InteractionID");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CreatedBy).HasMaxLength(50);
            entity.Property(e => e.EditAt).HasColumnType("datetime");
            entity.Property(e => e.EditBy).HasMaxLength(50);
            entity.Property(e => e.EmployeeId).HasColumnName("EmployeeID");
            entity.Property(e => e.InteractionDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.InteractionTime).HasPrecision(0);
            entity.Property(e => e.NextFollowUpDate).HasColumnType("datetime");
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.Property(e => e.OpportunityId).HasColumnName("OpportunityID");
            entity.Property(e => e.PartyId).HasColumnName("PartyID");
            entity.Property(e => e.SourceId).HasColumnName("SourceID");
            entity.Property(e => e.StageAfterId).HasColumnName("StageAfterID");
            entity.Property(e => e.StageBeforeId).HasColumnName("StageBeforeID");
            entity.Property(e => e.StatusId).HasColumnName("StatusID");
            entity.Property(e => e.Summary).HasMaxLength(1000);

            entity.HasOne(d => d.Employee).WithMany(p => p.CustomerInteractions)
                .HasForeignKey(d => d.EmployeeId)
                .HasConstraintName("FK_Interactions_Employees");

            entity.HasOne(d => d.Opportunity).WithMany(p => p.CustomerInteractions)
                .HasForeignKey(d => d.OpportunityId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Interactions_Opportunities");

            entity.HasOne(d => d.Party).WithMany(p => p.CustomerInteractions)
                .HasForeignKey(d => d.PartyId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Interactions_Parties");

            entity.HasOne(d => d.Source).WithMany(p => p.CustomerInteractions)
                .HasForeignKey(d => d.SourceId)
                .HasConstraintName("FK_Interactions_Sources");

            entity.HasOne(d => d.StageAfter).WithMany(p => p.CustomerInteractionStageAfters)
                .HasForeignKey(d => d.StageAfterId)
                .HasConstraintName("FK_Interactions_StageAfter");

            entity.HasOne(d => d.StageBefore).WithMany(p => p.CustomerInteractionStageBefores)
                .HasForeignKey(d => d.StageBeforeId)
                .HasConstraintName("FK_Interactions_StageBefore");

            entity.HasOne(d => d.Status).WithMany(p => p.CustomerInteractions)
                .HasForeignKey(d => d.StatusId)
                .HasConstraintName("FK_Interactions_Status");
        });

        modelBuilder.Entity<DailyExemption>(entity =>
        {
            entity.HasKey(e => e.ExemptionId).HasName("PK__DailyExe__B0B7514039DDD130");

            entity.HasIndex(e => e.BioEmployeeId, "idx_exemptions_biometric");

            entity.HasIndex(e => e.ExemptionDate, "idx_exemptions_date");

            entity.Property(e => e.ExemptionId).HasColumnName("ExemptionID");
            entity.Property(e => e.ApprovedBy)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.BioEmployeeId).HasColumnName("BioEmployeeID");
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.Description).HasColumnType("text");
            entity.Property(e => e.ExemptionDate).HasColumnType("datetime");
            entity.Property(e => e.ReasonCode)
                .HasMaxLength(20)
                .IsUnicode(false);
        });

        modelBuilder.Entity<Employee>(entity =>
        {
            entity.HasKey(e => e.EmployeeId).HasName("PK__Employee__7AD04FF149689B9D");

            entity.Property(e => e.EmployeeId).HasColumnName("EmployeeID");
            entity.Property(e => e.Address).HasMaxLength(150);
            entity.Property(e => e.BioEmployeeId).HasColumnName("BioEmployeeID");
            entity.Property(e => e.BirthDate).HasColumnType("datetime");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CreatedBy).HasMaxLength(50);
            entity.Property(e => e.CurrentSalaryBase).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.Department).HasMaxLength(100);
            entity.Property(e => e.EmailAddress).HasMaxLength(50);
            entity.Property(e => e.EndDate).HasColumnType("datetime");
            entity.Property(e => e.FullName).HasMaxLength(150);
            entity.Property(e => e.Gender).HasMaxLength(5);
            entity.Property(e => e.HireDate).HasColumnType("datetime");
            entity.Property(e => e.IsPermanentlyExempt).HasDefaultValue(false);
            entity.Property(e => e.JobTitle).HasMaxLength(100);
            entity.Property(e => e.MobilePhone).HasMaxLength(20);
            entity.Property(e => e.MobilePhone2).HasMaxLength(20);
            entity.Property(e => e.NationalId)
                .HasMaxLength(14)
                .HasColumnName("NationalID");
            entity.Property(e => e.Notes).HasMaxLength(150);
            entity.Property(e => e.Qualification)
                .HasMaxLength(100)
                .HasColumnName("qualification");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("نشط");
            entity.Property(e => e.Yearqualification)
                .HasMaxLength(4)
                .HasColumnName("yearqualification");
        });
        modelBuilder.Entity<EmployeeLoan>(entity =>
        {
            entity.ToTable("EmployeeLoans");

            entity.HasKey(e => e.LoanId);

            entity.Property(e => e.LoanAmount).HasColumnType("decimal(18,2)");
            entity.Property(e => e.MonthlyInstallment).HasColumnType("decimal(18,2)");
            entity.Property(e => e.RemainingAmount).HasColumnType("decimal(18,2)");
            entity.Property(e => e.StartDeductionMonth).HasMaxLength(7);
            entity.Property(e => e.Status).HasMaxLength(20);
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.Property(e => e.ApprovedBy).HasMaxLength(100);
            entity.Property(e => e.CreatedBy).HasMaxLength(100);

            entity.HasOne(d => d.Employee)
                  .WithMany()
                  .HasForeignKey(d => d.EmployeeId)
                  .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.CashBox)
                  .WithMany()
                  .HasForeignKey(d => d.CashBoxId)
                  .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.CashboxTransaction)
                  .WithMany()
                  .HasForeignKey(d => d.CashboxTransactionId)
                  .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<LoanInstallment>(entity =>
        {
            entity.ToTable("LoanInstallments");

            entity.HasKey(e => e.InstallmentId);

            entity.Property(e => e.Amount).HasColumnType("decimal(18,2)");
            entity.Property(e => e.DeductionMonth).HasMaxLength(7);
            entity.Property(e => e.Status).HasMaxLength(20);
            entity.Property(e => e.Notes).HasMaxLength(300);
            entity.Property(e => e.CreatedBy).HasMaxLength(100);

            entity.HasOne(d => d.Loan)
                  .WithMany(p => p.Installments)
                  .HasForeignKey(d => d.LoanId)
                  .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.Employee)
                  .WithMany()
                  .HasForeignKey(d => d.EmployeeId)
                  .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.Payroll)
                  .WithMany()
                  .HasForeignKey(d => d.PayrollId)
                  .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.PayrollDetail)
                  .WithMany()
                  .HasForeignKey(d => d.PayrollDetailId)
                  .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<EmployeeShift>(entity =>
        {
            entity.HasKey(e => e.EmployeeShiftId).HasName("PK__Employee__2FBBBA134CFF27A8");

            entity.Property(e => e.EmployeeShiftId).HasColumnName("EmployeeShiftID");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CreatedBy).HasMaxLength(50);
            entity.Property(e => e.EffectiveFrom)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.EffectiveTo).HasColumnType("datetime");
            entity.Property(e => e.EmployeeId).HasColumnName("EmployeeID");
            entity.Property(e => e.EndTime).HasPrecision(0);
            entity.Property(e => e.ShiftType)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.StartTime).HasPrecision(0);
            entity.Property(e => e.OffDay1) .HasColumnName("OffDay1");
            entity.Property(e => e.OffDay2).HasColumnName("OffDay2");
        });

        modelBuilder.Entity<Expense>(entity =>
        {
            entity.HasKey(e => e.ExpenseId).HasName("PK__Expenses__1445CFF356C3F1DC");

            entity.ToTable(tb => tb.HasTrigger("TRG_Audit_Expenses"));

            entity.Property(e => e.ExpenseId).HasColumnName("ExpenseID");
            entity.Property(e => e.Amount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.CashBoxId).HasColumnName("CashBoxID");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CreatedBy).HasMaxLength(50);
            entity.Property(e => e.ExpenseDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.ExpenseGroupId).HasColumnName("ExpenseGroupID");
            entity.Property(e => e.ExpenseName).HasMaxLength(100);
            entity.Property(e => e.IsAdvance).HasDefaultValue(false);
            entity.Property(e => e.Notes).HasMaxLength(255);
            entity.Property(e => e.Torecipient).HasMaxLength(100);

            entity.HasOne(d => d.ExpenseGroup).WithMany(p => p.Expenses)
                .HasForeignKey(d => d.ExpenseGroupId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Expenses__Expens__0B5CAFEA");
             entity.Property(e => e.AdvanceParentExpenseId);
            entity.Property(e => e.AdvanceMonthIndex);
        });

        modelBuilder.Entity<ExpenseGroup>(entity =>
        {
            entity.HasKey(e => e.ExpenseGroupId).HasName("PK__ExpenseG__0ECF0A51DE2B8C61");

            entity.Property(e => e.ExpenseGroupId).HasColumnName("ExpenseGroupID");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CreatedBy).HasMaxLength(50);
            entity.Property(e => e.ExpenseGroupName).HasMaxLength(100);
            entity.Property(e => e.ParentGroupId).HasColumnName("ParentGroupID");

            entity.HasOne(d => d.ParentGroup).WithMany(p => p.InverseParentGroup)
                .HasForeignKey(d => d.ParentGroupId)
                .HasConstraintName("FK_ExpenseGroups_Parent");
        });

        modelBuilder.Entity<InterestCategory>(entity =>
        {
            entity.HasKey(e => e.CategoryId);

            entity.Property(e => e.CategoryId).HasColumnName("CategoryID");
            entity.Property(e => e.CategoryName).HasMaxLength(100);
            entity.Property(e => e.CategoryNameAr).HasMaxLength(100);
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CreatedBy).HasMaxLength(50);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.LastUpdatedAt).HasColumnType("datetime");
            entity.Property(e => e.LastUpdatedBy).HasMaxLength(50);
        });

        modelBuilder.Entity<LostReason>(entity =>
        {
            entity.Property(e => e.LostReasonId).HasColumnName("LostReasonID");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CreatedBy).HasMaxLength(50);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.LastUpdatedAt).HasColumnType("datetime");
            entity.Property(e => e.LastUpdatedBy).HasMaxLength(50);
            entity.Property(e => e.ReasonName).HasMaxLength(100);
            entity.Property(e => e.ReasonNameAr).HasMaxLength(100);
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.NotificationId).HasName("PK__Notifica__20CF2E322A6DBEDB");

            entity.ToTable(tb => tb.HasTrigger("TRG_Audit_Notifications"));

            entity.Property(e => e.NotificationId).HasColumnName("NotificationID");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CreatedBy).HasMaxLength(100);
            entity.Property(e => e.FormName).HasMaxLength(100);
            entity.Property(e => e.LastReminderAt).HasColumnType("datetime");
            entity.Property(e => e.ReadAt).HasColumnType("datetime");
            entity.Property(e => e.RecipientUser).HasMaxLength(100);
            entity.Property(e => e.RelatedId).HasColumnName("RelatedID");
            entity.Property(e => e.RelatedTable).HasMaxLength(100);
            entity.Property(e => e.ReminderEndAt).HasColumnType("datetime");
            entity.Property(e => e.ReminderNextAt).HasColumnType("datetime");
            entity.Property(e => e.Title).HasMaxLength(200);
        });

        modelBuilder.Entity<Party>(entity =>
{
    entity.HasKey(e => e.PartyId).HasName("PK__dbo_Part__1640CD13CB7FCA23");

    entity.ToTable(tb => tb.HasTrigger("TRG_Audit_Parties"));

    // ============================
    // Original Columns
    // ============================
    entity.Property(e => e.PartyId).HasColumnName("PartyID");
    entity.Property(e => e.Address).HasMaxLength(250);
    entity.Property(e => e.BalanceType)
        .HasMaxLength(1)
        .IsUnicode(false)
        .HasDefaultValue("D")
        .IsFixedLength();
    entity.Property(e => e.ContactPerson).HasMaxLength(100);
    entity.Property(e => e.CreatedAt)
        .HasDefaultValueSql("(getdate())")
        .HasColumnType("datetime");
    entity.Property(e => e.CreatedBy).HasMaxLength(100);
    entity.Property(e => e.DataDone)
        .HasMaxLength(50)
        .HasColumnName("dataDone");
    entity.Property(e => e.Email).HasMaxLength(100);
    entity.Property(e => e.FloorNumber).HasMaxLength(50);
    entity.Property(e => e.IsActive).HasDefaultValue(true);
    entity.Property(e => e.NationalId)
        .HasMaxLength(14)
        .HasColumnName("NationalID");
    entity.Property(e => e.Notes).HasMaxLength(255);
    entity.Property(e => e.OpeningBalance)
        .HasDefaultValue(0m)
        .HasColumnType("decimal(18, 2)");
    entity.Property(e => e.PartyName).HasMaxLength(200);
    entity.Property(e => e.Phone).HasMaxLength(50);
    entity.Property(e => e.Phone2).HasMaxLength(50);
    entity.Property(e => e.ReferralSourceId).HasColumnName("ReferralSourceID");
    entity.Property(e => e.TaxNumber).HasMaxLength(50);

    // ============================
    // New Columns
    // ============================
    entity.Property(e => e.CustomerStage)
        .HasMaxLength(20)
        .HasDefaultValue("Lead");
    entity.Property(e => e.StageId)
    .HasColumnName("StageID");

    entity.Property(e => e.JobTitle)
        .HasMaxLength(100);

    entity.Property(e => e.ParentPartyId)
        .HasColumnName("ParentPartyID");

    entity.Property(e => e.City)
        .HasMaxLength(100);

    entity.Property(e => e.Area)
        .HasMaxLength(100);

    entity.Property(e => e.ContactSourceId)
        .HasColumnName("ContactSourceID");

    entity.Property(e => e.LastContactDate)
        .HasColumnType("datetime");

    entity.Property(e => e.Rating);

    entity.Property(e => e.LastUpdatedBy)
        .HasMaxLength(100);

    entity.Property(e => e.LastUpdatedAt)
        .HasColumnType("datetime");

    // ============================
    // Original Relations
    // ============================
    entity.HasOne(d => d.PartyTypeNavigation).WithMany(p => p.Parties)
        .HasForeignKey(d => d.PartyType)
        .OnDelete(DeleteBehavior.ClientSetNull)
        .HasConstraintName("FK_Parties_PartyTypes");

    entity.HasOne(d => d.ReferralSource).WithMany(p => p.Parties)
        .HasForeignKey(d => d.ReferralSourceId)
        .HasConstraintName("FK_Parties_ReferralSource");

    // ============================
    // New Relations
    // ============================
    entity.HasOne(d => d.ParentParty)
        .WithMany(p => p.InverseParentParty)
        .HasForeignKey(d => d.ParentPartyId)
        .OnDelete(DeleteBehavior.ClientSetNull)
        .HasConstraintName("FK_Parties_Parent");

    entity.HasOne(d => d.ContactSource)
        .WithMany(p => p.Parties)
        .HasForeignKey(d => d.ContactSourceId)
        .OnDelete(DeleteBehavior.ClientSetNull)
        .HasConstraintName("FK_Parties_ContactSource");
    entity.HasOne(d => d.Stage)
    .WithMany()
    .HasForeignKey(d => d.StageId)
    .OnDelete(DeleteBehavior.ClientSetNull)
    .HasConstraintName("FK_Parties_SalesStage");

    entity.HasMany(d => d.PartyContacts)
        .WithOne(p => p.Party)
        .HasForeignKey(d => d.PartyId)
        .OnDelete(DeleteBehavior.Cascade)
        .HasConstraintName("FK_PartyContacts_Parties");
});

// ============================
// PartyContact Entity
// ============================
modelBuilder.Entity<PartyContact>(entity =>
{
    entity.HasKey(e => e.ContactId)
        .HasName("PK_PartyContacts");

    entity.Property(e => e.ContactId)
        .HasColumnName("ContactID");

    entity.Property(e => e.PartyId)
        .HasColumnName("PartyID");

    entity.Property(e => e.ContactName)
        .HasMaxLength(100)
        .IsRequired();

    entity.Property(e => e.JobTitle)
        .HasMaxLength(100);

    entity.Property(e => e.Phone)
        .HasMaxLength(50);

    entity.Property(e => e.Email)
        .HasMaxLength(100);

    entity.Property(e => e.Notes)
        .HasMaxLength(255);

    entity.Property(e => e.IsPrimary)
        .HasDefaultValue(false);

    entity.Property(e => e.IsActive)
        .HasDefaultValue(true);

    entity.Property(e => e.CreatedBy)
        .HasMaxLength(100);

    entity.Property(e => e.CreatedAt)
        .HasDefaultValueSql("(getdate())")
        .HasColumnType("datetime");

    entity.HasOne(d => d.Party)
        .WithMany(p => p.PartyContacts)
        .HasForeignKey(d => d.PartyId)
        .OnDelete(DeleteBehavior.Cascade)
        .HasConstraintName("FK_PartyContacts_Parties");
});

        modelBuilder.Entity<PartyType>(entity =>
        {
            entity.Property(e => e.PartyTypeId).HasColumnName("PartyTypeID");
            entity.Property(e => e.PartyTypeName).HasMaxLength(50);
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasKey(e => e.PaymentId).HasName("PK__Payments__9B556A58E7B2E147");

            entity.Property(e => e.PaymentId).HasColumnName("PaymentID");
            entity.Property(e => e.Amount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.CashboxTransactionId).HasColumnName("CashboxTransactionID");
            entity.Property(e => e.CreatedAt).HasColumnType("datetime");
            entity.Property(e => e.CreatedBy).HasMaxLength(50);
            entity.Property(e => e.PaymentDate).HasColumnType("datetime");
            entity.Property(e => e.PaymentMethod).HasMaxLength(50);
            entity.Property(e => e.PaymentBracket).HasMaxLength(20);
            entity.Property(e => e.TransactionId).HasColumnName("TransactionID");

            entity.HasOne(d => d.Transaction).WithMany(p => p.Payments)
                .HasForeignKey(d => d.TransactionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Payments_Transactions");
        });

        modelBuilder.Entity<Payroll>(entity =>
        {
            entity.ToTable("Payroll");

            entity.Property(e => e.PayrollId)
                .HasColumnName("PayrollID");

            entity.Property(e => e.EmployeeId)
                .HasColumnName("EmployeeID");

            entity.Property(e => e.CashboxTransactionId)
                .HasColumnName("CashboxTransactionID");

            entity.Property(e => e.Allowances)
                .HasDefaultValue(0m)
                .HasColumnType("decimal(18, 2)");

            entity.Property(e => e.BasicSalary)
                .HasColumnType("decimal(18, 2)");

            entity.Property(e => e.Deductions)
                .HasDefaultValue(0m)
                .HasColumnType("decimal(18, 2)");

            entity.Property(e => e.NetSalary)
                .HasComputedColumnSql("(([BasicSalary]+ISNULL([BonusInPayroll],0))-ISNULL([Deductions],0))", true)
                .HasColumnType("decimal(20, 2)")
                .ValueGeneratedOnAddOrUpdate();

            entity.Property(e => e.Notes)
                .HasMaxLength(255);

            entity.Property(e => e.PaymentDate)
                .HasColumnType("datetime");

            entity.Property(e => e.PaymentStatus)
                .HasMaxLength(20)
                .HasDefaultValue("غير مدفوع");

            entity.Property(e => e.PayrollMonth)
                .HasMaxLength(7)
                .IsUnicode(false)
                .IsFixedLength();

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");

            entity.Property(e => e.CreatedBy)
                .HasMaxLength(50);

            // ── حقول جديدة ──────────────────────────────────
            entity.Property(e => e.BonusInPayroll)
                .HasColumnType("decimal(18, 2)");

            entity.Property(e => e.AbsenceDeduction)
                .HasColumnType("decimal(18, 2)");

            entity.Property(e => e.LateDeduction)
                .HasColumnType("decimal(18, 2)");

            entity.Property(e => e.LoanDeduction)
                .HasColumnType("decimal(18, 2)");

            entity.Property(e => e.PenaltyDeduction)
                .HasColumnType("decimal(18, 2)");

            entity.Property(e => e.PayrollRunId)
                .HasColumnName("PayrollRunID");

            // ── العلاقات ─────────────────────────────────────
            entity.HasOne(d => d.CashboxTransaction)
                .WithMany(p => p.Payrolls)
                .HasForeignKey(d => d.CashboxTransactionId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_Payroll_CashboxTransactions");

            entity.HasOne(d => d.Employee)
                .WithMany(p => p.Payrolls)
                .HasForeignKey(d => d.EmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Payroll_Employees");

            entity.HasOne(d => d.PayrollRun)
                .WithMany(p => p.Payrolls)
                .HasForeignKey(d => d.PayrollRunId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_Payroll_PayrollRuns");
        });

        modelBuilder.Entity<PayrollDetail>(entity =>
        {
            entity.Property(e => e.PayrollDetailID)
                .HasColumnName("PayrollDetailID");

            entity.Property(e => e.PayrollID)
                .HasColumnName("PayrollID");

            entity.Property(e => e.Amount)
                .HasColumnType("decimal(18, 2)");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");

            entity.Property(e => e.CreatedBy)
                .HasMaxLength(50);

            entity.Property(e => e.DetailDescription)
                .HasMaxLength(255);

            entity.Property(e => e.DetailType)
                .HasMaxLength(50);

            // ── حقول جديدة ──────────────────────────────────
            entity.Property(e => e.IsDeduction)
                .HasDefaultValue(true);

            entity.Property(e => e.PaymentType)
                .HasMaxLength(20)
                .HasDefaultValue("InPayroll");

            entity.Property(e => e.CashboxTransactionID)
                .HasColumnName("CashboxTransactionID");

            // ── العلاقات ─────────────────────────────────────
            entity.HasOne(d => d.Payroll)
                .WithMany(p => p.PayrollDetails)
                .HasForeignKey(d => d.PayrollID)
                .HasConstraintName("FK_PayrollDetails_Payroll");
        });

        // ── جداول جديدة ─────────────────────────────────────
        modelBuilder.Entity<PayrollRun>(entity =>
        {
            entity.HasKey(e => e.RunId);
            entity.Property(e => e.RunId)
        .HasColumnName("RunID");

    entity.Property(e => e.CashBoxId)
        .HasColumnName("CashBoxID");

            entity.Property(e => e.PayrollMonth)
                .HasMaxLength(7)
                .IsFixedLength();

            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Draft");

            entity.Property(e => e.TotalGross)
                .HasColumnType("decimal(18, 2)");

            entity.Property(e => e.TotalDeductions)
                .HasColumnType("decimal(18, 2)");

            entity.Property(e => e.TotalNet)
                .HasColumnType("decimal(18, 2)");

            entity.Property(e => e.ProcessedAt)
                .HasColumnType("datetime");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");

            entity.HasOne(d => d.CashBox)
                .WithMany()
                .HasForeignKey(d => d.CashBoxId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_PayrollRuns_CashBoxes");
        });

        modelBuilder.Entity<AttendanceManual>(entity =>
{
    entity.HasKey(e => e.ManualId);

    entity.ToTable("AttendanceManual");  // ✅ اسم الجدول الصح

    // ✅ mapping الأسماء زي ما هي في DB
    entity.Property(e => e.ManualId)
        .HasColumnName("ManualID");

    entity.Property(e => e.EmployeeId)
        .HasColumnName("EmployeeID");

    entity.Property(e => e.AttendanceMonth)
        .HasMaxLength(7)
        .IsFixedLength();

    entity.Property(e => e.Notes)
        .HasMaxLength(300);

    entity.Property(e => e.CreatedAt)
        .HasDefaultValueSql("(getdate())")
        .HasColumnType("datetime");

    entity.HasIndex(e => new { e.EmployeeId, e.AttendanceMonth })
        .IsUnique()
        .HasDatabaseName("UQ_AttendanceManual");

    entity.HasOne(d => d.Employee)
        .WithMany()
        .HasForeignKey(d => d.EmployeeId)
        .OnDelete(DeleteBehavior.ClientSetNull)
        .HasConstraintName("FK_AttendanceManual_Employees");
});

        modelBuilder.Entity<Permission>(entity =>
        {
            entity.HasKey(e => e.PermissionId).HasName("PK__Permissi__EFA6FB0F2F9E0245");

            entity.HasIndex(e => e.PermissionName, "UQ__Permissi__0FFDA357B4BCDBA6").IsUnique();

            entity.Property(e => e.PermissionId).HasColumnName("PermissionID");
            entity.Property(e => e.Category).HasMaxLength(50);
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CreatedBy).HasMaxLength(50);
            entity.Property(e => e.Description).HasMaxLength(255);
            entity.Property(e => e.FormName).HasMaxLength(50);
            entity.Property(e => e.PermissionName).HasMaxLength(50);
        });

        modelBuilder.Entity<PriceChangeRequest>(entity =>
        {
            entity.HasKey(e => e.RequestId);

            entity.HasIndex(e => e.ProductId, "IX_PriceChangeRequests_ProductID");

            entity.HasIndex(e => e.RequestedBy, "IX_PriceChangeRequests_RequestedBy");

            entity.HasIndex(e => e.Status, "IX_PriceChangeRequests_Status");

            entity.Property(e => e.RequestId).HasColumnName("RequestID");
            entity.Property(e => e.CurrentPrice).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.PriceType).HasMaxLength(20);
            entity.Property(e => e.ProductId).HasColumnName("ProductID");
            entity.Property(e => e.Reason).HasMaxLength(500);
            entity.Property(e => e.RequestedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.RequestedBy).HasMaxLength(100);
            entity.Property(e => e.RequestedPrice).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.ReviewNotes).HasMaxLength(500);
            entity.Property(e => e.ReviewedAt).HasColumnType("datetime");
            entity.Property(e => e.ReviewedBy).HasMaxLength(100);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Pending");

            entity.HasOne(d => d.Product).WithMany(p => p.PriceChangeRequests)
                .HasForeignKey(d => d.ProductId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PriceChangeRequests_Products");
        });

        modelBuilder.Entity<PriceHistory>(entity =>
        {
            entity.HasKey(e => e.HistoryId).HasName("PK__PriceHis__4D7B4ADD11AC4D5A");

            entity.ToTable("PriceHistory");

            entity.Property(e => e.HistoryId).HasColumnName("HistoryID");
            entity.Property(e => e.ChangeReason).HasMaxLength(255);
            entity.Property(e => e.ChangedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.ChangedBy).HasMaxLength(100);
            entity.Property(e => e.CustomerId).HasColumnName("CustomerID");
            entity.Property(e => e.NewPrice).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.OldPrice).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.PriceType).HasMaxLength(50);
            entity.Property(e => e.ProductId).HasColumnName("ProductID");

            entity.HasOne(d => d.Product).WithMany(p => p.PriceHistories)
                .HasForeignKey(d => d.ProductId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__PriceHist__Produ__52793849");
        });

        modelBuilder.Entity<PricingMargin>(entity =>
        {
            entity.HasKey(e => e.MarginId);

            entity.Property(e => e.MarginId).HasColumnName("MarginID");
            entity.Property(e => e.ChangeReason).HasMaxLength(255);
            entity.Property(e => e.ChangedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.ChangedBy).HasMaxLength(100);
            entity.Property(e => e.EliteMargin).HasColumnType("decimal(8, 2)");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.PremiumMargin).HasColumnType("decimal(8, 2)");
            entity.Property(e => e.PreviousElite)
                .HasDefaultValue(0m)
                .HasColumnType("decimal(8, 2)");
            entity.Property(e => e.PreviousPremium)
                .HasDefaultValue(0m)
                .HasColumnType("decimal(8, 2)");
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(e => e.ProductId).HasName("PK__Products__B40CC6ED028E1B45");

            entity.Property(e => e.ProductId).HasColumnName("ProductID");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CreatedBy).HasMaxLength(100);
            entity.Property(e => e.IsSelected).HasDefaultValue(false);
            entity.Property(e => e.PdfPath).HasMaxLength(500);
            entity.Property(e => e.Pdffile).HasColumnName("PDFFile");
            entity.Property(e => e.Period).HasDefaultValue(0);
            entity.Property(e => e.PricingStatusId)
                .HasDefaultValue(1)
                .HasColumnName("PricingStatusID");
            entity.Property(e => e.PricingType).HasMaxLength(50);
            entity.Property(e => e.ProductDescription).HasMaxLength(150);
            entity.Property(e => e.ProductGroupId).HasColumnName("ProductGroupID");
            entity.Property(e => e.ProductName).HasMaxLength(100);
            entity.Property(e => e.PurchasePrice)
                .HasDefaultValue(0m)
                .HasColumnType("decimal(18, 2)");
            entity.Property(e => e.PurchasePriceElite)
                .HasDefaultValue(0m)
                .HasColumnType("decimal(18, 2)");
            entity.Property(e => e.Qty)
                .HasDefaultValue(1)
                .HasColumnName("QTY");
            entity.Property(e => e.SuggestedSalePrice)
                .HasDefaultValue(0m)
                .HasColumnType("decimal(18, 2)");
            entity.Property(e => e.SuggestedSalePriceElite)
                .HasDefaultValue(0m)
                .HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.PricingStatus).WithMany(p => p.Products)
                .HasForeignKey(d => d.PricingStatusId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Products_ProductPricingStatus");

            entity.HasOne(d => d.ProductGroup).WithMany(p => p.Products)
                .HasForeignKey(d => d.ProductGroupId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Products__Produc__76969D2E");
        });

        modelBuilder.Entity<ProductComponent>(entity =>
        {
            entity.HasKey(e => e.ComponentId).HasName("PK__ProductC__D79CF02EB6358415");

            entity.Property(e => e.ComponentId).HasColumnName("ComponentID");
            entity.Property(e => e.ComponentName).HasMaxLength(100);
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CreatedBy).HasMaxLength(100);
            entity.Property(e => e.ProductId).HasColumnName("ProductID");

            entity.HasOne(d => d.Product).WithMany(p => p.ProductComponents)
                .HasForeignKey(d => d.ProductId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ProductCo__Produ__07C12930");
        });

        modelBuilder.Entity<ProductGroup>(entity =>
        {
            entity.HasKey(e => e.ProductGroupId).HasName("PK__ProductG__0F0D7B02D68CB565");

            entity.Property(e => e.ProductGroupId).HasColumnName("ProductGroupID");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CreatedBy).HasMaxLength(100);
            entity.Property(e => e.GroupName).HasMaxLength(100);
        });

        modelBuilder.Entity<ProductImage>(entity =>
        {
            entity.HasKey(e => e.ProductImagesId).HasName("PK__ProductI__5DEF22CAC0A52D8E");

            entity.Property(e => e.ProductImagesId).HasColumnName("ProductImagesID");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.ImageNote).HasMaxLength(255);
            entity.Property(e => e.ImagePath).HasMaxLength(500);
            entity.Property(e => e.ProductId).HasColumnName("ProductID");

            entity.HasOne(d => d.Product).WithMany(p => p.ProductImages)
                .HasForeignKey(d => d.ProductId)
                .HasConstraintName("FK_ProductImages_Products");
        });

        modelBuilder.Entity<ProductPricingStatus>(entity =>
        {
            entity.HasKey(e => e.PricingStatusId).HasName("PK__ProductP__F31F436F401C0457");

            entity.ToTable("ProductPricingStatus");

            entity.Property(e => e.PricingStatusId)
                .ValueGeneratedNever()
                .HasColumnName("PricingStatusID");
            entity.Property(e => e.Description).HasMaxLength(255);
            entity.Property(e => e.StatusName).HasMaxLength(100);
        });

        modelBuilder.Entity<ProductPricingTransition>(entity =>
        {
            entity.HasKey(e => e.TransitionId).HasName("PK__ProductP__54F04847C30A08B7");

            entity.Property(e => e.TransitionId).HasColumnName("TransitionID");
            entity.Property(e => e.AllowedRole).HasMaxLength(50);
            entity.Property(e => e.AutoNotify).HasDefaultValue(true);
            entity.Property(e => e.FromStatusId).HasColumnName("FromStatusID");
            entity.Property(e => e.ToStatusId).HasColumnName("ToStatusID");

            entity.HasOne(d => d.FromStatus).WithMany(p => p.ProductPricingTransitionFromStatuses)
                .HasForeignKey(d => d.FromStatusId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Transitions_FromStatus");

            entity.HasOne(d => d.ToStatus).WithMany(p => p.ProductPricingTransitionToStatuses)
                .HasForeignKey(d => d.ToStatusId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Transitions_ToStatus");
        });

        modelBuilder.Entity<Quotation>(entity =>
        {
            entity.HasKey(e => e.QuotationId).HasName("PK__Quotatio__E19752B3807A4526");

            entity.Property(e => e.QuotationId).HasColumnName("QuotationID");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CreatedBy).HasMaxLength(100);
            entity.Property(e => e.GrandTotal)
                .HasDefaultValue(0m)
                .HasColumnType("decimal(18, 2)");
            entity.Property(e => e.InvoiceId).HasColumnName("InvoiceID");
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.Property(e => e.PartyId).HasColumnName("PartyID");
            entity.Property(e => e.PricingType)
                .HasMaxLength(50)
                .HasColumnName("pricingType");
            entity.Property(e => e.QuotationDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.TotalAmount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.WarehouseId).HasColumnName("WarehouseID");
            entity.Property(e => e.Status)
        .IsRequired()
        .HasMaxLength(50)
        .HasDefaultValue("Draft");

    entity.Property(e => e.DiscountAmount)
        .HasColumnType("decimal(18, 2)")
        .HasDefaultValue(null);

    entity.HasIndex(e => e.Status);
        });

        modelBuilder.Entity<QuotationDetail>(entity =>
        {
            entity.HasKey(e => e.QuotationDetailId).HasName("PK__Quotatio__0CEE6A82898ABA3E");

            entity.Property(e => e.QuotationDetailId).HasColumnName("QuotationDetailID");
            entity.Property(e => e.Notes).HasMaxLength(255);
            entity.Property(e => e.ProductId).HasColumnName("ProductID");
            entity.Property(e => e.Quantity).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.QuotationId).HasColumnName("QuotationID");
            entity.Property(e => e.TotalAmount)
                .HasComputedColumnSql("([Quantity]*[UnitPrice])", true)
                .HasColumnType("decimal(37, 4)");
            entity.Property(e => e.UnitPrice).HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.Quotation).WithMany(p => p.QuotationDetails)
                .HasForeignKey(d => d.QuotationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Quotation__Quota__056ECC6A");
        });

        modelBuilder.Entity<ReferralSource>(entity =>
        {
            entity.HasKey(e => e.ReferralSourceId).HasName("PK__dbo_Refe__E425C93659247F00");

            entity.HasIndex(e => e.SourceName, "UQ__dbo_Refe__3C28DC179D1B0D0F").IsUnique();

            entity.Property(e => e.ReferralSourceId).HasColumnName("ReferralSourceID");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.SourceName).HasMaxLength(100);
        });

        modelBuilder.Entity<SalaryHistory>(entity =>
        {
            entity.HasKey(e => e.SalaryHistoryId).HasName("PK__SalaryHi__25D843D68CC15DB5");

            entity.ToTable("SalaryHistory");

            entity.Property(e => e.SalaryHistoryId).HasColumnName("SalaryHistoryID");
            entity.Property(e => e.ChangeDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CreatedBy).HasMaxLength(50);
            entity.Property(e => e.EmployeeId).HasColumnName("EmployeeID");
            entity.Property(e => e.NewSalary).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.OldSalary).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.Reason).HasMaxLength(255);

            entity.HasOne(d => d.Employee).WithMany(p => p.SalaryHistories)
                .HasForeignKey(d => d.EmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__SalaryHis__Emplo__2BC97F7C");
        });

        modelBuilder.Entity<SalesOpportunity>(entity =>
        {
            entity.HasKey(e => e.OpportunityId);

            entity.ToTable(tb => tb.HasTrigger("TRG_Audit_SalesOpportunities"));

            entity.HasIndex(e => e.EmployeeId, "IX_SalesOpportunities_EmployeeID");

            entity.HasIndex(e => e.FirstContactDate, "IX_SalesOpportunities_FirstContactDate");

            entity.HasIndex(e => e.NextFollowUpDate, "IX_SalesOpportunities_NextFollowUp");

            entity.HasIndex(e => e.PartyId, "IX_SalesOpportunities_PartyID");

            entity.HasIndex(e => e.StageId, "IX_SalesOpportunities_StageID");

            entity.Property(e => e.OpportunityId).HasColumnName("OpportunityID");
            entity.Property(e => e.AdTypeId).HasColumnName("AdTypeID");
            entity.Property(e => e.CategoryId).HasColumnName("CategoryID");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CreatedBy).HasMaxLength(50);
            entity.Property(e => e.EmployeeId).HasColumnName("EmployeeID");
            entity.Property(e => e.ExpectedValue).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.FirstContactDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.Guidance).HasMaxLength(500);
            entity.Property(e => e.InterestedProduct).HasMaxLength(200);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.LastContactDate).HasColumnType("datetime");
            entity.Property(e => e.LastUpdatedAt).HasColumnType("datetime");
            entity.Property(e => e.LastUpdatedBy).HasMaxLength(50);
            entity.Property(e => e.Location).HasMaxLength(200);
            entity.Property(e => e.LostNotes).HasMaxLength(500);
            entity.Property(e => e.LostReasonId).HasColumnName("LostReasonID");
            entity.Property(e => e.NextFollowUpDate).HasColumnType("datetime");
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.Property(e => e.PartyId).HasColumnName("PartyID");
            entity.Property(e => e.QuotationId).HasColumnName("QuotationID");
            entity.Property(e => e.SourceId).HasColumnName("SourceID");
            entity.Property(e => e.StageId)
                .HasDefaultValue(1)
                .HasColumnName("StageID");
            entity.Property(e => e.StatusId).HasColumnName("StatusID");
            entity.Property(e => e.TransactionId).HasColumnName("TransactionID");

            entity.HasOne(d => d.AdType).WithMany(p => p.SalesOpportunities)
                .HasForeignKey(d => d.AdTypeId)
                .HasConstraintName("FK_SalesOpportunities_AdTypes");

            entity.HasOne(d => d.Category).WithMany(p => p.SalesOpportunities)
                .HasForeignKey(d => d.CategoryId)
                .HasConstraintName("FK_SalesOpportunities_Categories");

            entity.HasOne(d => d.Employee).WithMany(p => p.SalesOpportunities)
                .HasForeignKey(d => d.EmployeeId)
                .HasConstraintName("FK_SalesOpportunities_Employees");

            entity.HasOne(d => d.LostReason).WithMany(p => p.SalesOpportunities)
                .HasForeignKey(d => d.LostReasonId)
                .HasConstraintName("FK_SalesOpportunities_LostReasons");

            entity.HasOne(d => d.Party).WithMany(p => p.SalesOpportunities)
                .HasForeignKey(d => d.PartyId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SalesOpportunities_Parties");

            entity.HasOne(d => d.Quotation).WithMany(p => p.SalesOpportunities)
                .HasForeignKey(d => d.QuotationId)
                .HasConstraintName("FK_SalesOpportunities_Quotations");

            entity.HasOne(d => d.Source).WithMany(p => p.SalesOpportunities)
                .HasForeignKey(d => d.SourceId)
                .HasConstraintName("FK_SalesOpportunities_Sources");

            entity.HasOne(d => d.Stage).WithMany(p => p.SalesOpportunities)
                .HasForeignKey(d => d.StageId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SalesOpportunities_Stages");

            entity.HasOne(d => d.Status).WithMany(p => p.SalesOpportunities)
                .HasForeignKey(d => d.StatusId)
                .HasConstraintName("FK_SalesOpportunities_Status");

            entity.HasOne(d => d.Transaction).WithMany(p => p.SalesOpportunities)
                .HasForeignKey(d => d.TransactionId)
                .HasConstraintName("FK_SalesOpportunities_Transactions");
        });

        modelBuilder.Entity<SalesStage>(entity =>
        {
            entity.HasKey(e => e.StageId);

            entity.Property(e => e.StageId).HasColumnName("StageID");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CreatedBy).HasMaxLength(50);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.LastUpdatedAt).HasColumnType("datetime");
            entity.Property(e => e.LastUpdatedBy).HasMaxLength(50);
            entity.Property(e => e.StageColor).HasMaxLength(20);
            entity.Property(e => e.StageName).HasMaxLength(50);
            entity.Property(e => e.StageNameAr).HasMaxLength(50);
        });

        modelBuilder.Entity<ShortPermission>(entity =>
        {
            entity.HasKey(e => e.PermissionId).HasName("PK__ShortPer__EFA6FB0FD5121DBF");

            entity.HasIndex(e => new { e.EmployeeId, e.PermissionDate, e.PermissionType }, "UQ_Employee_Date_Type").IsUnique();

            entity.Property(e => e.PermissionId).HasColumnName("PermissionID");
            entity.Property(e => e.ApprovalDate).HasColumnType("datetime");
            entity.Property(e => e.ApprovedByUserId).HasColumnName("ApprovedByUserID");
            entity.Property(e => e.AttendanceId).HasColumnName("AttendanceID");
            entity.Property(e => e.CreatedAt).HasColumnType("datetime");
            entity.Property(e => e.EmployeeId).HasColumnName("EmployeeID");
            entity.Property(e => e.ManagerComment).HasMaxLength(255);
            entity.Property(e => e.PermissionType).HasMaxLength(50);
            entity.Property(e => e.Reason).HasMaxLength(255);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Pending");
        });

        modelBuilder.Entity<StockLevel>(entity =>
        {
            entity.HasKey(e => e.StockLevelId).HasName("PK__StockLev__573D8053DCB218F4");

            entity.Property(e => e.StockLevelId).HasColumnName("StockLevelID");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CreatedBy).HasMaxLength(100);
            entity.Property(e => e.LastUpdatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.ProductId).HasColumnName("ProductID");
            entity.Property(e => e.WarehouseId).HasColumnName("WarehouseID");

            entity.HasOne(d => d.Product).WithMany(p => p.StockLevels)
                .HasForeignKey(d => d.ProductId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__StockLeve__Produ__7D439ABD");

            entity.HasOne(d => d.Warehouse).WithMany(p => p.StockLevels)
                .HasForeignKey(d => d.WarehouseId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__StockLeve__Wareh__7E37BEF6");
        });

        modelBuilder.Entity<StockTransaction>(entity =>
        {
            entity.HasKey(e => e.TransactionId).HasName("PK__StockTra__55433A4B3C9B820C");

            entity.Property(e => e.TransactionId).HasColumnName("TransactionID");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CreatedBy).HasMaxLength(100);
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.Property(e => e.ProductId).HasColumnName("ProductID");
            entity.Property(e => e.ReferenceId).HasColumnName("ReferenceID");
            entity.Property(e => e.ReferenceType).HasMaxLength(50);
            entity.Property(e => e.TotalAmount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.TransactionDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.TransactionType).HasMaxLength(50);
            entity.Property(e => e.UnitPrice).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.WarehouseId).HasColumnName("WarehouseID");

            entity.HasOne(d => d.Product).WithMany(p => p.StockTransactions)
                .HasForeignKey(d => d.ProductId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__StockTran__Produ__02FC7413");

            entity.HasOne(d => d.Warehouse).WithMany(p => p.StockTransactions)
                .HasForeignKey(d => d.WarehouseId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__StockTran__Wareh__03F0984C");
        });

        modelBuilder.Entity<TaskType>(entity =>
        {
            entity.Property(e => e.TaskTypeId).HasColumnName("TaskTypeID");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CreatedBy).HasMaxLength(50);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.LastUpdatedAt).HasColumnType("datetime");
            entity.Property(e => e.LastUpdatedBy).HasMaxLength(50);
            entity.Property(e => e.TaskTypeName).HasMaxLength(50);
            entity.Property(e => e.TaskTypeNameAr).HasMaxLength(50);
        });

        modelBuilder.Entity<TempInvoicePrintDetail>(entity =>
        {
            entity.HasKey(e => e.TempId).HasName("PK__Temp_Inv__06C703E1E417584F");

            entity.ToTable("Temp_InvoicePrintDetails");

            entity.Property(e => e.TempId).HasColumnName("TempID");
            entity.Property(e => e.DisplayName).HasMaxLength(250);
            entity.Property(e => e.ItemType).HasMaxLength(20);
            entity.Property(e => e.LineTotal)
                .HasComputedColumnSql("([Quantity]*[UnitPrice])", true)
                .HasColumnType("decimal(37, 4)");
            entity.Property(e => e.Notes).HasMaxLength(200);
            entity.Property(e => e.ProductId).HasColumnName("ProductID");
            entity.Property(e => e.ProductName).HasMaxLength(200);
            entity.Property(e => e.Quantity).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.TransactionId).HasColumnName("TransactionID");
            entity.Property(e => e.UnitPrice).HasColumnType("decimal(18, 2)");
        });

        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasKey(e => e.TransactionId).HasName("PK__Transact__55433A4B548B617A");

            entity.Property(e => e.TransactionId).HasColumnName("TransactionID");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CreatedBy).HasMaxLength(50);
            entity.Property(e => e.DiscountAmount)
                .HasDefaultValue(0m)
                .HasColumnType("decimal(18, 2)");
            entity.Property(e => e.DiscountPercentage)
                .HasDefaultValue(0m)
                .HasColumnType("decimal(5, 2)");
            entity.Property(e => e.DueDate).HasColumnType("datetime");
            entity.Property(e => e.EditAt).HasColumnType("datetime");
            entity.Property(e => e.EditBy).HasMaxLength(50);
            entity.Property(e => e.EditDone).HasMaxLength(50);
            entity.Property(e => e.EditReason).HasMaxLength(500);
            entity.Property(e => e.EditRequestDate).HasColumnType("datetime");
            entity.Property(e => e.EditStatus).HasDefaultValue(0);
            entity.Property(e => e.GrandTotal).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.InvoiceStatus).HasMaxLength(50);
            entity.Property(e => e.IsDelivered).HasDefaultValue(false);
            entity.Property(e => e.NetTotalAmount)
                .HasDefaultValue(0m)
                .HasColumnType("decimal(18, 2)");
            entity.Property(e => e.Notes).HasMaxLength(255);
            entity.Property(e => e.PaidAmount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.PartyId).HasColumnName("PartyID");
            entity.Property(e => e.PaymentMethod).HasMaxLength(20);
            entity.Property(e => e.ReferenceNumber).HasMaxLength(50);
            entity.Property(e => e.ReferenceType).HasMaxLength(20);
            entity.Property(e => e.TotalAmount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.TotalChargesAmount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.TransactionDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.TransactionType)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.WarehouseId).HasColumnName("WarehouseID");
        });

        modelBuilder.Entity<TransactionDetail>(entity =>
        {
            entity.HasKey(e => e.DetailId).HasName("PK__Transact__135C314D32A2B70C");

            entity.Property(e => e.DetailId).HasColumnName("DetailID");
            entity.Property(e => e.Notes).HasMaxLength(255);
            entity.Property(e => e.ProductId).HasColumnName("ProductID");
            entity.Property(e => e.Quantity).HasColumnType("decimal(18, 3)");
            entity.Property(e => e.TotalAmount)
                .HasComputedColumnSql("([Quantity]*[UnitPrice])", true)
                .HasColumnType("decimal(37, 5)");
            entity.Property(e => e.TransactionId).HasColumnName("TransactionID");
            entity.Property(e => e.UnitPrice).HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.Product).WithMany(p => p.TransactionDetails)
                .HasForeignKey(d => d.ProductId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_TransactionDetails_Products");

            entity.HasOne(d => d.Transaction).WithMany(p => p.TransactionDetails)
                .HasForeignKey(d => d.TransactionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_TransactionDetails_Transactions");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("PK__Users__1788CCAC0E51360C");

            entity.HasIndex(e => new { e.Username, e.IsActive }, "IX_Users_Username_IsActive");

            entity.HasIndex(e => e.Username, "UQ__Users__536C85E4986300E5").IsUnique();

            entity.Property(e => e.UserId).HasColumnName("UserID");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CreatedBy).HasMaxLength(50);
            entity.Property(e => e.Email).HasMaxLength(100);
            entity.Property(e => e.EmployeeId).HasColumnName("employeeID");
            entity.Property(e => e.Fcmtoken)
                .HasMaxLength(255)
                .HasColumnName("FCMToken");
            entity.Property(e => e.FullName).HasMaxLength(100);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.LastLogin).HasColumnType("datetime");
            entity.Property(e => e.Password).HasMaxLength(100);
            entity.Property(e => e.HashedPassword).HasMaxLength(500);
            entity.Property(e => e.Role)
                .HasMaxLength(50)
                .HasDefaultValue("User");
            entity.Property(e => e.Username).HasMaxLength(50);
            entity.Property(e => e.CrmAccessFromDate).HasColumnType("date");
        });

        modelBuilder.Entity<UserPermission>(entity =>
        {
            entity.HasKey(e => e.UserPermissionId).HasName("PK__UserPerm__A90F88D2617F4714");

            entity.Property(e => e.UserPermissionId).HasColumnName("UserPermissionID");
            entity.Property(e => e.AssignedBy).HasMaxLength(50);
            entity.Property(e => e.AssignedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.PermissionId).HasColumnName("PermissionID");
            entity.Property(e => e.UserId).HasColumnName("UserID");

            entity.HasOne(d => d.Permission).WithMany(p => p.UserPermissions)
                .HasForeignKey(d => d.PermissionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UserPermissions_Permissions");

            entity.HasOne(d => d.User).WithMany(p => p.UserPermissions)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UserPermissions_Users");
        });

        modelBuilder.Entity<VwComplaintsList>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_Complaints_List");

            entity.Property(e => e.ClientName).HasMaxLength(200);
            entity.Property(e => e.ClientPhone).HasMaxLength(50);
            entity.Property(e => e.ComplaintDate).HasColumnType("datetime");
            entity.Property(e => e.ComplaintId).HasColumnName("ComplaintID");
            entity.Property(e => e.ComplaintType).HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasColumnType("datetime");
            entity.Property(e => e.CreatedBy).HasMaxLength(50);
            entity.Property(e => e.EmployeeName).HasMaxLength(150);
            entity.Property(e => e.EscalatedTo).HasMaxLength(100);
            entity.Property(e => e.OpportunityId).HasColumnName("OpportunityID");
            entity.Property(e => e.PartyId).HasColumnName("PartyID");
            entity.Property(e => e.PriorityName).HasMaxLength(13);
            entity.Property(e => e.SolvedDate).HasColumnType("datetime");
            entity.Property(e => e.StatusName).HasMaxLength(13);
            entity.Property(e => e.Subject).HasMaxLength(255);
            entity.Property(e => e.TypeId).HasColumnName("TypeID");
        });

        modelBuilder.Entity<VwCrmTask>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_CRM_Tasks");

            entity.Property(e => e.AssignedToName).HasMaxLength(150);
            entity.Property(e => e.ClientName).HasMaxLength(200);
            entity.Property(e => e.CompletedBy).HasMaxLength(50);
            entity.Property(e => e.CompletedDate).HasColumnType("datetime");
            entity.Property(e => e.CompletionNotes).HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasColumnType("datetime");
            entity.Property(e => e.CreatedBy).HasMaxLength(50);
            entity.Property(e => e.DueDate).HasColumnType("datetime");
            entity.Property(e => e.DueTime).HasPrecision(0);
            entity.Property(e => e.OpportunityId).HasColumnName("OpportunityID");
            entity.Property(e => e.PartyId).HasColumnName("PartyID");
            entity.Property(e => e.Phone).HasMaxLength(50);
            entity.Property(e => e.Priority).HasMaxLength(20);
            entity.Property(e => e.Status).HasMaxLength(20);
            entity.Property(e => e.TaskDescription).HasMaxLength(500);
            entity.Property(e => e.TaskDueStatus).HasMaxLength(9);
            entity.Property(e => e.TaskId).HasColumnName("TaskID");
            entity.Property(e => e.TaskTypeId).HasColumnName("TaskTypeID");
            entity.Property(e => e.TaskTypeName).HasMaxLength(50);
            entity.Property(e => e.TaskTypeNameAr).HasMaxLength(50);
        });

        modelBuilder.Entity<VwCustomerInteraction>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_CustomerInteractions");

            entity.Property(e => e.ClientName).HasMaxLength(200);
            entity.Property(e => e.CreatedAt).HasColumnType("datetime");
            entity.Property(e => e.CreatedBy).HasMaxLength(50);
            entity.Property(e => e.EditAt).HasColumnType("datetime");
            entity.Property(e => e.EditBy).HasMaxLength(50);
            entity.Property(e => e.EmployeeId).HasColumnName("EmployeeID");
            entity.Property(e => e.EmployeeName).HasMaxLength(150);
            entity.Property(e => e.InteractionDate).HasColumnType("datetime");
            entity.Property(e => e.InteractionId).HasColumnName("InteractionID");
            entity.Property(e => e.InteractionTime).HasPrecision(0);
            entity.Property(e => e.NextFollowUpDate).HasColumnType("datetime");
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.Property(e => e.OpportunityId).HasColumnName("OpportunityID");
            entity.Property(e => e.PartyId).HasColumnName("PartyID");
            entity.Property(e => e.Phone).HasMaxLength(50);
            entity.Property(e => e.SourceIcon).HasMaxLength(10);
            entity.Property(e => e.SourceId).HasColumnName("SourceID");
            entity.Property(e => e.SourceName).HasMaxLength(50);
            entity.Property(e => e.StageAfterId).HasColumnName("StageAfterID");
            entity.Property(e => e.StageAfterName).HasMaxLength(50);
            entity.Property(e => e.StageBeforeId).HasColumnName("StageBeforeID");
            entity.Property(e => e.StageBeforeName).HasMaxLength(50);
            entity.Property(e => e.StatusId).HasColumnName("StatusID");
            entity.Property(e => e.StatusName).HasMaxLength(50);
            entity.Property(e => e.StatusNameAr).HasMaxLength(50);
            entity.Property(e => e.Summary).HasMaxLength(1000);
        });

        modelBuilder.Entity<VwPriceChangeRequest>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_PriceChangeRequests");

            entity.Property(e => e.CurrentMarginPercent).HasColumnType("decimal(38, 15)");
            entity.Property(e => e.CurrentPrice).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.GroupName).HasMaxLength(100);
            entity.Property(e => e.PriceDifference).HasColumnType("decimal(19, 2)");
            entity.Property(e => e.PriceType).HasMaxLength(20);
            entity.Property(e => e.ProductDescription).HasMaxLength(150);
            entity.Property(e => e.ProductId).HasColumnName("ProductID");
            entity.Property(e => e.ProductName).HasMaxLength(100);
            entity.Property(e => e.Reason).HasMaxLength(500);
            entity.Property(e => e.RequestId).HasColumnName("RequestID");
            entity.Property(e => e.RequestedAt).HasColumnType("datetime");
            entity.Property(e => e.RequestedBy).HasMaxLength(100);
            entity.Property(e => e.RequestedMarginPercent).HasColumnType("decimal(38, 15)");
            entity.Property(e => e.RequestedPrice).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.ReviewNotes).HasMaxLength(500);
            entity.Property(e => e.ReviewedAt).HasColumnType("datetime");
            entity.Property(e => e.ReviewedBy).HasMaxLength(100);
            entity.Property(e => e.Status).HasMaxLength(20);
        });

        modelBuilder.Entity<VwSalesDeliveryStatus>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_SalesDeliveryStatus");

            entity.Property(e => e.DeliveryStatus).HasMaxLength(11);
            entity.Property(e => e.DiscountAmount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.DiscountPercentage).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.DueDate).HasColumnType("datetime");
            entity.Property(e => e.EmployeeName).HasMaxLength(150);
            entity.Property(e => e.GrandTotal).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.NetTotalAmount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.PaidAmount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.PartyId).HasColumnName("PartyID");
            entity.Property(e => e.PartyName).HasMaxLength(200);
            entity.Property(e => e.TotalAmount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.TotalChargesAmount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.TransactionDate).HasColumnType("datetime");
            entity.Property(e => e.TransactionId).HasColumnName("TransactionID");
            entity.Property(e => e.TransactionType)
                .HasMaxLength(20)
                .IsUnicode(false);
        });

        modelBuilder.Entity<VwSalesOpportunity>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_SalesOpportunities");

            entity.Property(e => e.AdTypeId).HasColumnName("AdTypeID");
            entity.Property(e => e.AdTypeName).HasMaxLength(100);
            entity.Property(e => e.AdTypeNameAr).HasMaxLength(100);
            entity.Property(e => e.Address).HasMaxLength(250);
            entity.Property(e => e.CategoryId).HasColumnName("CategoryID");
            entity.Property(e => e.CategoryName).HasMaxLength(100);
            entity.Property(e => e.CategoryNameAr).HasMaxLength(100);
            entity.Property(e => e.ClientName).HasMaxLength(200);
            entity.Property(e => e.CreatedAt).HasColumnType("datetime");
            entity.Property(e => e.CreatedBy).HasMaxLength(50);
            entity.Property(e => e.EmployeeId).HasColumnName("EmployeeID");
            entity.Property(e => e.EmployeeName).HasMaxLength(150);
            entity.Property(e => e.ExpectedValue).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.FirstContactDate).HasColumnType("datetime");
            entity.Property(e => e.FollowUpStatus).HasMaxLength(8);
            entity.Property(e => e.Guidance).HasMaxLength(500);
            entity.Property(e => e.InterestedProduct).HasMaxLength(200);
            entity.Property(e => e.LastContactDate).HasColumnType("datetime");
            entity.Property(e => e.LastUpdatedAt).HasColumnType("datetime");
            entity.Property(e => e.LastUpdatedBy).HasMaxLength(50);
            entity.Property(e => e.Location).HasMaxLength(200);
            entity.Property(e => e.LostNotes).HasMaxLength(500);
            entity.Property(e => e.LostReasonId).HasColumnName("LostReasonID");
            entity.Property(e => e.LostReasonName).HasMaxLength(100);
            entity.Property(e => e.LostReasonNameAr).HasMaxLength(100);
            entity.Property(e => e.NextFollowUpDate).HasColumnType("datetime");
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.Property(e => e.OpportunityId).HasColumnName("OpportunityID");
            entity.Property(e => e.PartyId).HasColumnName("PartyID");
            entity.Property(e => e.Phone1).HasMaxLength(50);
            entity.Property(e => e.Phone2).HasMaxLength(50);
            entity.Property(e => e.QuotationId).HasColumnName("QuotationID");
            entity.Property(e => e.SourceIcon).HasMaxLength(10);
            entity.Property(e => e.SourceId).HasColumnName("SourceID");
            entity.Property(e => e.SourceName).HasMaxLength(50);
            entity.Property(e => e.SourceNameAr).HasMaxLength(50);
            entity.Property(e => e.StageColor).HasMaxLength(20);
            entity.Property(e => e.StageId).HasColumnName("StageID");
            entity.Property(e => e.StageName).HasMaxLength(50);
            entity.Property(e => e.StageNameAr).HasMaxLength(50);
            entity.Property(e => e.StatusId).HasColumnName("StatusID");
            entity.Property(e => e.StatusName).HasMaxLength(50);
            entity.Property(e => e.StatusNameAr).HasMaxLength(50);
            entity.Property(e => e.TransactionId).HasColumnName("TransactionID");
            entity.Property(e => e.Username).HasMaxLength(50);
        });

        modelBuilder.Entity<Warehouse>(entity =>
        {
            entity.HasKey(e => e.WarehouseId).HasName("PK__Warehous__2608AFD96528B9B3");

            entity.Property(e => e.WarehouseId).HasColumnName("WarehouseID");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CreatedBy).HasMaxLength(100);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Location).HasMaxLength(200);
            entity.Property(e => e.Notes).HasMaxLength(150);
            entity.Property(e => e.WarehouseName).HasMaxLength(100);
        });
        modelBuilder.Entity<ComplaintAttachment>(entity =>
{
    entity.HasKey(e => e.AttachmentId);
    entity.ToTable("ComplaintAttachments");

    entity.Property(e => e.FileName).HasMaxLength(255);
    entity.Property(e => e.OriginalFileName).HasMaxLength(255);
    entity.Property(e => e.FilePath).HasMaxLength(500);
    entity.Property(e => e.MimeType).HasMaxLength(100);
    entity.Property(e => e.UploadedAt)
        .HasDefaultValueSql("(getdate())")
        .HasColumnType("datetime");

    entity.HasOne(d => d.Complaint).WithMany(p => p.ComplaintAttachments)
        .HasForeignKey(d => d.ComplaintId)
        .OnDelete(DeleteBehavior.Cascade)
        .HasConstraintName("FK_Attachments_Complaint");

    entity.HasOne(d => d.UploadedBy).WithMany()
        .HasForeignKey(d => d.UploadedByUserId)
        .HasConstraintName("FK_Attachments_User");
});

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}