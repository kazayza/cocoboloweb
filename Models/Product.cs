using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class Product
{
    public int ProductId { get; set; }

    public string ProductName { get; set; } = null!;

    public string ProductDescription { get; set; } = null!;

    public string? ManufacturingDescription { get; set; }

    public int? Customer { get; set; }

    public int ProductGroupId { get; set; }

    public decimal? PurchasePrice { get; set; }

    public decimal? SuggestedSalePrice { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public decimal? PurchasePriceElite { get; set; }

    public decimal? SuggestedSalePriceElite { get; set; }

    public string? PricingType { get; set; }

    public int? Qty { get; set; }

    public int? Period { get; set; }

    public byte[]? Pdffile { get; set; }

    public bool? IsSelected { get; set; }

    public int PricingStatusId { get; set; }

    public string? PdfPath { get; set; }

    public virtual ICollection<PriceChangeRequest> PriceChangeRequests { get; set; } = new List<PriceChangeRequest>();

    public virtual ICollection<PriceHistory> PriceHistories { get; set; } = new List<PriceHistory>();

    public virtual ProductPricingStatus PricingStatus { get; set; } = null!;

    public virtual ICollection<ProductComponent> ProductComponents { get; set; } = new List<ProductComponent>();

    public virtual ProductGroup ProductGroup { get; set; } = null!;

    public virtual ICollection<ProductImage> ProductImages { get; set; } = new List<ProductImage>();

    public virtual ICollection<StockLevel> StockLevels { get; set; } = new List<StockLevel>();

    public virtual ICollection<StockTransaction> StockTransactions { get; set; } = new List<StockTransaction>();

    public virtual ICollection<TransactionDetail> TransactionDetails { get; set; } = new List<TransactionDetail>();
}
