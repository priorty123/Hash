using HashDash.Models;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace HashDash.Services
{
    public class NotificationService
    {
        private static int _nextId = 1;
        private const int MaxNotifications = 50;
        
        public ObservableCollection<NotificationItem> Notifications { get; }
        
        public event EventHandler? NotificationAdded;
        
        public NotificationService()
        {
            Notifications = new ObservableCollection<NotificationItem>();
        }
        
        public void AddNotification(string title, string message, NotificationType type)
        {
            var notification = new NotificationItem
            {
                Id = _nextId++,
                Timestamp = DateTime.Now,
                Title = title,
                Message = message,
                Type = type,
                IsRead = false
            };
            
            // Add to beginning
            Notifications.Insert(0, notification);
            
            // Keep only last 50
            while (Notifications.Count > MaxNotifications)
            {
                Notifications.RemoveAt(Notifications.Count - 1);
            }
            
            NotificationAdded?.Invoke(this, EventArgs.Empty);
        }
        
        public void MarkAllAsRead()
        {
            foreach (var notification in Notifications)
            {
                notification.IsRead = true;
            }
        }
        
        public void ClearAll()
        {
            Notifications.Clear();
        }
        
        public int UnreadCount => Notifications.Count(n => !n.IsRead);
    }
}