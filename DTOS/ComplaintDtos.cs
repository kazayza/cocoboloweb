using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace COCOBOLOERPNEW.DTOs;

#region 🔢 الثوابت (Priority / Status)

public static class ComplaintPriority
{
    public const byte VeryHigh = 1;
    public const byte High     = 2;
    public const byte Medium   = 3;
    public const byte Low      = 4;

    public static string ToText(byte? p) => p switch
    {
        1 => "عالية جداً 🔥",
        2 => "عالية 🔴",
        3 => "متوسطة 🟡",
        4 => "منخفضة 🟢",
        _ => "غير محدد"
    };

    public static (string Bg, string Fg) ToColors(byte? p) => p switch
    {
        1 => ("#d32f2f", "white"),
        2 => ("#f57c00", "white"),
        3 => ("#fbc02d", "black"),
        4 => ("#43a047", "white"),
        _ => ("#9e9e9e", "white")
    };
}

public static class ComplaintStatus
{
    public const byte New           = 1;
    public const byte InProgress    = 2;
    public const byte AwaitingClient = 3;
    public const byte Resolved      = 4;
    public const byte Rejected      = 5;
    public const byte Escalated     = 6;

    public static string ToText(byte? s) => s switch
    {
        1 => "جديدة",
        2 => "قيد الحل",
        3 => "انتظار العميل",
        4 => "تم الحل ✅",
        5 => "مرفوضة ❌",
        6 => "مصعدة ⚠️",
        _ => "غير معروف"
    };

    public static bool IsClosed(byte? s) => s is 4 or 5;
}

#endregion

#region 📝 الشكوى (Form / List / Detail)

public class ComplaintFormDto
{
    public int? ComplaintId { get; set; }

    [Required(ErrorMessage = "العميل مطلوب")]
    public int PartyId { get; set; }

    [Required(ErrorMessage = "نوع الشكوى مطلوب")]
    public int TypeId { get; set; }

    public int? TransactionId  { get; set; }
    public int? ProductId      { get; set; }
    public int? OpportunityId  { get; set; }

    [Required(ErrorMessage = "موضوع الشكوى مطلوب")]
    [StringLength(255)]
    public string Subject { get; set; } = "";

    [Required(ErrorMessage = "تفاصيل الشكوى مطلوبة")]
    [MinLength(5, ErrorMessage = "التفاصيل قصيرة جداً")]
    public string Details { get; set; } = "";

    [Range(1, 4)]
    public byte Priority { get; set; } = 2;

    public int? AssignedTo { get; set; }
}

public class ComplaintListItemDto
{
    public int       ComplaintId      { get; set; }
    public DateTime? ComplaintDate    { get; set; }
    public int       PartyId          { get; set; }
    public string?   ClientName       { get; set; }
    public string?   ClientPhone      { get; set; }
    public int       TypeId           { get; set; }
    public string?   ComplaintType    { get; set; }
    public string    Subject          { get; set; } = "";
    public byte?     Priority         { get; set; }
    public string?   PriorityName     { get; set; }
    public byte?     Status           { get; set; }
    public string?   StatusName       { get; set; }
    public int?      AssignedTo       { get; set; }
    public string?   EmployeeName     { get; set; }
    public DateTime? SolvedDate       { get; set; }
    public int?      DaysOpen         { get; set; }
    public bool?     Escalated        { get; set; }
    public int?      TransactionId    { get; set; }
    public int?      ProductId        { get; set; }
    public string?   ProductName      { get; set; }
    public byte?     SatisfactionLevel { get; set; }
    public int       AttachmentsCount { get; set; }
    public int       FollowUpsCount   { get; set; }
}

public class ComplaintFilterDto
{
    public DateTime? DateFrom        { get; set; }
    public DateTime? DateTo          { get; set; }
    public int?      PartyId         { get; set; }
    public string?   SearchText      { get; set; }
    public int?      TypeId          { get; set; }
    public byte?     Status          { get; set; }
    public byte?     Priority        { get; set; }
    public int?      AssignedTo      { get; set; }
    public bool      MineOnly        { get; set; }   // للموظف: شكاوى أنا اللي عاملها
    public bool      OpenOnly        { get; set; }   // غير المغلقة
    public int       Page            { get; set; } = 1;
    public int       PageSize        { get; set; } = 25;
}

public class PagedComplaintsDto
{
    public List<ComplaintListItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page       { get; set; }
    public int PageSize   { get; set; }
}

public class ComplaintDetailDto
{
    public int      ComplaintId       { get; set; }
    public DateTime? ComplaintDate    { get; set; }
    public string   Subject           { get; set; } = "";
    public string   Details           { get; set; } = "";
    public byte?    Priority          { get; set; }
    public string?  PriorityName      { get; set; }
    public byte?    Status            { get; set; }
    public string?  StatusName        { get; set; }
    public string?  Solution          { get; set; }
    public DateTime? SolvedDate       { get; set; }

    // العميل
    public int      PartyId           { get; set; }
    public string?  ClientName        { get; set; }
    public string?  ClientPhone       { get; set; }
    public string?  ClientEmail       { get; set; }
    public string?  ClientAddress     { get; set; }

    // النوع
    public int      TypeId            { get; set; }
    public string?  TypeName          { get; set; }

    // الإسناد
    public int?     AssignedTo        { get; set; }
    public string?  AssignedToName    { get; set; }

    // الفاتورة / المنتج
    public int?     TransactionId     { get; set; }
    public string?  TransactionRef    { get; set; }
    public DateTime? TransactionDate  { get; set; }
    public int?     ProductId         { get; set; }
    public string?  ProductName       { get; set; }

    // التصعيد
    public bool?    Escalated         { get; set; }
    public DateTime? EscalatedDate    { get; set; }
    public string?  EscalatedTo       { get; set; }
    public string?  EscalationReason  { get; set; }

    // التقييم
    public byte?    SatisfactionLevel { get; set; }

    // الإنشاء
    public string?  CreatedBy         { get; set; }
    public DateTime? CreatedAt        { get; set; }

    public int      DaysOpen          { get; set; }

    // ──── المرفقات والمتابعات ────
    public List<FollowUpItemDto>             FollowUps   { get; set; } = new();
    public List<ComplaintAttachmentDto>      Attachments { get; set; } = new();

    public bool IsClosed => ComplaintStatus.IsClosed(Status);
}

#endregion

#region 🔄 Workflow / Actions

public class ChangeStatusDto
{
    public int  ComplaintId { get; set; }

    [Range(1, 6, ErrorMessage = "الحالة غير صالحة")]
    public byte NewStatus   { get; set; }

    /// <summary>الحل (مطلوب عند Status = 4)</summary>
    public string? Solution { get; set; }
}

public class AssignComplaintDto
{
    public int  ComplaintId { get; set; }
    public int? AssignedTo  { get; set; }
}

public class EscalateComplaintDto
{
    public int     ComplaintId      { get; set; }

    [Required, StringLength(100)]
    public string  EscalatedTo      { get; set; } = "";

    [StringLength(500)]
    public string? EscalationReason { get; set; }
}

public class RateComplaintDto
{
    public int  ComplaintId { get; set; }

    [Range(1, 5)]
    public byte SatisfactionLevel { get; set; }
}

#endregion

#region 📋 المتابعات (Follow-Ups)

public class FollowUpFormDto
{
    public int? FollowUpId { get; set; }

    public int  ComplaintId { get; set; }

    [Required(ErrorMessage = "الملاحظات مطلوبة")]
    [MinLength(2)]
    public string Notes { get; set; } = "";

    [StringLength(500)]
    public string? ActionTaken { get; set; }

    public DateTime? NextFollowUpDate { get; set; }
}

public class FollowUpItemDto
{
    public int       FollowUpId        { get; set; }
    public DateTime? FollowUpDate      { get; set; }
    public int       FollowUpBy        { get; set; }
    public string?   FollowUpByName    { get; set; }
    public string?   Notes             { get; set; }
    public string?   ActionTaken       { get; set; }
    public DateTime? NextFollowUpDate  { get; set; }
}

#endregion

#region 📎 المرفقات

public class ComplaintAttachmentDto
{
    public int      AttachmentId     { get; set; }
    public string   FileName         { get; set; } = "";
    public string   OriginalFileName { get; set; } = "";
    public string   FilePath         { get; set; } = "";
    public long     FileSize         { get; set; }
    public string   MimeType         { get; set; } = "";
    public DateTime UploadedAt       { get; set; }
    public string?  UploadedByName   { get; set; }

    public string FileSizeFormatted =>
        FileSize switch
        {
            < 1024            => $"{FileSize} B",
            < 1024 * 1024     => $"{FileSize / 1024.0:F1} KB",
            < 1024L * 1024 * 1024 => $"{FileSize / (1024.0 * 1024):F1} MB",
            _                 => $"{FileSize / (1024.0 * 1024 * 1024):F2} GB"
        };

    public bool IsImage => MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}

#endregion

#region 📊 الـ Dashboard

public class ComplaintsDashboardDto
{
    public int TotalCount         { get; set; }
    public int NewCount           { get; set; }
    public int InProgressCount    { get; set; }
    public int AwaitingClientCount{ get; set; }
    public int ResolvedCount      { get; set; }
    public int RejectedCount      { get; set; }
    public int EscalatedCount     { get; set; }

    public double AverageDaysOpen { get; set; }
    public double AverageRating   { get; set; }

    public List<ComplaintListItemDto> RecentComplaints   { get; set; } = new();
    public List<ComplaintListItemDto> OverdueComplaints  { get; set; } = new();
    public List<TypeStatDto>          ByType             { get; set; } = new();
}

public class TypeStatDto
{
    public int    TypeId   { get; set; }
    public string TypeName { get; set; } = "";
    public int    Count    { get; set; }
}

#endregion

#region 🧰 Lookups

public class ComplaintTypeDto
{
    public int    TypeId     { get; set; }
    public string TypeName   { get; set; } = "";
    public string? TypeNameAr { get; set; }
    public bool   IsActive   { get; set; }
}

public class PartyLookupItemDto
{
    public int    PartyId   { get; set; }
    public string PartyName { get; set; } = "";
    public string? Phone    { get; set; }
}

public class EmployeeLookupItemDto
{
    public int     EmployeeId { get; set; }
    public string  FullName   { get; set; } = "";
    public string? JobTitle   { get; set; }
}
public class ProductLookupItemDto
{
    public int     ProductId   { get; set; }
    public string  ProductName { get; set; } = "";
    public string? Description { get; set; }
    public decimal? Price      { get; set; }
}

public class TransactionLookupItemDto
{
    public int     TransactionId   { get; set; }
    public string? ReferenceNumber { get; set; }
    public DateTime TransactionDate { get; set; }
    public decimal GrandTotal      { get; set; }
}

#endregion
