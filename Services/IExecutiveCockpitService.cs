using COCOBOLOERPNEW.DTOs;

namespace COCOBOLOERPNEW.Services;

public interface IExecutiveCockpitService
{
    // period: today | week | month | quarter | year | custom
    // ⭐ مرحلة 3: branchId اختياري (Null = كل الفروع)، وcustomFrom/customTo
    //    للفترة المخصصة (period = "custom")، وbypassCache للتحديث اليدوي
    //    (يتجاوز كاش الـ 60 ثانية ويكتب النتيجة الطازجة)، وuserScope
    //    يفصل مفاتيح الكاش بين المستهلكين (الصفحة/زكي). الاستدعاء القديم
    //    GetCockpitAsync(period) يعمل بنفس سلوكه (كل الفروع + كاش مشترك).
    Task<ExecutiveCockpitDto> GetCockpitAsync(
        string period,
        int? branchId = null,
        DateTime? customFrom = null,
        DateTime? customTo = null,
        bool bypassCache = false,
        string userScope = "shared");

    // الخط الزمني الكامل لموظف (تدقيق + فواتير أنشأها)
    Task<List<ExecutiveTimelineRowDto>> GetEmployeeTimelineAsync(int employeeId);
}
