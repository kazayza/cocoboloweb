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
            var roles = new List<string>();
            if (!string.IsNullOrEmpty(role)) roles.Add(role);
            return await GetUnreadCountAsync(username, roles);
        }

        public async Task<int> GetUnreadCountAsync(string username, List<string> roles)
        {
            return await _db.Notifications
                .Where(n => !n.IsRead && (n.RecipientUser == username || roles.Contains(n.RecipientUser)))
                .CountAsync();
        }

        // جلب آخر الإشعارات (إعطاء أولوية لغير المقروء أولاً)
        public async Task<List<Notification>> GetLatestAsync(string username, string? role, int count = 10)
        {
            var roles = new List<string>();
            if (!string.IsNullOrEmpty(role)) roles.Add(role);
            return await GetLatestAsync(username, roles, count);
        }

        public async Task<List<Notification>> GetLatestAsync(string username, List<string> roles, int count = 10)
        {
            return await BuildUserQuery(username, roles)
                .OrderBy(n => n.IsRead)
                .ThenByDescending(n => n.CreatedAt)
                .Take(count)
                .ToListAsync();
        }

        public async Task<List<Notification>> GetNotificationsPageAsync(
            string username,
            List<string> roles,
            string? searchText = null,
            bool? unreadOnly = null,
            int skip = 0,
            int take = 100)
        {
            var query = BuildUserQuery(username, roles);

            if (unreadOnly.HasValue)
                query = query.Where(n => n.IsRead != unreadOnly.Value);

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                var s = searchText.Trim();
                query = query.Where(n =>
                    (n.Title != null && n.Title.Contains(s)) ||
                    (n.Message != null && n.Message.Contains(s)) ||
                    (n.CreatedBy != null && n.CreatedBy.Contains(s)));
            }

            return await query
                .OrderByDescending(n => n.CreatedAt)
                .Skip(skip)
                .Take(take)
                .ToListAsync();
        }

        public async Task<int> GetNotificationsCountAsync(
            string username,
            List<string> roles,
            string? searchText = null,
            bool? unreadOnly = null)
        {
            var query = BuildUserQuery(username, roles);

            if (unreadOnly.HasValue)
                query = query.Where(n => n.IsRead != unreadOnly.Value);

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                var s = searchText.Trim();
                query = query.Where(n =>
                    (n.Title != null && n.Title.Contains(s)) ||
                    (n.Message != null && n.Message.Contains(s)) ||
                    (n.CreatedBy != null && n.CreatedBy.Contains(s)));
            }

            return await query.CountAsync();
        }

        public async Task MarkAllAsReadForUserAsync(string username, List<string> roles)
        {
            var unread = await _db.Notifications
                .Where(n => !n.IsRead && (n.RecipientUser == username || roles.Contains(n.RecipientUser)))
                .ToListAsync();
            foreach (var n in unread)
            {
                n.IsRead = true;
                n.ReadAt = DateTime.Now;
            }
            await _db.SaveChangesAsync();
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
            // Expand role → each active username (Flutter needs RecipientUser = username)
            var roleNorm = (role ?? string.Empty).Replace(" ", "").ToLowerInvariant();

            var users = await _db.Users
                .AsNoTracking()
                .Where(u => u.IsActive != false && u.Role != null && u.Username != null)
                .Select(u => new { u.Username, u.Role })
                .ToListAsync();

            var recipients = users
                .Where(u =>
                {
                    var r = (u.Role ?? string.Empty).Replace(" ", "").ToLowerInvariant();
                    return r == roleNorm
                           || string.Equals(u.Role, role, StringComparison.OrdinalIgnoreCase);
                })
                .Select(u => u.Username!)
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (recipients.Count == 0)
            {
                // legacy fallback: single role row (web still matches via roles.Contains)
                _db.Notifications.Add(new Notification
                {
                    Title = title,
                    Message = message,
                    RecipientUser = role,
                    CreatedBy = createdBy,
                    FormName = formName,
                    RelatedTable = relatedTable,
                    RelatedId = relatedId,
                    CreatedAt = DateTime.Now
                });
                await _db.SaveChangesAsync();
                return;
            }

            foreach (var username in recipients)
            {
                if (string.Equals(username, createdBy, StringComparison.OrdinalIgnoreCase))
                    continue;

                _db.Notifications.Add(new Notification
                {
                    Title = title,
                    Message = message,
                    RecipientUser = username,
                    CreatedBy = createdBy,
                    FormName = formName,
                    RelatedTable = relatedTable,
                    RelatedId = relatedId,
                    CreatedAt = DateTime.Now
                });
            }

            await _db.SaveChangesAsync();

            // Optional FCM via mobile Node API
            try
            {
                var apiBase = Environment.GetEnvironmentVariable("MOBILE_API_BASE_URL");
                if (string.IsNullOrWhiteSpace(apiBase)) return;

                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                foreach (var username in recipients)
                {
                    if (string.Equals(username, createdBy, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var payload = new
                    {
                        title,
                        message,
                        recipientUser = username,
                        relatedTable,
                        relatedId,
                        formName,
                        createdBy,
                        // بلازور حفظ الإشعار في قاعدة البيانات بالفعل
                        // فنخبر الـ Node API ألا يحفظه مرة أخرى (نفس الـ DB)
                        skipSave = true
                    };
                    await http.PostAsJsonAsync($"{apiBase.TrimEnd('/')}/api/notifications", payload);
                }
            }
            catch
            {
                // never break business flow
            }
        }

        private IQueryable<Notification> BuildUserQuery(string username, List<string> roles)
        {
            return _db.Notifications
                .AsNoTracking()
                .Where(n => n.RecipientUser == username || roles.Contains(n.RecipientUser));
        }
    }

}