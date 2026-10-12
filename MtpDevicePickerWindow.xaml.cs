using HashDash.Services;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace HashDash
{
    public partial class MtpDevicePickerWindow : Window
    {
        public MtpDeviceInfo? SelectedDevice { get; private set; }

        public MtpDevicePickerWindow()
        {
            InitializeComponent();
            Loaded += async (_, _) => await RefreshDevicesAsync();
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshDevicesAsync();
        }

        private async Task RefreshDevicesAsync()
        {
            btnLoad.IsEnabled = false;
            lstDevices.ItemsSource = null;
            txtEmptyState.Visibility = Visibility.Collapsed;

            try
            {
                var devices = await Task.Run(() => MtpDeviceService.GetConnectedDevices().ToList());
                lstDevices.ItemsSource = devices;

                if (devices.Count == 0)
                {
                    txtEmptyState.Visibility = Visibility.Visible;
                }
                else
                {
                    lstDevices.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error listing connected devices: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnLoad.IsEnabled = true;
            }
        }

        private void BtnLoad_Click(object sender, RoutedEventArgs e)
        {
            TryAccept();
        }

        private void LstDevices_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            TryAccept();
        }

        private void TryAccept()
        {
            if (lstDevices.SelectedItem is not MtpDeviceInfo device)
            {
                MessageBox.Show("Select a device to load files from.", "No Device Selected",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SelectedDevice = device;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
