namespace COCOBOLOERPNEW.Models;

public partial class Product
{
    public virtual ICollection<ProductFactoryAlternative> ProductFactoryAlternatives { get; set; } = new List<ProductFactoryAlternative>();
}
