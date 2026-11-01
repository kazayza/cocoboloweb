namespace COCOBOLOERPNEW.Services;

/// <summary>
/// ربط التكليف العام بمستند في النظام: الأنواع المعتمدة + عناوينها العربية
/// + مسار صفحة كل نوع. العنوان (Title) لقطة تُحفظ وقت الإنشاء على التكليف،
/// والمسار يُبنى من النوع والرقم وقت العرض.
/// </summary>
public static class GeneralTaskLinks
{
    public const string Invoice = "Invoice";
    public const string Delivery = "Delivery";
    public const string JobOrder = "JobOrder";
    public const string Quotation = "Quotation";
    public const string Customer = "Customer";
    public const string Maintenance = "Maintenance";

    public static readonly IReadOnlyList<(string Type, string Label)> Types = new[]
    {
        (Invoice, "فاتورة بيع"),
        (Delivery, "تسليمة"),
        (JobOrder, "أمر تشغيل"),
        (Quotation, "عرض سعر"),
        (Customer, "عميل"),
        (Maintenance, "بند صيانة تسليم"),
    };

    public static bool IsValidType(string? type)
        => type is Invoice or Delivery or JobOrder or Quotation or Customer or Maintenance;

    public static string LabelFor(string? type) => type switch
    {
        Invoice => "فاتورة بيع",
        Delivery => "تسليمة",
        JobOrder => "أمر تشغيل",
        Quotation => "عرض سعر",
        Customer => "عميل",
        Maintenance => "صيانة تسليم",
        _ => "مستند"
    };

    public static string? RouteFor(string? type, int? id)
    {
        if (!id.HasValue || id.Value <= 0) return null;
        return type switch
        {
            Invoice => $"/sales/invoices/{id.Value}",
            Delivery => $"/sales-delivery-status/{id.Value}",
            JobOrder => $"/sales/invoices/{id.Value}/job-order",
            Quotation => $"/quotations/{id.Value}",
            Customer => $"/crm/customers/{id.Value}",
            Maintenance => "/delivery-maintenance",
            _ => null
        };
    }
}
