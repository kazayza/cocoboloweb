using System.Text.Json.Serialization;

namespace COCOBOLOERPNEW.DTOs;

public class CustomerResponseDto
{
    [JsonPropertyName("response")]
    public string Response { get; set; } = "";
    
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    // ⭐ دفعة 1: لا يوجد حقل لاسم العميل هنا عمدًا — اسم المستجيب يُؤخذ
    //    دائمًا من سجل العميل المرتبط بالعرض في الخادم، لا من جسم الطلب.
}