using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class ProductImage
{
    public int ProductImagesId { get; set; }

    public int ProductId { get; set; }

    public string ImagePath { get; set; } = null!;

    public byte[]? ImageProduct { get; set; }

    public string? ImageNote { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Product Product { get; set; } = null!;
}
