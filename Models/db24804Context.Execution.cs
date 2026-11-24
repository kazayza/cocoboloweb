using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Models;

public partial class db24804Context
{
    public virtual DbSet<ExecutionOrder> ExecutionOrders { get; set; }
    public virtual DbSet<ExecutionOrderLine> ExecutionOrderLines { get; set; }
    public virtual DbSet<ExecutionStage> ExecutionStages { get; set; }
}
