namespace COCOBOLOERPNEW.Models;

/// <summary>
/// CRM Data Scoping — partial extension for User model
/// </summary>
public partial class User
{
    /// <summary>أول تاريخ مسموح للمستخدم يشوف بيانات CRM منه</summary>
    public DateTime? CrmAccessFromDate { get; set; }
}
