using System.Globalization;
using System.Text.Json;
using COCOBOLOERPNEW.DTOs;
using static COCOBOLOERPNEW.Services.Assistant.ZakiInvoiceQueries;

namespace COCOBOLOERPNEW.Services.Assistant;

/// <summary>
/// تحويل حركات سجل التعديلات (AuditLogs) لتغييرات مفهومة.
/// منطق المقارنة منقول حرفياً من InvoiceEditHistoryDrawer (شاشة سجل التعديلات)،
/// عشان زكي يقول نفس التغييرات اللي الشاشة بتعرضها.
/// لو غيرت منطق الدرج، غيّره هنا كمان.
/// </summary>
public static class ZakiInvoiceEditDiff
{
    public sealed record Change(string Label, string? Old, string? New);

    public static string ActionLabel(string? action) => action switch
    {
        "Insert" => "إنشاء الفاتورة",
        "Update" => "تعديل بيانات عامة",
        "FullEdit" => "تعديل كامل للفاتورة",
        "EditRequest" => "طلب تعديل",
        "EditApprove" => "اعتماد طلب تعديل",
        "EditReject" => "رفض طلب تعديل",
        "ReceivePurchaseInvoice" => "استلام فاتورة شراء",
        "DeliveryPermissionRequest" => "طلب إذن تسليم (مبالغ متبقية)",
        "DeliveryPermissionGranted" => "منح إذن تسليم",
        "UpdateDeliveryStatus" => "تحديث حالة التسليم",
        "Cancel" => "إلغاء الفاتورة",
        null or "" => "حركة",
        _ => action
    };

    public static string EditStatusLabel(int? status) => status switch
    {
        InvoiceEditStatuses.Pending => "طلب تعديل معلق",
        InvoiceEditStatuses.Approved => "طلب تعديل معتمد",
        InvoiceEditStatuses.Rejected => "طلب تعديل مرفوض",
        InvoiceEditStatuses.Edited => "تم التعديل بعد الاعتماد",
        _ => "لا يوجد طلب تعديل"
    };

    /// <summary>سطر واحد يوصف التغيير بالعربي</summary>
    public static string Describe(Change c)
    {
        if (c.Old != null && c.New != null) return c.Label + " من " + c.Old + " إلى " + c.New;
        if (c.New != null) return c.Label + ": " + c.New;
        if (c.Old != null) return c.Label + ": كان " + c.Old;
        return c.Label;
    }

    public static List<Change> Compute(string? actionType, string? oldData, string? newData)
    {
        var res = new List<Change>();
        JsonDocument? oldDoc = null;
        JsonDocument? newDoc = null;

        try
        {
            if (!string.IsNullOrWhiteSpace(oldData)) oldDoc = JsonDocument.Parse(oldData);
            if (!string.IsNullOrWhiteSpace(newData)) newDoc = JsonDocument.Parse(newData);

            var o = oldDoc?.RootElement ?? default;
            var n = newDoc?.RootElement ?? default;

            switch (actionType)
            {
                case "FullEdit":
                {
                    AddMoney(res, "إجمالي الأصناف", o, n, "TotalAmount");
                    AddMoney(res, "الخصم", o, n, "DiscountAmount");
                    AddMoney(res, "الصافي", o, n, "NetTotalAmount");
                    AddMoney(res, "رسوم إضافية", o, n, "TotalChargesAmount");
                    AddMoney(res, "الإجمالي", o, n, "GrandTotal");
                    AddStr(res, "المخزن", o, n, "WarehouseId", "مخزن #");
                    AddStr(res, "طريقة الدفع", o, n, "PaymentMethod");
                    AddDate(res, "تاريخ الفاتورة", o, n, "TransactionDate");
                    AddDate(res, "تاريخ الاستحقاق", o, n, "DueDate");
                    AddBool(res, "التسليم", o, n, "IsDelivered");

                    var oldItems = GetItems(o);
                    var newItems = GetItems(n);
                    var oldById = new Dictionary<int, JsonElement>();
                    foreach (var oi in oldItems)
                        oldById[GetInt(oi, "DetailId")] = oi;

                    foreach (var ni in newItems)
                    {
                        var nid = GetInt(ni, "DetailId");
                        var nName = GetName(ni);

                        if (!oldById.TryGetValue(nid, out var oi))
                        {
                            res.Add(new Change("صنف مضاف", null, nName));
                            continue;
                        }

                        var oName = GetName(oi);
                        if (GetInt(oi, "ProductId") != GetInt(ni, "ProductId"))
                        {
                            res.Add(new Change("استبدال صنف", oName, nName));
                        }
                        else
                        {
                            var oq = GetDec(oi, "Quantity");
                            var nq = GetDec(ni, "Quantity");
                            if (oq != nq)
                                res.Add(new Change(nName + " — الكمية", Fmt(oq), Fmt(nq)));

                            var op = GetDec(oi, "UnitPrice");
                            var np = GetDec(ni, "UnitPrice");
                            if (op != np)
                                res.Add(new Change(nName + " — السعر", MoneyOrDash(op), MoneyOrDash(np)));
                        }
                    }

                    var newIds = newItems.Select(i => GetInt(i, "DetailId")).ToHashSet();
                    foreach (var oi in oldItems)
                    {
                        if (!newIds.Contains(GetInt(oi, "DetailId")))
                            res.Add(new Change("صنف محذوف", GetName(oi), null));
                    }
                    break;
                }
                case "Update":
                {
                    AddStr(res, "الملاحظات", o, n, "Notes");
                    AddDate(res, "تاريخ الاستحقاق", o, n, "DueDate");
                    AddBool(res, "التسليم", o, n, "IsDelivered");
                    AddStr(res, "طريقة الدفع", o, n, "PaymentMethod");
                    break;
                }
                case "DeliveryPermissionRequest":
                {
                    var note = JStr(n, "Note");
                    if (!string.IsNullOrWhiteSpace(note)) res.Add(new Change("تفاصيل الطلب", null, note));
                    break;
                }
                case "DeliveryPermissionGranted":
                {
                    res.Add(new Change("الحالة", null, "تم منح الإذن — يجوز التسليم رغم وجود مبالغ متبقية"));
                    break;
                }
                case "ReceivePurchaseInvoice":
                {
                    AddStr(res, "المخزن المستلَم إليه", o, n, "WarehouseId", "مخزن #");
                    AddDate(res, "تاريخ الاستلام", o, n, "DeliveredAt");
                    AddStr(res, "ملاحظات الاستلام", o, n, "DeliveredNotes");
                    break;
                }
                case "UpdateDeliveryStatus":
                {
                    var st = JStr(n, "RequestedStatus");
                    if (!string.IsNullOrWhiteSpace(st)) res.Add(new Change("الحالة المطلوبة", null, st));
                    AddStr(res, "موظف التوصيل", o, n, "DeliveryEmployeeName");
                    AddBool(res, "حالة التسليم", o, n, "IsDelivered");
                    AddDate(res, "تاريخ التسليم", o, n, "DeliveredAt");
                    AddStr(res, "ملاحظات التسليم", o, n, "DeliveredNotes");
                    break;
                }
                case "Insert":
                {
                    var gt = GetDec(n, "GrandTotal");
                    if (gt.HasValue) res.Add(new Change("قيمة الفاتورة عند الإنشاء", null, Money(gt.Value)));
                    var cnt = GetItems(n).Count;
                    if (cnt > 0) res.Add(new Change("عدد الأصناف", null, cnt.ToString(Culture)));
                    break;
                }
                case "Cancel":
                {
                    var r = JStr(n, "EditReason") ?? JStr(o, "EditReason") ?? JStr(n, "Reason");
                    if (!string.IsNullOrWhiteSpace(r)) res.Add(new Change("سبب الإلغاء", null, r));
                    break;
                }
                case "EditRequest":
                {
                    var r = JStr(n, "Reason");
                    if (!string.IsNullOrWhiteSpace(r)) res.Add(new Change("سبب الطلب", null, r));
                    break;
                }
                case "EditApprove":
                case "EditReject":
                {
                    var nt = JStr(n, "Notes");
                    if (!string.IsNullOrWhiteSpace(nt))
                        res.Add(new Change(actionType == "EditReject" ? "سبب الرفض" : "ملاحظات الاعتماد", null, nt));
                    break;
                }
            }
        }
        catch (JsonException)
        {
            // JSON مش متوقع: نعرض الحركة من غير تفاصيل
        }
        finally
        {
            oldDoc?.Dispose();
            newDoc?.Dispose();
        }

        return res;
    }

    // ════════════════════════════════════════════════════════════
    // قراءة JSON بشكل متسامح
    // ════════════════════════════════════════════════════════════
    private static string? JStr(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "نعم",
            JsonValueKind.False => "لا",
            _ => null
        };
    }

    private static decimal? GetDec(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)) return d;
        if (v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var s)) return s;
        return null;
    }

    private static DateTime? GetDate(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.String && DateTime.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        return null;
    }

    private static int GetInt(JsonElement el, string name)
    {
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i))
            return i;
        return 0;
    }

    private static bool? GetBool(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null };
    }

    private static List<JsonElement> GetItems(JsonElement el)
    {
        var list = new List<JsonElement>();
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("Items", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var i in arr.EnumerateArray())
                list.Add(i);
        }
        return list;
    }

    private static string GetName(JsonElement item)
    {
        var name = JStr(item, "ProductName");
        return string.IsNullOrWhiteSpace(name) ? "صنف #" + GetInt(item, "ProductId") : name;
    }

    private static string MoneyOrDash(decimal? v) => v.HasValue ? Money(v.Value) : "—";

    private static string? Fmt(decimal? v) => v?.ToString("0.##", Culture);

    // ════════════════════════════════════════════════════════════
    // مقارنة الحقول
    // ════════════════════════════════════════════════════════════
    private static void AddMoney(List<Change> res, string label, JsonElement o, JsonElement n, string prop)
    {
        var ov = GetDec(o, prop);
        var nv = GetDec(n, prop);
        if (ov != nv && (ov.HasValue || nv.HasValue))
            res.Add(new Change(label, MoneyOrDash(ov), MoneyOrDash(nv)));
    }

    private static void AddStr(List<Change> res, string label, JsonElement o, JsonElement n, string prop, string? prefix = null)
    {
        string? Map(string? v) => v == null ? null : (prefix ?? "") + v;
        var ov = Map(JStr(o, prop));
        var nv = Map(JStr(n, prop));
        if (ov != nv && (ov != null || nv != null))
            res.Add(new Change(label, ov, nv));
    }

    private static void AddDate(List<Change> res, string label, JsonElement o, JsonElement n, string prop)
    {
        var ov = GetDate(o, prop);
        var nv = GetDate(n, prop);
        if (ov != nv && (ov.HasValue || nv.HasValue))
            res.Add(new Change(label, ov?.ToString("yyyy-MM-dd", Culture), nv?.ToString("yyyy-MM-dd", Culture)));
    }

    private static void AddBool(List<Change> res, string label, JsonElement o, JsonElement n, string prop)
    {
        var ov = GetBool(o, prop);
        var nv = GetBool(n, prop);
        if (ov != nv && (ov.HasValue || nv.HasValue))
        {
            res.Add(new Change(label,
                ov == true ? "تم التسليم" : ov == false ? "لم يتم" : null,
                nv == true ? "تم التسليم" : nv == false ? "لم يتم" : null));
        }
    }
}
