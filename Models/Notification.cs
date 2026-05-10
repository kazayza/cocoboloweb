using System;
using System.Collections.Generic;

namespace COCOBOLOERPNEW.Models;

public partial class Notification
{
    public int NotificationId { get; set; }

    public string Title { get; set; } = null!;

    public string Message { get; set; } = null!;

    public string? RelatedTable { get; set; }

    public int? RelatedId { get; set; }

    public string? RecipientUser { get; set; }

    public bool IsRead { get; set; }

    public string CreatedBy { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime? ReadAt { get; set; }

    public string? FormName { get; set; }

    public bool ReminderEnabled { get; set; }

    public int? ReminderIntervalMinutes { get; set; }

    public DateTime? ReminderNextAt { get; set; }

    public DateTime? ReminderEndAt { get; set; }

    public DateTime? LastReminderAt { get; set; }
}
