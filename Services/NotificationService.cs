using COCOBOLOERPNEW.Models;
using Microsoft.EntityFrameworkCore;

namespace COCOBOLOERPNEW.Services
{
    public class NotificationService
    {
        private readonly db24804Context _db;

        public NotificationService(db24804Context db)
        {
            _db = db;
        }

        // جلب عدد الإشعارات غير المقروءة
        public async Task<int> GetUnreadCountAsync(string username, string? role)
    {
        return await _db.Notifications
            .Where(n => !n.IsRead && (n.RecipientUser == username || n.RecipientUser == role))
            .CountAsync();
    }

        // جلب آخر 10 إشعارات
        public async Task<List<Notification>> GetLatestAsync(string username, string? role, int count = 10)
    {
        return await _db.Notifications
            .Where(n => n.RecipientUser == username || n.RecipientUser == role)
            .OrderByDescending(n => n.CreatedAt)
            .Take(count)
            .ToListAsync();
    }

        // تعليم إشعار كمقروء
        public async Task MarkAsReadAsync(int notificationId)
        {
            var notification = await _db.Notifications.FindAsync(notificationId);
            if (notification != null && !notification.IsRead)
            {
                notification.IsRead = true;
                notification.ReadAt = DateTime.Now;
                await _db.SaveChangesAsync();
            }
        }

        // إرسال إشعار جديد
        public async Task AddAsync(string title, string message, string recipientUser, string createdBy, string? formName = null, string? relatedTable = null, int? relatedId = null)
        {
            var notification = new Notification
            {
                Title = title,
                Message = message,
                RecipientUser = recipientUser,
                CreatedBy = createdBy,
                FormName = formName,
                RelatedTable = relatedTable,
                RelatedId = relatedId,
                CreatedAt = DateTime.Now
            };
            _db.Notifications.Add(notification);
            await _db.SaveChangesAsync();
        }
            // إرسال إشعار لدور معين (زي Admin, Sales, etc.)
    public async Task NotifyRoleAsync(string title, string message, string role, string createdBy, string? formName = null, string? relatedTable = null, int? relatedId = null)
    {
        var notification = new Notification
        {
            Title = title,
            Message = message,
            RecipientUser = role, // هنخزن اسم الدور هنا
            CreatedBy = createdBy,
            FormName = formName,
            RelatedTable = relatedTable,
            RelatedId = relatedId,
            CreatedAt = DateTime.Now
        };
        _db.Notifications.Add(notification);
        await _db.SaveChangesAsync();
    }
    }

}