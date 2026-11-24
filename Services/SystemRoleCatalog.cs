namespace COCOBOLOERPNEW.Services;

public static class SystemRoleCatalog
{
    public static readonly (string Value, string Label)[] Roles =
    {
        ("Admin", "مدير النظام"),
        ("GeneralManager", "مدير عام"),
        ("SalesManager", "مدير المبيعات"),
        ("Sales", "موظف مبيعات"),
        ("AccountManager", "مدير الحسابات"),
        ("Account", "محاسب"),
        ("FactoryManager", "مدير المصنع"),
        ("ProductionManager", "مدير الإنتاج"),
        ("factory", "مصنع"),
        ("DesignEngineer", "مهندس تصميم"),
        ("ExecutionEngineer", "مهندس تنفيذ"),
        ("DeliveryOfficer", "مسئول التسليمات"),
        ("HrManager", "مدير الموارد البشرية"),
        ("CustomerService", "خدمة العملاء"),
        ("SocialManager", "مدير السوشيال"),
        ("Social", "سوشيال"),
        ("SocialView", "مشاهدة السوشيال"),
        ("Viewer", "مشاهد"),
        ("B2B", "قطاع الأعمال"),
        ("User", "مستخدم")
    };

    public static string Text(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return "—";
        foreach (var item in Roles)
        {
            if (string.Equals(item.Value, role, StringComparison.OrdinalIgnoreCase))
                return item.Label;
        }
        return role;
    }

    public static IEnumerable<(string Value, string Label)> ForSelect(string? current)
    {
        foreach (var role in Roles)
            yield return role;
        if (!string.IsNullOrWhiteSpace(current)
            && !Roles.Any(role => string.Equals(role.Value, current, StringComparison.OrdinalIgnoreCase)))
            yield return (current, current);
    }
}
