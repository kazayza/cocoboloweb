using System;

namespace COCOBOLOERPNEW.Models;

public partial class ComplaintAttachment
{
    public int      AttachmentId     { get; set; }
    public int      ComplaintId      { get; set; }
    public string   FileName         { get; set; } = null!;
    public string   OriginalFileName { get; set; } = null!;
    public string   FilePath         { get; set; } = null!;
    public long     FileSize         { get; set; }
    public string   MimeType         { get; set; } = null!;
    public int      UploadedByUserId { get; set; }
    public DateTime UploadedAt       { get; set; }

    public virtual Complaint? Complaint  { get; set; }
    public virtual User?      UploadedBy { get; set; }
}