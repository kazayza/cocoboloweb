using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class Calendar
{
    public DateTime CalendarDate { get; set; }

    public byte? DayOfWeek { get; set; }

    public string? DayName { get; set; }

    public bool? IsWeekend { get; set; }

    public bool? IsHoliday { get; set; }

    public DateTime? CreatedAt { get; set; }
}
