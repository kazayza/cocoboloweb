namespace COCOBOLOERPNEW.Models;

public partial class ProductFactoryAlternativeImage
{
    public int AlternativeImageId { get; set; }
    public int AlternativeId { get; set; }
    public string ImagePath { get; set; } = "";
    public string? Caption { get; set; }
    public bool IsPrimary { get; set; }
    public DateTime CreatedAt { get; set; }

    public virtual ProductFactoryAlternative Alternative { get; set; } = null!;
}
