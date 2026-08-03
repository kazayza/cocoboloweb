using COCOBOLOERPNEW.DTOs;
using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services;

public class B2BPortalService : IB2BPortalService
{
    private readonly IDbContextFactory<db24804Context> _factory;
    private readonly IB2BRequestService _requestService;

    public B2BPortalService(IDbContextFactory<db24804Context> factory, IB2BRequestService requestService)
    {
        _factory = factory;
        _requestService = requestService;
    }

    public async Task<B2BPortalDashboardDto> GetDashboardAsync(int partyId, int portalUserId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var partyName = await db.Parties.AsNoTracking()
            .Where(p => p.PartyId == partyId)
            .Select(p => p.PartyName)
            .FirstOrDefaultAsync() ?? "عميل B2B";

        var portalUserName = await db.B2BPortalUsers.AsNoTracking()
            .Where(x => x.PortalUserId == portalUserId)
            .Select(x => x.FullName)
            .FirstOrDefaultAsync() ?? "مستخدم البوابة";

        var openQuotations = await db.Quotations.AsNoTracking()
            .CountAsync(x => x.PartyId == partyId && x.InvoiceId == null && x.Status != QuotationStatuses.Rejected && x.Status != QuotationStatuses.Converted);

        var openInvoices = await db.Transactions.AsNoTracking()
            .CountAsync(x => x.PartyId == partyId && x.TransactionType == TransactionTypes.Sale && x.InvoiceStatus != "Paid" && x.InvoiceStatus != "Cancelled");

        var outstandingAmount = await db.Transactions.AsNoTracking()
            .Where(x => x.PartyId == partyId && x.TransactionType == TransactionTypes.Sale && x.InvoiceStatus != "Cancelled")
            .SumAsync(x => (decimal?)(x.GrandTotal - x.PaidAmount)) ?? 0m;

        var pendingDeliveries = await db.VwSalesDeliveryStatuses.AsNoTracking()
            .CountAsync(x => x.PartyId == partyId && x.DeliveryStatus != "تم التسليم");

        return new B2BPortalDashboardDto
        {
            PartyName = partyName,
            PortalUserName = portalUserName,
            OpenQuotationsCount = openQuotations,
            OpenInvoicesCount = openInvoices,
            PendingDeliveriesCount = pendingDeliveries,
            OutstandingAmount = outstandingAmount,
            RecentRequests = (await _requestService.GetRequestsAsync(partyId: partyId)).Take(5).ToList()
        };
    }
}
