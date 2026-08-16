using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class StockService : IStockService
{
    private readonly db24804Context _db;
    private readonly IAuditService _audit;

    public StockService(db24804Context db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<List<BranchListDto>> GetBranchesAsync()
    {
        return await _db.Branches
            .AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.BranchNameAr)
            .Select(b => new BranchListDto
            {
                BranchId = b.BranchId,
                BranchCode = b.BranchCode,
                BranchNameAr = b.BranchNameAr,
                BranchNameEn = b.BranchNameEn,
                Address = b.Address,
                Phone = b.Phone,
                ManagerEmployeeId = b.ManagerEmployeeId,
                ManagerEmployeeName = b.ManagerEmployee != null ? b.ManagerEmployee.FullName : null,
                IsActive = b.IsActive,
                CreatedAt = b.CreatedAt,
                CreatedBy = b.CreatedBy
            })
            .ToListAsync();
    }

    public async Task<List<WarehouseListDto>> GetWarehousesAsync(int? branchId = null)
    {
        var query = _db.Warehouses
            .AsNoTracking()
            .Include(w => w.Branch)
            .Where(w => w.IsActive == true);

        if (branchId.HasValue)
            query = query.Where(w => w.BranchId == branchId.Value);

        return await query
            .OrderBy(w => w.Branch != null ? w.Branch.BranchNameAr : "")
            .ThenBy(w => w.WarehouseName)
            .Select(w => new WarehouseListDto
            {
                WarehouseId = w.WarehouseId,
                WarehouseName = w.WarehouseName,
                BranchId = w.BranchId,
                BranchNameAr = w.Branch != null ? w.Branch.BranchNameAr : null,
                Location = w.Location,
                Notes = w.Notes,
                IsActive = w.IsActive ?? true,
                CreatedAt = w.CreatedAt,
                CreatedBy = w.CreatedBy
            })
            .ToListAsync();
    }

    public async Task<List<StockProductLookupDto>> SearchProductsAsync(string? searchText, int take = 30)
    {
        var query = from p in _db.Products.AsNoTracking()
                    join c in _db.Parties.AsNoTracking() on p.Customer equals c.PartyId into pc
                    from customer in pc.DefaultIfEmpty()
                    where !p.Customer.HasValue
                    select new StockProductLookupDto
                    {
                        ProductId = p.ProductId,
                        ProductName = p.ProductName,
                        ProductDescription = p.ProductDescription,
                        CustomerName = customer != null ? customer.PartyName : null
                    };

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var s = searchText.Trim();
            query = query.Where(x =>
                x.ProductId.ToString().Contains(s) ||
                x.ProductName.Contains(s) ||
                (x.ProductDescription != null && x.ProductDescription.Contains(s)) ||
                (x.CustomerName != null && x.CustomerName.Contains(s)));
        }

        return await query
            .OrderBy(x => x.ProductName)
            .Take(take)
            .ToListAsync();
    }

    public async Task<int> GetCurrentStockAsync(int productId, int warehouseId)
    {
        return await _db.StockLevels
            .AsNoTracking()
            .Where(s => s.ProductId == productId && s.WarehouseId == warehouseId)
            .Select(s => s.Quantity)
            .FirstOrDefaultAsync();
    }

    public async Task<List<StockProductLookupDto>> SearchProductsByWarehouseAsync(int warehouseId, string? searchText, int take = 30)
    {
        var query = from s in _db.StockLevels.AsNoTracking()
                    join p in _db.Products.AsNoTracking() on s.ProductId equals p.ProductId
                    join c in _db.Parties.AsNoTracking() on p.Customer equals c.PartyId into pc
                    from customer in pc.DefaultIfEmpty()
                    where s.WarehouseId == warehouseId && s.Quantity > 0 && !p.Customer.HasValue
                    select new StockProductLookupDto
                    {
                        ProductId = p.ProductId,
                        ProductName = p.ProductName,
                        ProductDescription = p.ProductDescription,
                        CustomerName = customer != null ? customer.PartyName : null
                    };

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var s = searchText.Trim();
            query = query.Where(x =>
                x.ProductId.ToString().Contains(s) ||
                x.ProductName.Contains(s) ||
                (x.ProductDescription != null && x.ProductDescription.Contains(s)) ||
                (x.CustomerName != null && x.CustomerName.Contains(s)));
        }

        return await query
            .Distinct()
            .OrderBy(x => x.ProductName)
            .Take(take)
            .ToListAsync();
    }

    public async Task<StockEntryResultDto> AddStockEntryAsync(StockEntryFormDto dto, string currentUserName)
    {
        if (!dto.BranchId.HasValue || dto.BranchId.Value <= 0)
            return new StockEntryResultDto { Success = false, Message = "اختيار الفرع مطلوب." };

        if (!dto.WarehouseId.HasValue || dto.WarehouseId.Value <= 0)
            return new StockEntryResultDto { Success = false, Message = "اختيار المخزن مطلوب." };

        if (!dto.ProductId.HasValue || dto.ProductId.Value <= 0)
            return new StockEntryResultDto { Success = false, Message = "اختيار المنتج مطلوب." };

        if (dto.Quantity <= 0)
            return new StockEntryResultDto { Success = false, Message = "الكمية يجب أن تكون أكبر من صفر." };

        var warehouse = await _db.Warehouses.AsNoTracking()
            .Include(w => w.Branch)
            .FirstOrDefaultAsync(w => w.WarehouseId == dto.WarehouseId.Value && w.IsActive == true);

        if (warehouse == null)
            return new StockEntryResultDto { Success = false, Message = "المخزن غير موجود أو غير نشط." };

        if (warehouse.BranchId != dto.BranchId)
            return new StockEntryResultDto { Success = false, Message = "المخزن المختار لا يتبع الفرع المحدد." };

        var product = await _db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProductId == dto.ProductId.Value);

        if (product == null)
            return new StockEntryResultDto { Success = false, Message = "المنتج غير موجود." };

        if (product.Customer.HasValue)
            return new StockEntryResultDto { Success = false, Message = "منتجات العملاء المخصصة لا تدخل مخزون المعرض ولا يمكن إضافتها من هذه الشاشة." };

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var now = DateTime.Now;
            var stock = await _db.StockLevels
                .FirstOrDefaultAsync(s => s.ProductId == dto.ProductId.Value && s.WarehouseId == dto.WarehouseId.Value);

            int oldQty = stock?.Quantity ?? 0;

            if (stock == null)
            {
                stock = new StockLevel
                {
                    ProductId = dto.ProductId.Value,
                    WarehouseId = dto.WarehouseId.Value,
                    Quantity = dto.Quantity,
                    CreatedBy = currentUserName,
                    CreatedAt = now,
                    LastUpdatedAt = now
                };
                _db.StockLevels.Add(stock);
            }
            else
            {
                stock.Quantity += dto.Quantity;
                stock.LastUpdatedAt = now;
            }

            var reasonLabel = dto.EntryReason switch
            {
                "OpeningBalance" => "رصيد افتتاحي",
                "ShowroomSupply" => "إدخال للمخزن / صالة العرض",
                "FactoryReceive" => "استلام من المصنع",
                "Adjustment" => "تسوية مخزنية",
                _ => dto.EntryReason
            };

            var transaction = new StockTransaction
            {
                ProductId = dto.ProductId.Value,
                WarehouseId = dto.WarehouseId.Value,
                TransactionType = "In",
                Quantity = dto.Quantity,
                TransactionDate = now,
                ReferenceId = null,
                ReferenceType = dto.EntryReason,
                UnitPrice = dto.UnitPrice,
                TotalAmount = dto.UnitPrice.HasValue ? dto.UnitPrice.Value * dto.Quantity : null,
                Notes = string.IsNullOrWhiteSpace(dto.Notes) ? reasonLabel : $"{reasonLabel} | {dto.Notes.Trim()}",
                CreatedBy = currentUserName,
                CreatedAt = now
            };

            _db.StockTransactions.Add(transaction);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            await _audit.LogAsync<object>(
                "StockTransactions",
                "ManualStockEntry",
                transaction.TransactionId.ToString(),
                null,
                new
                {
                    transaction.TransactionId,
                    transaction.ProductId,
                    ProductName = product.ProductName,
                    transaction.WarehouseId,
                    WarehouseName = warehouse.WarehouseName,
                    BranchId = warehouse.BranchId,
                    BranchName = warehouse.Branch?.BranchNameAr,
                    AddedQty = dto.Quantity,
                    OldQty = oldQty,
                    NewQty = stock.Quantity,
                    transaction.UnitPrice,
                    transaction.TotalAmount,
                    transaction.ReferenceType,
                    transaction.Notes
                },
                currentUserName);

            return new StockEntryResultDto
            {
                Success = true,
                Message = "تمت إضافة الرصيد للمخزن بنجاح.",
                StockTransactionId = transaction.TransactionId,
                NewQuantity = stock.Quantity
            };
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return new StockEntryResultDto
            {
                Success = false,
                Message = "حدث خطأ: " + (ex.InnerException?.Message ?? ex.Message)
            };
        }
    }

    public async Task<StockTransferResultDto> TransferStockAsync(StockTransferFormDto dto, string currentUserName)
    {
        if (!dto.FromBranchId.HasValue || dto.FromBranchId.Value <= 0)
            return new StockTransferResultDto { Success = false, Message = "اختيار فرع المصدر مطلوب." };

        if (!dto.FromWarehouseId.HasValue || dto.FromWarehouseId.Value <= 0)
            return new StockTransferResultDto { Success = false, Message = "اختيار مخزن المصدر مطلوب." };

        if (!dto.ToBranchId.HasValue || dto.ToBranchId.Value <= 0)
            return new StockTransferResultDto { Success = false, Message = "اختيار فرع الوجهة مطلوب." };

        if (!dto.ToWarehouseId.HasValue || dto.ToWarehouseId.Value <= 0)
            return new StockTransferResultDto { Success = false, Message = "اختيار مخزن الوجهة مطلوب." };

        if (dto.FromWarehouseId == dto.ToWarehouseId)
            return new StockTransferResultDto { Success = false, Message = "لا يمكن التحويل إلى نفس المخزن." };

        if (!dto.ProductId.HasValue || dto.ProductId.Value <= 0)
            return new StockTransferResultDto { Success = false, Message = "اختيار المنتج مطلوب." };

        if (dto.Quantity <= 0)
            return new StockTransferResultDto { Success = false, Message = "الكمية يجب أن تكون أكبر من صفر." };

        var fromWarehouse = await _db.Warehouses.AsNoTracking().Include(w => w.Branch)
            .FirstOrDefaultAsync(w => w.WarehouseId == dto.FromWarehouseId.Value && w.IsActive == true);
        var toWarehouse = await _db.Warehouses.AsNoTracking().Include(w => w.Branch)
            .FirstOrDefaultAsync(w => w.WarehouseId == dto.ToWarehouseId.Value && w.IsActive == true);

        if (fromWarehouse == null || toWarehouse == null)
            return new StockTransferResultDto { Success = false, Message = "مخزن المصدر أو الوجهة غير موجود أو غير نشط." };

        if (fromWarehouse.BranchId != dto.FromBranchId || toWarehouse.BranchId != dto.ToBranchId)
            return new StockTransferResultDto { Success = false, Message = "المخازن المختارة لا تتبع الفروع المحددة." };

        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.ProductId == dto.ProductId.Value);
        if (product == null)
            return new StockTransferResultDto { Success = false, Message = "المنتج غير موجود." };

        if (product.Customer.HasValue)
            return new StockTransferResultDto { Success = false, Message = "منتجات العملاء المخصصة لا تدخل التحويلات المخزنية الخاصة بالمعرض." };

        var sourceStock = await _db.StockLevels.FirstOrDefaultAsync(s => s.ProductId == dto.ProductId.Value && s.WarehouseId == dto.FromWarehouseId.Value);
        if (sourceStock == null || sourceStock.Quantity < dto.Quantity)
            return new StockTransferResultDto { Success = false, Message = "الكمية المتاحة في مخزن المصدر لا تكفي للتحويل." };

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var now = DateTime.Now;
            var transferRef = $"TR-{now:yyyyMMddHHmmssfff}";
            var latestUnitPrice = await _db.StockTransactions.AsNoTracking()
                .Where(t => t.ProductId == dto.ProductId.Value && t.WarehouseId == dto.FromWarehouseId.Value && t.UnitPrice != null)
                .OrderByDescending(t => t.TransactionDate)
                .ThenByDescending(t => t.TransactionId)
                .Select(t => t.UnitPrice)
                .FirstOrDefaultAsync();

            var destinationStock = await _db.StockLevels.FirstOrDefaultAsync(s => s.ProductId == dto.ProductId.Value && s.WarehouseId == dto.ToWarehouseId.Value);
            var oldSourceQty = sourceStock.Quantity;
            var oldDestinationQty = destinationStock?.Quantity ?? 0;

            sourceStock.Quantity -= dto.Quantity;
            sourceStock.LastUpdatedAt = now;

            if (destinationStock == null)
            {
                destinationStock = new StockLevel
                {
                    ProductId = dto.ProductId.Value,
                    WarehouseId = dto.ToWarehouseId.Value,
                    Quantity = dto.Quantity,
                    CreatedBy = currentUserName,
                    CreatedAt = now,
                    LastUpdatedAt = now
                };
                _db.StockLevels.Add(destinationStock);
            }
            else
            {
                destinationStock.Quantity += dto.Quantity;
                destinationStock.LastUpdatedAt = now;
            }

            var extraNotes = string.IsNullOrWhiteSpace(dto.Notes) ? string.Empty : $" | {dto.Notes.Trim()}";
            var outTx = new StockTransaction
            {
                ProductId = dto.ProductId.Value,
                WarehouseId = dto.FromWarehouseId.Value,
                TransactionType = "Out",
                Quantity = dto.Quantity,
                TransactionDate = now,
                ReferenceId = dto.ToWarehouseId,
                ReferenceType = "WarehouseTransferOut",
                UnitPrice = latestUnitPrice,
                TotalAmount = latestUnitPrice.HasValue ? latestUnitPrice.Value * dto.Quantity : null,
                Notes = $"تحويل مخزني {transferRef} إلى {toWarehouse.WarehouseName}{extraNotes}",
                CreatedBy = currentUserName,
                CreatedAt = now
            };

            var inTx = new StockTransaction
            {
                ProductId = dto.ProductId.Value,
                WarehouseId = dto.ToWarehouseId.Value,
                TransactionType = "In",
                Quantity = dto.Quantity,
                TransactionDate = now,
                ReferenceId = dto.FromWarehouseId,
                ReferenceType = "WarehouseTransferIn",
                UnitPrice = latestUnitPrice,
                TotalAmount = latestUnitPrice.HasValue ? latestUnitPrice.Value * dto.Quantity : null,
                Notes = $"تحويل مخزني {transferRef} من {fromWarehouse.WarehouseName}{extraNotes}",
                CreatedBy = currentUserName,
                CreatedAt = now
            };

            _db.StockTransactions.AddRange(outTx, inTx);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            await _audit.LogAsync<object>(
                "StockTransactions",
                "WarehouseTransfer",
                transferRef,
                null,
                new
                {
                    TransferReference = transferRef,
                    ProductId = dto.ProductId.Value,
                    ProductName = product.ProductName,
                    FromBranchId = fromWarehouse.BranchId,
                    FromBranchName = fromWarehouse.Branch?.BranchNameAr,
                    FromWarehouseId = fromWarehouse.WarehouseId,
                    FromWarehouseName = fromWarehouse.WarehouseName,
                    ToBranchId = toWarehouse.BranchId,
                    ToBranchName = toWarehouse.Branch?.BranchNameAr,
                    ToWarehouseId = toWarehouse.WarehouseId,
                    ToWarehouseName = toWarehouse.WarehouseName,
                    Quantity = dto.Quantity,
                    OldSourceQty = oldSourceQty,
                    NewSourceQty = sourceStock.Quantity,
                    OldDestinationQty = oldDestinationQty,
                    NewDestinationQty = destinationStock.Quantity,
                    UnitPrice = latestUnitPrice,
                    dto.Notes
                },
                currentUserName);

            return new StockTransferResultDto
            {
                Success = true,
                Message = "تم التحويل المخزني بنجاح.",
                TransferReference = transferRef,
                SourceNewQuantity = sourceStock.Quantity,
                DestinationNewQuantity = destinationStock.Quantity
            };
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return new StockTransferResultDto
            {
                Success = false,
                Message = "حدث خطأ: " + (ex.InnerException?.Message ?? ex.Message)
            };
        }
    }

    public async Task<List<StockTransactionListDto>> GetStockTransactionsAsync(StockTransactionFilterDto filter)
    {
        var query = _db.StockTransactions
            .AsNoTracking()
            .Include(t => t.Product)
            .Include(t => t.Warehouse)
                .ThenInclude(w => w.Branch)
            .AsQueryable();

        if (filter.BranchId.HasValue)
            query = query.Where(t => t.Warehouse.BranchId == filter.BranchId.Value);

        if (filter.WarehouseId.HasValue)
            query = query.Where(t => t.WarehouseId == filter.WarehouseId.Value);

        if (filter.ProductId.HasValue)
            query = query.Where(t => t.ProductId == filter.ProductId.Value);

        if (!string.IsNullOrWhiteSpace(filter.TransactionType))
            query = query.Where(t => t.TransactionType == filter.TransactionType);

        if (!string.IsNullOrWhiteSpace(filter.ReferenceType))
            query = query.Where(t => t.ReferenceType == filter.ReferenceType);

        if (filter.DateFrom.HasValue)
            query = query.Where(t => t.TransactionDate >= filter.DateFrom.Value.Date);

        if (filter.DateTo.HasValue)
            query = query.Where(t => t.TransactionDate < filter.DateTo.Value.Date.AddDays(1));

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var s = filter.SearchText.Trim();
            query = query.Where(t =>
                t.Product.ProductName.Contains(s)
                || (t.Warehouse.WarehouseName != null && t.Warehouse.WarehouseName.Contains(s))
                || (t.Warehouse.Branch != null && t.Warehouse.Branch.BranchNameAr.Contains(s))
                || (t.Notes != null && t.Notes.Contains(s))
                || (t.CreatedBy != null && t.CreatedBy.Contains(s)));
        }

        return await query
            .OrderByDescending(t => t.TransactionDate)
            .ThenByDescending(t => t.TransactionId)
            .Select(t => new StockTransactionListDto
            {
                TransactionId = t.TransactionId,
                ProductId = t.ProductId,
                ProductName = t.Product.ProductName,
                WarehouseId = t.WarehouseId,
                WarehouseName = t.Warehouse.WarehouseName,
                BranchId = t.Warehouse.BranchId,
                BranchNameAr = t.Warehouse.Branch != null ? t.Warehouse.Branch.BranchNameAr : null,
                TransactionType = t.TransactionType,
                Quantity = t.Quantity,
                TransactionDate = t.TransactionDate,
                ReferenceId = t.ReferenceId,
                ReferenceType = t.ReferenceType,
                UnitPrice = t.UnitPrice,
                TotalAmount = t.TotalAmount,
                Notes = t.Notes,
                CreatedBy = t.CreatedBy,
                CreatedAt = t.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<StockCountWorkspaceDto?> GetStockCountWorkspaceAsync(int warehouseId, int? stockCountId = null)
    {
        if (stockCountId.HasValue)
        {
            var existing = await _db.StockCountHeaders
                .AsNoTracking()
                .Include(h => h.Branch)
                .Include(h => h.Warehouse)
                .Include(h => h.Lines)
                    .ThenInclude(l => l.Product)
                .FirstOrDefaultAsync(h => h.StockCountId == stockCountId.Value);

            if (existing == null)
                return null;

            return new StockCountWorkspaceDto
            {
                StockCountId = existing.StockCountId,
                BranchId = existing.BranchId,
                BranchNameAr = existing.Branch.BranchNameAr,
                WarehouseId = existing.WarehouseId,
                WarehouseName = existing.Warehouse.WarehouseName,
                CountDate = existing.CountDate,
                Status = existing.Status,
                Notes = existing.Notes,
                Lines = existing.Lines
                    .OrderBy(l => l.Product.ProductName)
                    .Select(l => new StockCountLineDto
                    {
                        StockCountLineId = l.StockCountLineId,
                        ProductId = l.ProductId,
                        ProductName = l.Product.ProductName,
                        SystemQty = l.SystemQty,
                        ActualQty = l.ActualQty,
                        DifferenceQty = l.DifferenceQty,
                        Notes = l.Notes
                    })
                    .ToList()
            };
        }

        var warehouse = await _db.Warehouses.AsNoTracking()
            .Include(w => w.Branch)
            .FirstOrDefaultAsync(w => w.WarehouseId == warehouseId && w.IsActive == true);

        if (warehouse == null || !warehouse.BranchId.HasValue)
            return null;

        var lines = await _db.StockLevels.AsNoTracking()
            .Include(s => s.Product)
            .Where(s => s.WarehouseId == warehouseId && !s.Product.Customer.HasValue)
            .OrderBy(s => s.Product.ProductName)
            .Select(s => new StockCountLineDto
            {
                ProductId = s.ProductId,
                ProductName = s.Product.ProductName,
                SystemQty = s.Quantity,
                ActualQty = s.Quantity,
                DifferenceQty = 0,
                Notes = null
            })
            .ToListAsync();

        return new StockCountWorkspaceDto
        {
            BranchId = warehouse.BranchId.Value,
            BranchNameAr = warehouse.Branch?.BranchNameAr ?? "",
            WarehouseId = warehouse.WarehouseId,
            WarehouseName = warehouse.WarehouseName,
            CountDate = DateTime.Now,
            Status = "Draft",
            Lines = lines
        };
    }

    public async Task<List<StockCountHeaderListDto>> GetStockCountsAsync(StockCountFilterDto filter)
    {
        var query = _db.StockCountHeaders
            .AsNoTracking()
            .Include(h => h.Branch)
            .Include(h => h.Warehouse)
            .Include(h => h.Lines)
            .AsQueryable();

        if (filter.BranchId.HasValue)
            query = query.Where(h => h.BranchId == filter.BranchId.Value);

        if (filter.WarehouseId.HasValue)
            query = query.Where(h => h.WarehouseId == filter.WarehouseId.Value);

        return await query
            .OrderByDescending(h => h.CountDate)
            .ThenByDescending(h => h.StockCountId)
            .Select(h => new StockCountHeaderListDto
            {
                StockCountId = h.StockCountId,
                BranchId = h.BranchId,
                BranchNameAr = h.Branch.BranchNameAr,
                WarehouseId = h.WarehouseId,
                WarehouseName = h.Warehouse.WarehouseName,
                CountDate = h.CountDate,
                Status = h.Status,
                CreatedBy = h.CreatedBy,
                CreatedAt = h.CreatedAt,
                FinalizedBy = h.FinalizedBy,
                FinalizedAt = h.FinalizedAt,
                LinesCount = h.Lines.Count,
                DifferenceItemsCount = h.Lines.Count(l => l.DifferenceQty != 0)
            })
            .ToListAsync();
    }

    public async Task<(bool Success, string Message, int? StockCountId)> SaveStockCountDraftAsync(StockCountWorkspaceDto dto, string currentUserName)
    {
        if (dto.BranchId <= 0)
            return (false, "اختيار الفرع مطلوب.", null);
        if (dto.WarehouseId <= 0)
            return (false, "اختيار المخزن مطلوب.", null);
        if (dto.Lines == null || dto.Lines.Count == 0)
            return (false, "لا توجد أصناف للجرد.", null);

        var warehouse = await _db.Warehouses.AsNoTracking()
            .FirstOrDefaultAsync(w => w.WarehouseId == dto.WarehouseId && w.IsActive == true);
        if (warehouse == null || warehouse.BranchId != dto.BranchId)
            return (false, "المخزن المختار لا يتبع الفرع المحدد.", null);

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            StockCountHeader header;
            object? oldData = null;
            var isNew = !dto.StockCountId.HasValue || dto.StockCountId.Value == 0;

            if (isNew)
            {
                header = new StockCountHeader
                {
                    BranchId = dto.BranchId,
                    WarehouseId = dto.WarehouseId,
                    CountDate = dto.CountDate == default ? DateTime.Now : dto.CountDate,
                    Status = "Draft",
                    Notes = dto.Notes,
                    CreatedBy = currentUserName,
                    CreatedAt = DateTime.Now
                };
                _db.StockCountHeaders.Add(header);
                await _db.SaveChangesAsync();
            }
            else
            {
                header = await _db.StockCountHeaders.Include(h => h.Lines).FirstOrDefaultAsync(h => h.StockCountId == dto.StockCountId.Value)
                    ?? throw new Exception("مستند الجرد غير موجود.");

                if (string.Equals(header.Status, "Finalized", StringComparison.OrdinalIgnoreCase))
                    return (false, "لا يمكن تعديل جرد معتمد.", null);

                oldData = new
                {
                    header.StockCountId,
                    header.BranchId,
                    header.WarehouseId,
                    header.CountDate,
                    header.Status,
                    header.Notes,
                    LinesCount = header.Lines.Count
                };

                header.BranchId = dto.BranchId;
                header.WarehouseId = dto.WarehouseId;
                header.CountDate = dto.CountDate == default ? header.CountDate : dto.CountDate;
                header.Notes = dto.Notes;

                _db.StockCountLines.RemoveRange(header.Lines);
                await _db.SaveChangesAsync();
            }

            var lines = dto.Lines.Select(l => new StockCountLine
            {
                StockCountId = header.StockCountId,
                ProductId = l.ProductId,
                SystemQty = l.SystemQty,
                ActualQty = l.ActualQty,
                DifferenceQty = l.ActualQty - l.SystemQty,
                Notes = l.Notes
            }).ToList();

            _db.StockCountLines.AddRange(lines);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            await _audit.LogAsync<object>(
                "StockCountHeaders",
                isNew ? "Insert" : "Update",
                header.StockCountId.ToString(),
                oldData,
                new
                {
                    header.StockCountId,
                    header.BranchId,
                    header.WarehouseId,
                    header.CountDate,
                    header.Status,
                    header.Notes,
                    LinesCount = lines.Count,
                    DifferenceItemsCount = lines.Count(x => x.DifferenceQty != 0)
                },
                currentUserName);

            return (true, "تم حفظ مسودة الجرد بنجاح.", header.StockCountId);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return (false, "حدث خطأ: " + (ex.InnerException?.Message ?? ex.Message), null);
        }
    }

    public async Task<(bool Success, string Message)> FinalizeStockCountAsync(int stockCountId, string currentUserName)
    {
        var header = await _db.StockCountHeaders
            .Include(h => h.Branch)
            .Include(h => h.Warehouse)
            .Include(h => h.Lines)
            .FirstOrDefaultAsync(h => h.StockCountId == stockCountId);

        if (header == null)
            return (false, "مستند الجرد غير موجود.");

        if (string.Equals(header.Status, "Finalized", StringComparison.OrdinalIgnoreCase))
            return (false, "هذا الجرد معتمد بالفعل.");

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var now = DateTime.Now;
            foreach (var line in header.Lines)
            {
                var stock = await _db.StockLevels.FirstOrDefaultAsync(s => s.ProductId == line.ProductId && s.WarehouseId == header.WarehouseId);
                var currentQty = stock?.Quantity ?? 0;
                var delta = line.ActualQty - currentQty;

                if (stock == null)
                {
                    stock = new StockLevel
                    {
                        ProductId = line.ProductId,
                        WarehouseId = header.WarehouseId,
                        Quantity = line.ActualQty,
                        CreatedBy = currentUserName,
                        CreatedAt = now,
                        LastUpdatedAt = now
                    };
                    _db.StockLevels.Add(stock);
                }
                else
                {
                    stock.Quantity = line.ActualQty;
                    stock.LastUpdatedAt = now;
                }

                if (delta != 0)
                {
                    _db.StockTransactions.Add(new StockTransaction
                    {
                        ProductId = line.ProductId,
                        WarehouseId = header.WarehouseId,
                        TransactionType = delta > 0 ? "In" : "Out",
                        Quantity = Math.Abs(delta),
                        TransactionDate = now,
                        ReferenceId = header.StockCountId,
                        ReferenceType = delta > 0 ? "StockCountAdjustmentIn" : "StockCountAdjustmentOut",
                        UnitPrice = null,
                        TotalAmount = null,
                        Notes = $"تسوية جرد رقم #{header.StockCountId}" + (string.IsNullOrWhiteSpace(line.Notes) ? "" : $" | {line.Notes}"),
                        CreatedBy = currentUserName,
                        CreatedAt = now
                    });
                }
            }

            header.Status = "Finalized";
            header.FinalizedAt = now;
            header.FinalizedBy = currentUserName;

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            await _audit.LogAsync<object>(
                "StockCountHeaders",
                "Finalize",
                header.StockCountId.ToString(),
                null,
                new
                {
                    header.StockCountId,
                    header.BranchId,
                    header.WarehouseId,
                    header.Status,
                    header.FinalizedAt,
                    header.FinalizedBy,
                    DifferenceItemsCount = header.Lines.Count(x => x.DifferenceQty != 0)
                },
                currentUserName);

            return (true, "تم اعتماد الجرد وتنفيذ التسويات المخزنية بنجاح.");
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return (false, "حدث خطأ: " + (ex.InnerException?.Message ?? ex.Message));
        }
    }
}
