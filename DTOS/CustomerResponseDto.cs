using System.Text.Json.Serialization;

namespace COCOBOLOERPNEW.DTOs;

public class CustomerResponseDto
{
    [JsonPropertyName("response")]
    public string Response { get; set; } = "";
    
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
    
    [JsonPropertyName("customerName")]
    public string? CustomerName { get; set; }
}