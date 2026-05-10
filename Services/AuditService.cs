using System;
using System.Text.Json;
using System.Threading.Tasks;
using COCOBOLOERPNEW.Models;

namespace COCOBOLOERPNEW.Services
{
    public class AuditService : IAuditService
    {
        private readonly db24804Context _context;

        public AuditService(db24804Context context)
        {
            _context = context;
        }

        public async Task LogAsync<T>(string tableName, string actionType, string primaryKeyValue, T? oldData, T? newData, string userName)
        {
            try
            {
                                var options = new JsonSerializerOptions
                {
                    WriteIndented = false,
                    ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping // ✅ يكتب العربي كما هو
                };

                var log = new AuditLog
                {
                    TableName       = tableName,
                    ActionType      = actionType,
                    PrimaryKeyValue = primaryKeyValue,
                    OldData         = oldData != null ? JsonSerializer.Serialize(oldData, options) : null,
                    NewData         = newData != null ? JsonSerializer.Serialize(newData, options) : null,
                    ActionDate      = DateTime.Now,
                    LoginName       = userName,
                    AccessUserName  = userName,
                    AppName         = "COCOBOLOERP",
                    HostName        = Environment.MachineName
                };

                _context.AuditLogs.Add(log);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuditService] Error: {ex.Message}");
            }
        }
    }
}