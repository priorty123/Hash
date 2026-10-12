using Microsoft.Toolkit.Uwp.Notifications;
using HashDash.Models;
using System;

namespace HashDash.Services
{
    public class ToastNotificationService
    {
        private readonly NotificationService? _notificationService;

        // ✅ Constructor accepts NotificationService
        public ToastNotificationService(NotificationService? notificationService = null)
        {
            _notificationService = notificationService;
        }

        public void ShowSuccess(string title, string message)
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(message)
                .Show();

            // ✅ Also add to bell notifications
            _notificationService?.AddNotification(title, message, NotificationType.Success);
        }

        public void ShowWarning(string title, string message)
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(message)
                .Show();

            // ✅ Also add to bell notifications
            _notificationService?.AddNotification(title, message, NotificationType.Warning);
        }

        public void ShowError(string title, string message)
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(message)
                .Show();

            // ✅ Also add to bell notifications
            _notificationService?.AddNotification(title, message, NotificationType.Error);
        }

        public void ShowAutomationComplete(int totalFiles, int successful, int failed, TimeSpan duration)
        {
            string title = failed > 0
                ? "⚠ Automation Completed with Warnings"
                : "✓ Automation Completed";

            string message = failed > 0
                ? $"Processed: {totalFiles} | Successful: {successful} | Failed: {failed}"
                : $"Successfully processed {totalFiles} files in {duration.Hours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}";

            new ToastContentBuilder()
                .AddText(title)
                .AddText(message)
                .Show();

            // ✅ Also add to bell notifications
            var type = failed > 0 ? NotificationType.Warning : NotificationType.Success;
            _notificationService?.AddNotification(title, message, type);
        }
    }
}