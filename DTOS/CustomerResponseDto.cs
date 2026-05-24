namespace COCOBOLOERPNEW.DTOs;

/// <summary>DTO لرد العميل على العرض</summary>
public class CustomerResponseDto
{
    public string Response { get; set; } = "";  // "Accepted" or "Rejected"
    public string? Reason { get; set; }          // اختياري للرفض
    public string? CustomerName { get; set; }    // اختياري
}