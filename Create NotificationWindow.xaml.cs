using HashDash.Services;
using System.Windows;

namespace HashDash
{
    public partial class NotificationWindow : Window
    {
        private readonly NotificationService _notificationService;

        public NotificationWindow(NotificationService notificationService)
        {
            InitializeComponent();
            _notificationService = notificationService;
            
            // Bind to notifications
            lstNotifications.ItemsSource = _notificationService.Notifications;
            
            // Update UI
            UpdateNotificationCount();
            UpdateEmptyState();
            
            // Listen for changes
            _notificationService.Notifications.CollectionChanged += (s, e) =>
            {
                UpdateNotificationCount();
                UpdateEmptyState();
            };
        }

        private void UpdateNotificationCount()
        {
            int count = _notificationService.Notifications.Count;
            txtNotificationCount.Text = count == 0 
                ? "No notifications" 
                : $"{count} notification{(count == 1 ? "" : "s")}";
        }

        private void UpdateEmptyState()
        {
            bool isEmpty = _notificationService.Notifications.Count == 0;
            pnlEmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
            lstNotifications.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
        }

        private void BtnClearAll_Click(object sender, RoutedEventArgs e)
        {
            if (_notificationService.Notifications.Count == 0)
                return;

            var result = MessageBox.Show(
                "Are you sure you want to clear all notifications?",
                "Clear Notifications",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _notificationService.ClearAll();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}