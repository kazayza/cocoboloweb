using Microsoft.AspNetCore.Components;

namespace COCOBOLOERPNEW.Services;

/// <summary>
/// استثناء مخصص للتطبيق — بيستخدم لرمي أخطاء واضحة للمستخدم
/// </summary>
public class AppException : Exception
{
    public string UserMessage { get; }
    public string? Details { get; }

    public AppException(string userMessage, string? details = null)
        : base(userMessage)
    {
        UserMessage = userMessage;
        Details = details;
    }

    public AppException(string userMessage, Exception innerException)
        : base(userMessage, innerException)
    {
        UserMessage = userMessage;
        Details = innerException.Message;
    }
}

/// <summary>
/// خدمة مركزية لعرض الإشعارات (Snackbars) في كل الصفحات
/// </summary>
public class AppToastService
{
    /// <summary>
    /// عرض رسالة نجاح
    /// </summary>
    public static (string Message, string Severity) Success(string message)
        => (message, "Success");

    /// <summary>
    /// عرض رسالة خطأ
    /// </summary>
    public static (string Message, string Severity) Error(string message)
        => (message, "Error");

    /// <summary>
    /// عرض رسالة تحذير
    /// </summary>
    public static (string Message, string Severity) Warning(string message)
        => (message, "Warning");

    /// <summary>
    /// عرض رسالة معلومات
    /// </summary>
    public static (string Message, string Severity) Info(string message)
        => (message, "Info");
}
