namespace COCOBOLOERPNEW.DTOs;

/// <summary>صف من ملف استيراد منتجات الموقع</summary>
public class ProductWebsiteImportRowDto
{
    public int    RowNumber       { get; set; }
    public string ProductName     { get; set; } = "";
    public string ProductDescription { get; set; } = ""; // إلزامي (حتى 150 حرفاً)
    public string GroupName       { get; set; } = "";   // اسم أو رقم مجموعة — الفارغة → «منتجات الموقع»
    public int?   GroupId         { get; set; }         // لو كُتب رقم مجموعة موجود في النظام
    public decimal? CostStd       { get; set; }
    public decimal? CostPremium   { get; set; }
    public decimal? CostElite     { get; set; }
    public decimal? PriceStd      { get; set; }
    public decimal? PricePremium  { get; set; }
    public decimal? PriceElite    { get; set; }
    public int?    WebsiteProductId { get; set; }
    public string? Error         { get; set; }          // غير فارغة = الصف مرفوض
}

/// <summary>نتيجة تنفيذ الاستيراد</summary>
public class ProductWebsiteImportResultDto
{
    public int    Added           { get; set; }
    public int    SkippedDuplicates { get; set; }
    public int    GroupsCreated   { get; set; }
    public List<string> SampleAddedNames    { get; set; } = new();
    public List<string> SampleDuplicateNames { get; set; } = new();
    public List<string> Errors              { get; set; } = new();
}
