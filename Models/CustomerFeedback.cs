using System;

namespace COCOBOLOERPNEW.Models;

/// <summary>
/// سجل فيدباك خدمة العملاء (اتصال بأخذ فيدباك بعد زيارة المعرض /
/// بعد تسليم الفواتير / عند وجود شكوى).
/// </summary>
public partial class CustomerFeedback
{
    public int      FeedbackId         { get; set; }
    public string   FeedbackType       { get; set; } = null!;   // Visit | Delivery | ComplaintFollowUp
    public int      PartyId            { get; set; }
    public int?     OpportunityId      { get; set; }
    public int?     TransactionId      { get; set; }
    public int?     ComplaintId        { get; set; }
    public int?     VisitInteractionId { get; set; }
    public DateTime? SourceDate        { get; set; }
    public string?  ClientName         { get; set; }
    public string?  Phone              { get; set; }
    public string?  Channel            { get; set; }            // اتصال | واتساب | ...
    public string   Status             { get; set; } = "Open";  // Open | Done | Skipped
    public string?  Outcome            { get; set; }            // Positive | Negative | NoAnswer | NotInterested | CallbackLater
    public byte?    Satisfaction       { get; set; }            // 1..5
    public string?  Notes              { get; set; }
    public string?  EmployeeNotes      { get; set; }   // انطباع الموظف (ملاحظة داخلية)
    public DateTime? NextCallDate      { get; set; }
    public int?     AssignedTo         { get; set; }            // EmployeeId
    public string?  CreatedBy          { get; set; }
    public DateTime CreatedAt          { get; set; }
    public string?  CompletedBy        { get; set; }
    public DateTime? CompletedAt       { get; set; }
    public string?  UpdatedBy          { get; set; }
    public DateTime? UpdatedAt         { get; set; }
    public bool     IsActive           { get; set; } = true;
}
