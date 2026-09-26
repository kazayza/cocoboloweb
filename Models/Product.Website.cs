using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

/// <summary>🌐 منتجات الموقع — علم القناة + معرف المنتج في الموقع (2026-09-17).
/// يتطلب سكربت Database/Add_ProductWebsite.sql قبل التشغيل.</summary>
public partial class Product
{
    public bool IsWebsite { get; set; }

    /// <summary>رقم المنتج على الموقع الرسمي — مفتاح مستقبلي للربط/المزامنة (غير مستخدم حالياً).</summary>
    public int? WebsiteProductId { get; set; }
}
