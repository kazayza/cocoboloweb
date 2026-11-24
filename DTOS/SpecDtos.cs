namespace COCOBOLOERPNEW.DTOs;

public static class SpecDataTypes
{
    public const string Number = "Number";
    public const string Choice = "Choice";
    public const string Text = "Text";
}

public static class SpecOwners
{
    public const string Product = "Product";
    public const string Alternative = "Alternative";
    public const string Component = "Component";
}

public static class ComponentKinds
{
    public const string Material = "Material";
    public const string Piece = "Piece";
    public const string Product = "Product";
}

public class SpecListDto
{
    public int ListId { get; set; }
    public string ListCode { get; set; } = "";
    public string ListNameAr { get; set; } = "";
}

public class SpecOptionDto
{
    public int OptionId { get; set; }
    public int ListId { get; set; }
    public string OptionNameAr { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public class SpecQuestionDto
{
    public int QuestionId { get; set; }
    public int ProductGroupId { get; set; }
    public string QuestionNameAr { get; set; } = "";
    public string DataType { get; set; } = SpecDataTypes.Text;
    public int? ListId { get; set; }
    public string? ListNameAr { get; set; }
    public string? ListCode { get; set; }
    public string? UnitName { get; set; }
    public bool IsRequired { get; set; }
    public int SortOrder { get; set; }
    public List<SpecOptionDto> Options { get; set; } = new();
}

public class SpecVariableImageDto
{
    public int ImageId { get; set; }
    public int QuestionId { get; set; }
    public string Path { get; set; } = "";
}

public class SpecImagePick
{
    public int QuestionId { get; set; }
    public List<Microsoft.AspNetCore.Components.Forms.IBrowserFile> Files { get; set; } = new();
}

public class SpecAnswerDto
{
    public int QuestionId { get; set; }
    public int? OptionId { get; set; }
    public decimal? NumberValue { get; set; }
    public string? TextValue { get; set; }
}

public static class SpecVariableRules
{
    public const string NoneText = "لا يوجد";

    public static bool IsNone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var compact = new string(value.Where(c => !char.IsWhiteSpace(c)).ToArray());
        return string.Equals(compact, "لايوجد", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCatalogVariable(SpecQuestionDto question)
        => question.ListId.HasValue
           && question.DataType != SpecDataTypes.Number
           && !string.Equals(question.ListCode, "BEDSIZE", StringComparison.OrdinalIgnoreCase);

    private static readonly string[] BasicDimensionNames = { "الطول", "العرض", "الارتفاع", "العمق", "القطر" };

    public static bool IsBasicDimension(SpecQuestionDto question)
    {
        var name = question.QuestionNameAr?.Trim();
        return question.DataType == SpecDataTypes.Number
            && name != null
            && BasicDimensionNames.Contains(name);
    }

    public static List<string> MissingNames(IEnumerable<SpecQuestionDto> questions, IReadOnlyDictionary<int, SpecAnswerDto> drafts)
        => questions.Where(IsCatalogVariable)
            .Where(q => !drafts.TryGetValue(q.QuestionId, out var answer) || string.IsNullOrWhiteSpace(answer.TextValue))
            .Select(q => string.IsNullOrWhiteSpace(q.QuestionNameAr) ? (q.ListNameAr ?? "متغير") : q.QuestionNameAr)
            .ToList();

    public static string MissingMessage(string subject, IReadOnlyList<string> names)
    {
        var joined = string.Join("، ", names);
        var core = $"نسيت إضافة متغير من المتغيرات: {joined}";
        return string.IsNullOrWhiteSpace(subject) ? core : $"{subject}. {core}";
    }
}
