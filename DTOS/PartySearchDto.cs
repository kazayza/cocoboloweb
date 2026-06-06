namespace COCOBOLOERPNEW.DTOs;

public class PartySearchDto
{
    public int PartyId { get; set; }
    public string PartyName { get; set; } = "";
    public string? Phone { get; set; }
    public string? Phone2 { get; set; }
    public string? LastStageName { get; set; }
    public DateTime? LastContactDate { get; set; }
}