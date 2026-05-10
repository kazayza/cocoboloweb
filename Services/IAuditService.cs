using System.Threading.Tasks;

namespace COCOBOLOERPNEW.Services
{
    public interface IAuditService
    {
        Task LogAsync<T>(string tableName, string actionType, string primaryKeyValue, T? oldData, T? newData, string userName);
    }
}