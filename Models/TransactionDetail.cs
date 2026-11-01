using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class TransactionDetail
{
    public int DetailId { get; set; }

    public int TransactionId { get; set; }

    public int ProductId { get; set; }

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal? TotalAmount { get; set; }

    public string? Notes { get; set; }

    public int? SelectedAlternativeId { get; set; }

    public string PricingTier { get; set; } = null!;

    // ⭐ جدولة تسليم البند: التاريخ الخاص بالبند لا يُخزَّن إلا إن فُصل يدويًا عن
    // تاريخ استحقاق الفاتورة (DeliveryDateIsCustom = true)؛ وإلا فالبند «وارث»
    // (DeliveryDate = NULL) ويتحرك تلقائيًا مع تاريخ الفاتورة — انظر DeliveryScheduleResolver.
    public DateTime? DeliveryDate { get; set; }

    public bool DeliveryDateIsCustom { get; set; }

    public virtual Product Product { get; set; } = null!;

    public virtual Transaction Transaction { get; set; } = null!;
}
