using System.Data;
using System.Linq.Expressions;
using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class SpecService : ISpecService
{
    private readonly IDbContextFactory<db24804Context> _dbFactory;

    private static void AddParam(System.Data.Common.DbCommand cmd, string name, object value)
    {
        var parameter = cmd.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        cmd.Parameters.Add(parameter);
    }

    public SpecService(IDbContextFactory<db24804Context> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<bool> IsReadyAsync()
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            await db.SpecLists.AsNoTracking().Select(x => x.ListId).Take(1).ToListAsync();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<List<SpecListDto>> GetListsAsync()
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.SpecLists.AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.ListNameAr)
                .Select(x => new SpecListDto
                {
                    ListId = x.ListId,
                    ListCode = x.ListCode,
                    ListNameAr = x.ListNameAr
                })
                .ToListAsync();
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            return new();
        }
    }

    public async Task<(bool Ok, string Message, int? ListId)> AddListAsync(string nameAr)
    {
        var name = nameAr?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return (false, "اسم نوع المتغير مطلوب.", null);
        if (name.Length > 100)
            return (false, "اسم النوع أطول من اللازم.", null);
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var exists = await db.SpecLists.AnyAsync(x => x.ListNameAr == name && x.IsActive);
            if (exists) return (false, "النوع موجود بالفعل.", null);
            var list = new SpecList
            {
                ListCode = "U" + Guid.NewGuid().ToString("N")[..12],
                ListNameAr = name,
                IsActive = true
            };
            db.SpecLists.Add(list);
            await db.SaveChangesAsync();
            db.SpecQuestions.Add(new SpecQuestion
            {
                ProductGroupId = 0,
                QuestionNameAr = name,
                DataType = SpecDataTypes.Text,
                ListId = list.ListId,
                SortOrder = 60,
                IsActive = true
            });
            await db.SaveChangesAsync();
            return (true, "تمت إضافة نوع المتغير.", list.ListId);
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            return (false, "جداول المتغيرات غير موجودة.", null);
        }
        catch (Exception ex)
        {
            return (false, "تعذر إضافة النوع: " + (ex.InnerException?.Message ?? ex.Message), null);
        }
    }

    public async Task<List<string>> GetOptionNamesAsync(int listId)
    {
        if (listId <= 0) return new();
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.SpecOptions.AsNoTracking()
                .Where(o => o.ListId == listId && o.IsActive)
                .OrderBy(o => o.SortOrder).ThenBy(o => o.OptionNameAr)
                .Select(o => o.OptionNameAr)
                .ToListAsync();
        }
        catch
        {
            return new();
        }
    }

    public async Task<List<SpecOptionDto>> GetOptionsAsync(int listId)
    {
        if (listId <= 0) return new();
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.SpecOptions.AsNoTracking()
                .Where(o => o.ListId == listId && o.IsActive)
                .OrderBy(o => o.SortOrder).ThenBy(o => o.OptionNameAr)
                .Select(o => new SpecOptionDto
                {
                    OptionId = o.OptionId,
                    ListId = o.ListId,
                    OptionNameAr = o.OptionNameAr,
                    IsActive = o.IsActive
                })
                .ToListAsync();
        }
        catch
        {
            return new();
        }
    }

    public async Task<List<string>> GetTextSuggestionsAsync(int questionId)
    {
        if (questionId <= 0) return new();
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var values = await db.SpecAnswers.AsNoTracking()
                .Where(a => a.QuestionId == questionId && a.TextValue != null && a.TextValue != "")
                .Select(a => a.TextValue!)
                .Distinct()
                .ToListAsync();
            var listId = await db.SpecQuestions.AsNoTracking()
                .Where(q => q.QuestionId == questionId)
                .Select(q => q.ListId)
                .FirstOrDefaultAsync();
            if (listId.HasValue)
            {
                values.AddRange(await db.SpecOptions.AsNoTracking()
                    .Where(o => o.ListId == listId && o.IsActive)
                    .Select(o => o.OptionNameAr)
                    .ToListAsync());
            }
            return values.Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();
        }
        catch
        {
            return new();
        }
    }

    public async Task<(bool Ok, string Message, int? OptionId)> AddOptionAsync(int listId, string nameAr)
    {
        var name = nameAr?.Trim();
        if (listId <= 0 || string.IsNullOrWhiteSpace(name))
            return (false, "اسم الاختيار مطلوب.", null);
        if (SpecVariableRules.IsNone(name))
            return (false, "لا يوجد إجابة على المنتج، وليست اختيارًا في القائمة.", null);
        if (name.Length > 100)
            return (false, "اسم الاختيار أطول من اللازم.", null);
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var existing = await db.SpecOptions.FirstOrDefaultAsync(x => x.ListId == listId && x.OptionNameAr == name);
            if (existing != null)
            {
                if (existing.IsActive)
                    return (false, "الاختيار موجود بالفعل.", existing.OptionId);
                existing.IsActive = true;
                await db.SaveChangesAsync();
                return (true, "تمت إضافة الاختيار.", existing.OptionId);
            }
            var option = new SpecOption { ListId = listId, OptionNameAr = name, IsActive = true };
            db.SpecOptions.Add(option);
            await db.SaveChangesAsync();
            return (true, "تمت إضافة الاختيار.", option.OptionId);
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            return (false, "جداول المتغيرات غير موجودة. شغّل السكربت أولاً.", null);
        }
    }

    public async Task<int> GetProductGroupIdAsync(int productId)
    {
        if (productId <= 0) return 0;
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.Products.AsNoTracking()
                .Where(p => p.ProductId == productId)
                .Select(p => p.ProductGroupId)
                .FirstOrDefaultAsync();
        }
        catch
        {
            return 0;
        }
    }

    public async Task<List<SpecQuestionDto>> GetStandardQuestionsAsync()
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            foreach (var list in new (string Code, string Name)[]
            {
                ("COLOR", "ألوان"),
                ("WOOD", "أنواع الخشب"),
                ("VENEER", "أنواع القشرة"),
                ("FABRIC", "أقمشة"),
                ("MARBLE", "رخام"),
                ("ACCESSORY", "إكسسوارات"),
                ("GLASS", "زجاج"),
                ("BEDSIZE", "تسمية المقاس")
            })
            {
                if (!await db.SpecLists.AnyAsync(x => x.ListCode == list.Code))
                    db.SpecLists.Add(new SpecList { ListCode = list.Code, ListNameAr = list.Name, IsActive = true });
            }
            await db.SaveChangesAsync();
            var bedList = await db.SpecLists.FirstOrDefaultAsync(x => x.ListCode == "BEDSIZE");
            if (bedList != null && !await db.SpecOptions.AnyAsync(x => x.ListId == bedList.ListId))
            {
                foreach (var name in new[] { "مفرد", "مزدوج", "كوين", "كينج" })
                    db.SpecOptions.Add(new SpecOption { ListId = bedList.ListId, OptionNameAr = name, IsActive = true });
                await db.SaveChangesAsync();
            }
            var lists = await db.SpecLists.Where(x => x.IsActive).ToListAsync();
            if (lists.Count == 0)
            {
                await DeactivateQuestionsOnInactiveListsAsync(db);
                return new();
            }

            var wanted = new (string Name, string Type, string? Code, string? Unit, int Sort)[]
            {
                ("اللون", SpecDataTypes.Text, "COLOR", null, 1),
                ("نوع الخشب", SpecDataTypes.Choice, "WOOD", null, 2),
                ("نوع القشرة", SpecDataTypes.Text, "VENEER", null, 3),
                ("القماش", SpecDataTypes.Text, "FABRIC", null, 4),
                ("الرخام", SpecDataTypes.Text, "MARBLE", null, 5),
                ("الإكسسوار", SpecDataTypes.Text, "ACCESSORY", null, 6),
                ("تسمية المقاس", SpecDataTypes.Choice, "BEDSIZE", null, 7),
                ("الطول", SpecDataTypes.Number, null, "سم", 10),
                ("العرض", SpecDataTypes.Number, null, "سم", 11),
                ("العمق", SpecDataTypes.Number, null, "سم", 12),
                ("الارتفاع", SpecDataTypes.Number, null, "سم", 13),
                ("القطر", SpecDataTypes.Number, null, "سم", 14),
                ("السمك", SpecDataTypes.Number, null, "سم", 20),
                ("ارتفاع الجلسة", SpecDataTypes.Number, null, "سم", 21),
                ("ارتفاع الظهر", SpecDataTypes.Number, null, "سم", 22),
                ("طول داخلي", SpecDataTypes.Number, null, "سم", 30),
                ("عرض داخلي", SpecDataTypes.Number, null, "سم", 31),
                ("عمق داخلي", SpecDataTypes.Number, null, "سم", 32),
                ("ارتفاع داخلي", SpecDataTypes.Number, null, "سم", 33),
                ("طول خارجي", SpecDataTypes.Number, null, "سم", 40),
                ("عرض خارجي", SpecDataTypes.Number, null, "سم", 41),
                ("عمق خارجي", SpecDataTypes.Number, null, "سم", 42),
                ("ارتفاع خارجي", SpecDataTypes.Number, null, "سم", 43)
            };
            var existing = await db.SpecQuestions.Where(q => q.ProductGroupId == 0).ToListAsync();
            var changed = await DeactivateQuestionsOnInactiveListsAsync(db, existing);
            var oldSize = existing.FirstOrDefault(q => q.QuestionNameAr == "المقاس" && q.IsActive);
            if (oldSize != null)
            {
                oldSize.IsActive = false;
                changed = true;
            }
            foreach (var item in wanted)
            {
                int? linkedListId = item.Code == null ? null : lists.FirstOrDefault(l => l.ListCode == item.Code)?.ListId;
                if (item.Code != null && !linkedListId.HasValue)
                    continue;
                var current = linkedListId.HasValue
                    ? existing.FirstOrDefault(q => q.ListId == linkedListId)
                    : existing.FirstOrDefault(q => q.QuestionNameAr == item.Name);
                if (current == null)
                    current = existing.FirstOrDefault(q => q.QuestionNameAr == item.Name && (item.Code == null || q.ListId == null || q.ListId == linkedListId));
                if (current != null)
                {
                    if (current.DataType != item.Type || current.ListId != linkedListId || current.UnitName != item.Unit || current.SortOrder != item.Sort || !current.IsActive)
                    {
                        current.DataType = item.Type;
                        current.ListId = linkedListId;
                        current.UnitName = item.Unit;
                        current.IsActive = true;
                        current.SortOrder = item.Sort;
                        changed = true;
                    }
                    continue;
                }
                db.SpecQuestions.Add(new SpecQuestion
                {
                    ProductGroupId = 0,
                    QuestionNameAr = item.Name,
                    DataType = item.Type,
                    ListId = linkedListId,
                    UnitName = item.Unit,
                    SortOrder = item.Sort,
                    IsActive = true
                });
                changed = true;
            }
            if (changed) await db.SaveChangesAsync();
            await EnsureListQuestionsAsync(db);
            return await LoadQuestionsAsync(db, q => q.ProductGroupId == 0 && q.IsActive);
        }
        catch
        {
            return new();
        }
    }

    public async Task<List<SpecQuestionDto>> GetQuestionsAsync(int productGroupId)
    {
        if (productGroupId <= 0) return new();
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await LoadQuestionsAsync(db, q => q.ProductGroupId == productGroupId && q.IsActive);
        }
        catch
        {
            return new();
        }
    }

    private static async Task<List<SpecQuestionDto>> LoadQuestionsAsync(db24804Context db, System.Linq.Expressions.Expression<Func<SpecQuestion, bool>> predicate)
    {
        var questions = await db.SpecQuestions.AsNoTracking()
            .Where(predicate)
            .OrderBy(q => q.SortOrder).ThenBy(q => q.QuestionId)
            .ToListAsync();
        if (questions.Count == 0) return new();

        var listIds = questions.Where(q => q.ListId.HasValue).Select(q => q.ListId!.Value).Distinct().ToList();
        var lists = listIds.Count == 0
            ? new Dictionary<int, SpecList>()
            : await db.SpecLists.AsNoTracking().Where(l => listIds.Contains(l.ListId)).ToDictionaryAsync(l => l.ListId);
        var options = listIds.Count == 0
            ? new List<SpecOption>()
            : await db.SpecOptions.AsNoTracking().Where(o => listIds.Contains(o.ListId) && o.IsActive).OrderBy(o => o.SortOrder).ThenBy(o => o.OptionNameAr).ToListAsync();

        return questions.Select(q =>
        {
            lists.TryGetValue(q.ListId ?? 0, out var listRow);
            return new SpecQuestionDto
        {
            QuestionId = q.QuestionId,
            ProductGroupId = q.ProductGroupId,
            QuestionNameAr = q.QuestionNameAr,
            DataType = q.DataType,
            ListId = q.ListId,
            ListNameAr = listRow?.ListNameAr,
            ListCode = listRow?.ListCode,
            UnitName = q.UnitName,
            IsRequired = q.IsRequired,
            SortOrder = q.SortOrder,
            Options = options.Where(o => o.ListId == q.ListId)
                .Select(o => new SpecOptionDto { OptionId = o.OptionId, ListId = o.ListId, OptionNameAr = o.OptionNameAr, IsActive = o.IsActive })
                .ToList()
        };
        }).ToList();
    }

    public async Task<(bool Ok, string Message)> AddQuestionAsync(SpecQuestionDto question)
    {
        var name = question.QuestionNameAr?.Trim();
        if (question.ProductGroupId <= 0 || string.IsNullOrWhiteSpace(name))
            return (false, "اسم السؤال والمجموعة مطلوبان.");
        if (question.DataType == SpecDataTypes.Choice && !question.ListId.HasValue)
            return (false, "سؤال الاختيار يحتاج قائمة.");
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            db.SpecQuestions.Add(new SpecQuestion
            {
                ProductGroupId = question.ProductGroupId,
                QuestionNameAr = name,
                DataType = question.DataType,
                ListId = question.DataType == SpecDataTypes.Choice ? question.ListId : null,
                UnitName = string.IsNullOrWhiteSpace(question.UnitName) ? null : question.UnitName.Trim(),
                IsRequired = question.IsRequired,
                SortOrder = question.SortOrder,
                IsActive = true
            });
            await db.SaveChangesAsync();
            return (true, "تمت إضافة السؤال للمجموعة.");
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            return (false, "جداول المتغيرات غير موجودة. شغّل السكربت أولاً.");
        }
    }

    public async Task<List<SpecAnswerDto>> GetAnswersAsync(string ownerType, int ownerId)
    {
        if (ownerId <= 0) return new();
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.SpecAnswers.AsNoTracking()
                .Where(a => a.OwnerType == ownerType && a.OwnerId == ownerId)
                .Select(a => new SpecAnswerDto
                {
                    QuestionId = a.QuestionId,
                    OptionId = a.OptionId,
                    NumberValue = a.NumberValue,
                    TextValue = a.TextValue
                })
                .ToListAsync();
        }
        catch (Exception)
        {
            return new();
        }
    }

    public async Task<(bool Ok, string Message)> SaveAnswersAsync(string ownerType, int ownerId, IReadOnlyList<SpecAnswerDto> answers)
    {
        if (ownerId <= 0) return (false, "احفظ السجل أولاً ثم سجّل المتغيرات.");
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var existing = await db.SpecAnswers
                .Where(a => a.OwnerType == ownerType && a.OwnerId == ownerId)
                .ToListAsync();
            foreach (var answer in answers)
            {
                var row = existing.FirstOrDefault(x => x.QuestionId == answer.QuestionId);
                if (row == null)
                {
                    row = new SpecAnswer { OwnerType = ownerType, OwnerId = ownerId, QuestionId = answer.QuestionId };
                    db.SpecAnswers.Add(row);
                }
                row.OptionId = answer.OptionId;
                row.NumberValue = answer.NumberValue;
                row.TextValue = string.IsNullOrWhiteSpace(answer.TextValue) ? null : answer.TextValue.Trim();
            }
            await db.SaveChangesAsync();
            return (true, "تم حفظ المتغيرات.");
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            return (true, "");
        }
        catch (Exception ex)
        {
            return (false, "تعذر حفظ المتغيرات: " + (ex.InnerException?.Message ?? ex.Message));
        }
    }

    public async Task ClearAnswersAsync(string ownerType, int ownerId)
    {
        if (ownerId <= 0) return;
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var rows = await db.SpecAnswers.Where(a => a.OwnerType == ownerType && a.OwnerId == ownerId).ToListAsync();
            if (rows.Count == 0) return;
            db.SpecAnswers.RemoveRange(rows);
            await db.SaveChangesAsync();
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
        }
    }

    public async Task<List<SpecVariableImageDto>> GetVariableImagesAsync(string ownerType, int ownerId)
    {
        var rows = new List<SpecVariableImageDto>();
        if (ownerId <= 0) return rows;
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var conn = db.Database.GetDbConnection();
            var opened = conn.State != ConnectionState.Open;
            if (opened) await conn.OpenAsync();
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT ImageId, QuestionId, ImagePath FROM dbo.SpecVariableImages
                    WHERE OwnerType = @owner AND OwnerId = @id ORDER BY ImageId
                    """;
                AddParam(cmd, "@owner", ownerType);
                AddParam(cmd, "@id", ownerId);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    rows.Add(new SpecVariableImageDto
                    {
                        ImageId = reader.GetInt32(0),
                        QuestionId = reader.GetInt32(1),
                        Path = reader.IsDBNull(2) ? "" : reader.GetString(2)
                    });
            }
            finally { if (opened) await conn.CloseAsync(); }
        }
        catch { }
        return rows;
    }

    public async Task SaveVariableImagesAsync(string ownerType, int ownerId, IReadOnlyList<SpecVariableImageDto> images)
    {
        if (ownerId <= 0) return;
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var conn = db.Database.GetDbConnection();
            var opened = conn.State != ConnectionState.Open;
            if (opened) await conn.OpenAsync();
            try
            {
                await using var delete = conn.CreateCommand();
                delete.CommandText = "DELETE FROM dbo.SpecVariableImages WHERE OwnerType = @owner AND OwnerId = @id";
                AddParam(delete, "@owner", ownerType);
                AddParam(delete, "@id", ownerId);
                await delete.ExecuteNonQueryAsync();
                foreach (var image in images.Where(i => !string.IsNullOrWhiteSpace(i.Path)))
                {
                    await using var insert = conn.CreateCommand();
                    insert.CommandText = """
                        INSERT INTO dbo.SpecVariableImages (OwnerType, OwnerId, QuestionId, ImagePath)
                        VALUES (@owner, @id, @question, @path)
                        """;
                    AddParam(insert, "@owner", ownerType);
                    AddParam(insert, "@id", ownerId);
                    AddParam(insert, "@question", image.QuestionId);
                    AddParam(insert, "@path", image.Path.Trim());
                    await insert.ExecuteNonQueryAsync();
                }
            }
            finally { if (opened) await conn.CloseAsync(); }
        }
        catch { }
    }

    public async Task RemoveAnswersAsync(string ownerType, int ownerId, IReadOnlyList<int> questionIds)
    {
        if (ownerId <= 0 || questionIds.Count == 0) return;
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var rows = await db.SpecAnswers
                .Where(a => a.OwnerType == ownerType && a.OwnerId == ownerId && questionIds.Contains(a.QuestionId))
                .ToListAsync();
            if (rows.Count == 0) return;
            db.SpecAnswers.RemoveRange(rows);
            await db.SaveChangesAsync();
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
        }
    }

    public async Task<(bool Ok, string Message)> RenameOptionAsync(int optionId, string nameAr)
    {
        var name = nameAr?.Trim();
        if (optionId <= 0 || string.IsNullOrWhiteSpace(name))
            return (false, "اسم الاختيار مطلوب.");
        if (name.Length > 100)
            return (false, "اسم الاختيار أطول من اللازم.");
        if (SpecVariableRules.IsNone(name))
            return (false, "لا يوجد إجابة على المنتج، وليست اختيارًا في القائمة.");
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var option = await db.SpecOptions.FirstOrDefaultAsync(x => x.OptionId == optionId);
            if (option == null) return (false, "الاختيار غير موجود.");
            var duplicate = await db.SpecOptions.AnyAsync(x => x.ListId == option.ListId && x.OptionId != optionId && x.OptionNameAr == name && x.IsActive);
            if (duplicate) return (false, "الاختيار موجود بالفعل.");
            var answers = await db.SpecAnswers.Where(a => a.OptionId == optionId).ToListAsync();
            option.OptionNameAr = name;
            foreach (var answer in answers)
                answer.TextValue = name;
            await db.SaveChangesAsync();
            return (true, answers.Count == 0
                ? "تم تعديل الاختيار."
                : "تم تعديل الاختيار، والمنتجات المرتبطة اتحدثت.");
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            return (false, "جداول المتغيرات غير موجودة.");
        }
        catch (Exception ex)
        {
            return (false, "تعذر تعديل الاختيار: " + (ex.InnerException?.Message ?? ex.Message));
        }
    }

    public async Task<(bool Ok, string Message)> RemoveOptionAsync(int optionId)
    {
        if (optionId <= 0) return (false, "الاختيار غير موجود.");
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var option = await db.SpecOptions.FirstOrDefaultAsync(x => x.OptionId == optionId);
            if (option == null) return (false, "الاختيار غير موجود.");
            var questionIds = await db.SpecQuestions.Where(q => q.ListId == option.ListId).Select(q => q.QuestionId).ToListAsync();
            var linked = await db.SpecAnswers.Where(a => a.OptionId == optionId).ToListAsync();
            var named = questionIds.Count == 0
                ? new List<SpecAnswer>()
                : await db.SpecAnswers.Where(a => questionIds.Contains(a.QuestionId) && a.TextValue == option.OptionNameAr).ToListAsync();
            var used = linked.Count > 0 || named.Count > 0;
            if (!used)
            {
                db.SpecOptions.Remove(option);
                await db.SaveChangesAsync();
                return (true, "تم مسح الاختيار.");
            }
            option.IsActive = false;
            foreach (var answer in linked)
            {
                if (string.IsNullOrWhiteSpace(answer.TextValue))
                    answer.TextValue = option.OptionNameAr;
                answer.OptionId = null;
            }
            await db.SaveChangesAsync();
            return (true, "الاختيار مستخدم. اتشال من القائمة الجديدة، والنص المحفوظ على المنتجات فضل.");
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            return (false, "جداول المتغيرات غير موجودة.");
        }
        catch (Exception ex)
        {
            return (false, "تعذر مسح الاختيار: " + (ex.InnerException?.Message ?? ex.Message));
        }
    }

    public async Task<(bool Ok, string Message)> RenameListAsync(int listId, string nameAr)
    {
        var name = nameAr?.Trim();
        if (listId <= 0 || string.IsNullOrWhiteSpace(name))
            return (false, "اسم نوع المتغير مطلوب.");
        if (name.Length > 100)
            return (false, "اسم النوع أطول من اللازم.");
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var list = await db.SpecLists.FirstOrDefaultAsync(x => x.ListId == listId && x.IsActive);
            if (list == null) return (false, "نوع المتغير غير موجود.");
            if (list.ListCode == "BEDSIZE")
                return (false, "تسمية المقاس ليست من المتغيرات الظاهرة.");
            var duplicate = await db.SpecLists.AnyAsync(x => x.ListId != listId && x.IsActive && x.ListNameAr == name);
            if (duplicate) return (false, "النوع موجود بالفعل.");
            var questions = await db.SpecQuestions.Where(q => q.ListId == listId && q.ProductGroupId == 0).ToListAsync();
            list.ListNameAr = name;
            foreach (var question in questions)
                question.QuestionNameAr = name;
            await db.SaveChangesAsync();
            return (true, "تم تعديل اسم النوع.");
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            return (false, "جداول المتغيرات غير موجودة.");
        }
        catch (Exception ex)
        {
            return (false, "تعذر تعديل النوع: " + (ex.InnerException?.Message ?? ex.Message));
        }
    }

    public async Task<(bool Ok, string Message)> RemoveListAsync(int listId)
    {
        if (listId <= 0) return (false, "نوع المتغير غير موجود.");
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var list = await db.SpecLists.FirstOrDefaultAsync(x => x.ListId == listId && x.IsActive);
            if (list == null) return (false, "نوع المتغير غير موجود.");
            if (list.ListCode == "BEDSIZE")
                return (false, "تسمية المقاس ليست من المتغيرات الظاهرة.");
            var questions = await db.SpecQuestions.Where(q => q.ListId == listId).ToListAsync();
            var questionIds = questions.Select(q => q.QuestionId).ToList();
            var answers = questionIds.Count == 0
                ? new List<SpecAnswer>()
                : await db.SpecAnswers.Where(a => questionIds.Contains(a.QuestionId)).ToListAsync();
            if (answers.Any(IsRealAnswer))
                return (false, "النوع مستخدم في منتجات بقيمة حقيقية. عدّل القيمة أو اكتب لا يوجد قبل المسح.");
            list.IsActive = false;
            foreach (var question in questions)
                question.IsActive = false;
            var options = await db.SpecOptions.Where(o => o.ListId == listId && o.IsActive).ToListAsync();
            foreach (var option in options)
                option.IsActive = false;
            await db.SaveChangesAsync();
            return (true, "تم مسح النوع.");
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            return (false, "جداول المتغيرات غير موجودة.");
        }
        catch (Exception ex)
        {
            return (false, "تعذر مسح النوع: " + (ex.InnerException?.Message ?? ex.Message));
        }
    }

    private static bool IsRealAnswer(SpecAnswer answer)
    {
        if (answer.NumberValue.HasValue) return true;
        if (SpecVariableRules.IsNone(answer.TextValue)) return false;
        if (!string.IsNullOrWhiteSpace(answer.TextValue)) return true;
        return answer.OptionId.HasValue;
    }

    private static async Task<bool> DeactivateQuestionsOnInactiveListsAsync(db24804Context db, List<SpecQuestion>? existing = null)
    {
        var inactiveIds = await db.SpecLists.Where(x => !x.IsActive).Select(x => x.ListId).ToListAsync();
        if (inactiveIds.Count == 0) return false;
        var questions = existing ?? await db.SpecQuestions.Where(q => q.ProductGroupId == 0 && q.ListId.HasValue).ToListAsync();
        var changed = false;
        foreach (var question in questions.Where(q => q.IsActive && q.ListId.HasValue && inactiveIds.Contains(q.ListId.Value)))
        {
            question.IsActive = false;
            changed = true;
        }
        if (existing == null && changed)
            await db.SaveChangesAsync();
        return changed;
    }

    private static async Task EnsureListQuestionsAsync(db24804Context db)
    {
        var lists = await db.SpecLists.Where(x => x.IsActive && x.ListCode != "BEDSIZE").ToListAsync();
        var questions = await db.SpecQuestions.Where(q => q.ProductGroupId == 0 && q.IsActive).ToListAsync();
        var added = false;
        foreach (var list in lists)
        {
            if (questions.Any(q => q.ListId == list.ListId)) continue;
            db.SpecQuestions.Add(new SpecQuestion
            {
                ProductGroupId = 0,
                QuestionNameAr = list.ListNameAr,
                DataType = SpecDataTypes.Text,
                ListId = list.ListId,
                SortOrder = 60,
                IsActive = true
            });
            added = true;
        }
        if (added) await db.SaveChangesAsync();
    }

    private static bool IsUnavailable(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            var msg = e.Message ?? "";
            if (msg.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("اسم الكائن", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("208", StringComparison.OrdinalIgnoreCase) && msg.Contains("Spec", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
