# COCOBOLO ERP — Leads CRM Integration Project Status
## آخر تحديث: 2026-06-09

---

## 1. نظرة عامة على المشروع

| البند | التفاصيل |
|-------|----------|
| **اسم المشروع** | COCOBOLO ERP (cocoboloweb) |
| **النوع** | Blazor Server Application |
| **الـ Framework** | .NET 8 |
| **الـ UI** | MudBlazor |
| **الـ Database** | SQL Server (via Entity Framework Core) |
| **DbContext** | `db24804Context` |
| **المسار المحلي** | `E:\COCO BOLO\COCOBOLOWeb\COCOBOLOERPNEW\COCOBOLOERPNEW` |
| **الموقع المباشر** | https://cocobolo.runasp.net |
| **البورت المحلي** | 5142 |

---

## 2. الـ Feature: Google Sheets → ERP Leads Integration

### تدفق البيانات:
```
Meta Lead Ads → Google Sheet → Google Apps Script → ERP API → LeadsCRM Table → UI Management → Convert to Client
```

### التفاصيل:
1. **Meta Lead Ads** → بتبعت بيانات العملاء المحتملين لـ Google Sheet
2. **Google Apps Script** → شغال على الشيت (silent - مفيش menu)
   - Trigger كل 5 دقائق (time-based)
   - Trigger فوري عند التعديل (onEditInstallable)
   - بينزل البيانات من الشيت ويرسلها للـ ERP API
3. **ERP API** → بيستقبل البيانات ويخزنها في LeadsCRM table
4. **ERP UI** → صفحة إدارة Leads (`/crm/leads`) للعرض والتعديل والتحويل
5. **Convert** → تحويل Lead لعميل (Party + SalesOpportunity + CustomerInteraction + CrmTask)

---

## 3. الملفات المنشأة/المعدلة

### 3.1 Models
| الملف | الوصف |
|-------|-------|
| `Models/LeadsCrm.cs` | Entity class لجدول LeadsCRM — كل الأعمدة |
| `Models/db24804Context.cs` | **معدّل** — أضفنا `DbSet<LeadsCrm> LeadsCRMs` + OnModelCreating mapping |

### 3.2 DTOs
| الملف | الوصف |
|-------|-------|
| `DTOS/LeadImportDto.cs` | LeadImportRequest, LeadImportResult, BatchLeadImportRequest, BatchLeadImportResult, MetaFormTranslations |
| `DTOS/LeadsCrmDto.cs` | LeadsCrmFilterDto, LeadsCrmListDto, LeadsCrmDetailDto, LeadsCrmUpdateDto, LeadConvertDto, LeadsCrmStatsDto, LeadsByCampaignDto, LeadsByPlatformDto |

**ملاحظات مهمة عن DTOs:**
- `LeadsCrmFilterDto`: فيها `SearchText` + `SearchTerm` + `PageNumber` (مش Page)
- `LeadsCrmListDto`: فيها خصائص إضافية (Email, Address, ProjectStage, DecisionMaker, NextAction, BestTimeToReach) فوق الأساسية
- `LeadConvertDto`: فيها `ExpectedValue` + `Notes` + `EmployeeId`
- `LeadStatus` default = `"جديد"` (عربي مش English)
- `PagedResult<T>` موجود بالفعل في المشروع — بستخدم `PageNumber` مش `Page`

### 3.3 Services
| الملف | الوصف |
|-------|-------|
| `Services/ILeadsCrmService.cs` | Interface — 7 methods (باستخدام `using COCOBOLOERPNEW.Models`) |
| `Services/LeadsCrmService.cs` | Implementation كامل |
| `Services/LeadsCrmPermissions.cs` | Static class للصلاحيات |

**ILeadsCrmService Methods:**
```csharp
Task<PagedResult<LeadsCrmListDto>> GetLeadsAsync(LeadsCrmFilterDto filter);
Task<LeadsCrmDetailDto?> GetLeadByIdAsync(int leadId);
Task<(bool Success, string Message)> UpdateLeadAsync(LeadsCrmUpdateDto dto, string userName);
Task<(bool Success, string Message, int PartyId, int OpportunityId)> ConvertLeadToClientAsync(LeadConvertDto dto, string userName);
Task<LeadsCrmStatsDto> GetStatsAsync();
Task<List<Employee>> GetEmployeesAsync();
Task<(bool Success, string Message)> DeleteLeadAsync(int leadId, string userName);
```

**ملاحظات مهمة عن Service:**
- `GetLeadsAsync`: مقسمة 3 خطوات (leads → employee names → merge) لتجنب DbContext concurrency
- `GetEmployeesAsync`: بدون فلتر IsActive (الـ Employee model مفيهاش IsActive)
- `UpdateLeadAsync`: بتاخد DTO فيه LeadId (2 باراميتر بس: dto + userName)
- `ConvertLeadToClientAsync`: بتنشئ Party + SalesOpportunity + CustomerInteraction + CrmTask
- كل الـ statuses بالعربي: "جديد", "تم التواصل", "مؤهل", "محوّل", "مرفوض"
- `IAuditService.LogAsync<T>(string table, string action, string pk, T? oldData, T? newData, string userName)` — 6 باراميتر

**LeadsCrmPermissions Keys:**
- `frm_LeadsCRM:View`
- `frm_LeadsCRM:Add`
- `frm_LeadsCRM:Edit`
- `frm_LeadsCRM:Delete`

### 3.4 Endpoints
| الملف | الوصف |
|-------|-------|
| `Endpoints/LeadImportEndpoints.cs` | 3 endpoints للـ Google Apps Script |

**Endpoints:**
- `POST /api/leads/import` — استيراد lead واحد
- `POST /api/leads/import-batch` — استيراد مجموعة leads
- `GET /api/leads/ping` — فحص الاتصال

**ملاحظات:** Endpoint بيعمل insert لـ LeadsCRM بس (مفيش Party/Opportunity/Task). لازم نتأكد إن `LeadDate` بتتعبأ من بيانات الشيت مش `DateTime.Now` دايماً.

### 3.5 UI (Razor Pages)
| الملف | الوصف |
|-------|-------|
| `Components/Pages/CRM/LeadsList.razor` | صفحة إدارة Leads (`/crm/leads`) |

**مميزات الصفحة:**
- Header مع KPI row (إجمالي، جديد، تم التواصل، مؤهل، محوّل، مرفوض)
- بحث بـ Enter + زرار بحث (بدون Immediate — عشان نتجنب DbContext concurrency)
- Quick filter chips (جديد، تم التواصل، مؤهل)
- Filter bar (الحالة، المنصة، الموظف، من تاريخ، إلى تاريخ)
- Data table مع أزرار إجراءات (بدون MudTooltip — لأنها بتبتلع الـ click events)
- Detail Dialog بـ LeadsCrmDetailDto (بيانات كاملة من API + loading spinner)
- Edit Dialog بأزرار حالة ملونة + سبب رفض + feedback
- Convert Dialog باختيار موظف + قيمة فرصة + ملاحظات
- Delete Dialog بـ MudMessageBox

### 3.6 Configuration
| الملف | الوصف |
|-------|-------|
| `appsettings.json` | `"LeadImport": {"ApiKey": "cocobolo-meta-2026-xK9mP3vR7wQz"}` |
| `Program.cs` | `builder.Services.AddScoped<ILeadsCrmService, LeadsCrmService>();` + `app.MapLeadImportEndpoints();` |

### 3.7 SQL
- LeadsCRM table مع كل الأعمدة + Indexes على Phone, LeadStatus, IsConverted

---

## 4. المشاكل اللي اتحلت

| المشكلة | الحل |
|---------|------|
| FK_Tasks_Employees error عند استيراد leads | أعدنا كتابة endpoint يعمل insert لـ LeadsCRM بس |
| CS1061 db24804Context لا يحتوي على LeadsCRMs | أضفنا DbSet + OnModelCreating |
| CS0101 PagedResult مكرر | شيلنا PagedResult من LeadsCrmDto.cs |
| CS0117 PagedResult لا يحتوي على Page | غيّرنا لـ PageNumber |
| CS0246 ILeadsCrmService غير موجود | أنشأنا ملف الـ interface |
| Google Apps Script onEdit error | غيّرنا الاسم لـ onEditInstallable |
| DNS error على localhost | استخدمنا production URL |
| CS1026 ") expected" | مشكلة inline Style مع C# interpolation |
| Icons.Material.Rounded.PhoneInTalk غير موجود | غيّرناه لـ PhoneCallback |
| SearchText مش موجود في FilterDto | أضفناه |
| GetEmployeesAsync مش موجود في Interface | أضفناه |
| Employee.IsActive مش موجود | شيلنا الـ Where condition |
| DbContext concurrency error | قسّمنا GetLeadsAsync لـ 3 queries منفصلة + شيلنا Task.WhenAll + شيلنا Immediate="true" |
| _isBusy بيعمل block لكل العمليات | شيلناه خالص |
| MudTooltip بيبتلع Click events | شيلنا MudTooltip واستخدمنا Title="..." بداله |
| ApplyFilters مكررة مرتين | شيلنا واحدة |
| DeleteLeadAsync بترجع tuple مش bool | استخدمنا tuple deconstruction |

---

## 5. المشاكل المعلقة (Pending)

### 5.1 أولوية عالية 🔴
| # | المشكلة | التفاصيل |
|---|---------|---------|
| 1 | **أزرار الإجراءات مش شغالة** | لازم نتأكد إن شيلنا MudTooltip — المشكلة كانت إن MudTooltip بيبتلع الـ click events. لو لسه مش شغال ممكن يكون السبب تاني |
| 2 | **تاريخ الاستيراد** | الـ LeadImportEndpoints بيعمل `CreatedAt = DateTime.Now` بدل ما يأخذ التاريخ من الشيت. لازم نتأكد إن `LeadDate` بتتعبأ صح |
| 3 | **عرض التفاصيل** | الـ Detail Dialog بيعتمد على `GetLeadByIdAsync` — لازم نتأكد إن الـ method شغالة صح وبتجيب كل البيانات |

### 5.2 أولوية متوسطة 🟡
| # | المشكلة | التفاصيل |
|---|---------|---------|
| 4 | **Menu Item** | مفيش عنصر في الـ navigation menu بيودي لصفحة `/crm/leads` |
| 5 | **Permission Entries** | لازم نضيف صلاحيات frm_LeadsCRM في صفحة CRM Settings |
| 6 | **ContactSource** | ممكن نضيف "إعلان Meta" كـ ContactSource جديد في قاعدة البيانات |
| 7 | **Employee.IsActive** | الـ GetEmployeesAsync بتجيب كل الموظفين بدون فلتر — نحتاج نعرف الاسم الصح للخاصية |

### 5.3 أولوية منخفضة 🟢
| # | المشكلة | التفاصيل |
|---|---------|---------|
| 8 | **End-to-end test** | تجربة التدفق الكامل من Meta Ads لحد التحويل |
| 9 | **Manual Sync** | زرار "مزامنة من الشيت" دلوقتي بيعمل refresh بس — ممكن نعمل endpoint للمزامنة اليدوية |
| 10 | **LeadsByPlatform/ByCampaign** | الإحصائيات دي موجودة في الـ StatsDto بس مش معروضة في الـ UI |

---

## 6. Google Apps Script

### الإعداد:
- Silent version (مفيش onOpen menu)
- `installTrigger()` بتنشئ:
  - Time-based trigger كل 5 دقائق
  - onEditInstallable trigger (للتعديلات الفورية)
- بيستخدم production URL: `https://cocobolo.runasp.net`

### الـ Script Functions:
- `sendNewLeads()` → بتبعت leads جديدة لـ POST /api/leads/import-batch
- `onEditInstallable(e)` → بتبعت lead واحد لـ POST /api/leads/import
- `installTrigger()` → بتنشئ الـ triggers
- `deleteAllTriggers()` → بتمسح كل الـ triggers

---

## 7. Schema — LeadsCRM Table

### الأعمدة الأساسية:
- LeadId (PK, int, identity)
- FullName, Phone, Phone2, Email
- City, Area, Address

### بيانات Meta:
- MetaLeadId, CampaignId, CampaignName
- AdId, AdName, AdsetId, AdSetName
- FormId, FormName, Platform
- IsOrganic, InboxUrl, FormLanguage

### بيانات المشروع:
- ProjectType, ProjectStage, Budget
- DecisionMaker, NextAction, BestTimeToReach
- ProjectStageAlt, BudgetAlt

### حالة الـ Lead:
- LeadStatus (nvarchar: "جديد", "تم التواصل", "مؤهل", "محوّل", "مرفوض")
- IsConverted (bit)
- ConvertedPartyId, ConvertedOpportunityId, ConvertedDate, ConvertedBy
- IsDuplicate (bit), DuplicateOfPhone

### بيانات التتبع:
- AssignedEmployeeId (FK → Employees)
- LastContactDate, QualifiedDate
- RejectedReason, Feedback, Notes
- SheetTabName, SheetRowNumber
- ExtraData, LeadDate
- CreatedAt, CreatedBy

### Indexes:
- IX_LeadsCRM_Phone
- IX_LeadsCRM_LeadStatus
- IX_LeadsCRM_IsConverted

---

## 8. أنماط مهمة في المشروع

### 8.1 الصلاحيات:
```csharp
// Pattern: frm_FormName:Action
// Check: ClaimsPrincipal.HasClaim("Permission", key)
LeadsCrmPermissions.CanView(user);   // frm_LeadsCRM:View
LeadsCrmPermissions.CanEdit(user);   // frm_LeadsCRM:Edit
LeadsCrmPermissions.CanDelete(user); // frm_LeadsCRM:Delete
```

### 8.2 Audit Service:
```csharp
await _audit.LogAsync("LeadsCRM", "Update", leadId.ToString(), null, dto, userName);
// 6 باراميتر: table, action, pk, oldData, newData, userName
```

### 8.3 PagedResult<T>:
```csharp
// موجود في المشروع — DO NOT recreate
// Properties: Items, TotalCount, PageNumber, PageSize, TotalPages, HasPrevious, HasNext
// PageNumber مش Page!
```

### 8.4 MudBlazor Tips:
- **MudTooltip** بيبتلع click events — استخدم `Title="..."` بدله
- **Immediate="true"** في MudTextField بيعمل query مع كل حرف — خليه بـ Enter
- **MudDialog** يستخدم `@bind-IsOpen` للفتح والإغلاق
- **MudMessageBox** للـ confirm dialogs
- **MudSelectItem Value** لازم يكون cast صح: `@("جديد")` أو `@((int?)emp.EmployeeId)`

### 8.5 EF Core in Blazor Server:
- DbContext مش thread-safe — مينفعش تشغل استعلامين بالتوازي
- استخدم `await` متتابع (sequential) مش `Task.WhenAll`
- لو في sub-query (زي Employees جوه Leads)، اعملهم كـ queries منفصلة

---

## 9. الخطوات التالية (Next Steps)

### لما تكمل، ابدأ بالترتيب ده:

1. **✅ تأكد إن الأزرار شغالة** — لو MudTooltip لسه موجود، شيله
2. **✅ اختبر عرض التفاصيل** — افتح Dialog وشوف البيانات كاملة
3. **✅ اختبر التعديل** — غيّر حالة Lead واحفظ
4. **✅ اختبر التحويل** — حوّل Lead لعميل
5. **✅ أصلح تاريخ الاستيراد** — في LeadImportEndpoints.cs
6. **✅ أضف Menu Item** — في الـ sidebar navigation
7. **✅ أضف Permission Entries** — في صفحة CRM Settings
8. **✅ End-to-end test** — من الشيت لحد التحويل

---

## 10. ملاحظات عامة

- المستخدم بيتعامل بالعربي — لازم الـ UI والرسائل كلها عربي
- المستخدم بيحب نعمل خطوة خطوة ("خطوه خطوه") مع تأكيد كل خطوة
- Statuses في الداتابيز عربي: "جديد", "تم التواصل", "مؤهل", "محوّل", "مرفوض"
- الـ API Key: `cocobolo-meta-2026-xK9mP3vR7wQz`
- الـ Admin role: "Admin"
