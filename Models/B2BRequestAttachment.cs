namespace COCOBOLOERPNEW.Models;

public partial class B2BRequestAttachment
{
    public int AttachmentId { get; set; }
    public int RequestId { get; set; }
    public string FileName { get; set; } = null!;
    public string StoredFileName { get; set; } = null!;
    public string RelativePath { get; set; } = null!;
    public string? ContentType { get; set; }
    public long FileSizeBytes { get; set; }
    public DateTime UploadedAt { get; set; }
    public string UploadedBy { get; set; } = null!;

    public virtual B2BRequest Request { get; set; } = null!;
}
