using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class Employee
{
    public int EmployeeId { get; set; }

    public string FullName { get; set; } = null!;

    public string? JobTitle { get; set; }

    public string? Department { get; set; }

    public string NationalId { get; set; } = null!;

    public string? Gender { get; set; }

    public DateTime? BirthDate { get; set; }

    public string? Qualification { get; set; }

    public string? Yearqualification { get; set; }

    public string? Address { get; set; }

    public string? MobilePhone { get; set; }

    public string? MobilePhone2 { get; set; }

    public string? EmailAddress { get; set; }

    public DateTime? HireDate { get; set; }

    public DateTime? EndDate { get; set; }

    public int? BioEmployeeId { get; set; }

    public bool? IsPermanentlyExempt { get; set; }

    public decimal? CurrentSalaryBase { get; set; }

    public string Status { get; set; } = null!;

    public string? Notes { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<ComplaintFollowUp> ComplaintFollowUps { get; set; } = new List<ComplaintFollowUp>();

    public virtual ICollection<Complaint> Complaints { get; set; } = new List<Complaint>();

    public virtual ICollection<CrmTask> CrmTasks { get; set; } = new List<CrmTask>();

    public virtual ICollection<CustomerInteraction> CustomerInteractions { get; set; } = new List<CustomerInteraction>();

    public virtual ICollection<Payroll> Payrolls { get; set; } = new List<Payroll>();

    public virtual ICollection<SalaryHistory> SalaryHistories { get; set; } = new List<SalaryHistory>();

    public virtual ICollection<SalesOpportunity> SalesOpportunities { get; set; } = new List<SalesOpportunity>();
}
