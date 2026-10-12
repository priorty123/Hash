using HashDash.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace HashDash
{
    /// <summary>One row in the device picker: an iOS device over AFC, or any portable device over MTP/WPD.</summary>
    public class DevicePickerItem
    {
        public IosDeviceInfo? Ios { get; init; }
        public MtpDeviceInfo? Mtp { get; init; }

        public string Title    { get; init; } = "";
        public string Subtitle { get; init; } = "";
        public string Badge    { get; init; } = "";
        public bool   IsWarning { get; init; }
    }

    public partial class MtpDevicePickerWindow : Window
    {
        public DevicePickerItem? SelectedDevice { get; private set; }

        /// <summary>iOS only: scan the whole media partition rather than just the Photos library.</summary>
        public bool ScanEntireMediaPartition { get; private set; }

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
            txtIosNote.Visibility    = Visibility.Collapsed;

            try
            {
                var items = new List<DevicePickerItem>();

                var ios = await Task.Run(() =>
                {
                    try   { return (devices: IosAfcService.GetConnectedDevices(), error: (string?)null); }
                    catch (Exception ex) { return (devices: new List<IosDeviceInfo>(), error: ex.Message); }
                });

                foreach (var d in ios.devices)
                {
                    items.Add(new DevicePickerItem
                    {
                        Ios      = d,
                        Title    = d.FriendlyName,
                        Subtitle = $"{d.ProductType}  ·  iOS {d.ProductVersion}  ·  {d.Udid}",
                        Badge    = d.IsPaired
                            ? "Full Photos library — all albums, app saves, edits (existing trust reused)"
                            : "Not trusted by this PC — HashDash will not pair with it",
                        IsWarning = !d.IsPaired
                    });
                }

                var mtp = await Task.Run(() => MtpDeviceService.GetConnectedDevices().ToList());
                foreach (var d in mtp)
                {
                    bool isApple = d.FriendlyName.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ||
                                   d.FriendlyName.Contains("iPad",   StringComparison.OrdinalIgnoreCase) ||
                                   d.DeviceId.Contains("vid_05ac",   StringComparison.OrdinalIgnoreCase);
                    items.Add(new DevicePickerItem
                    {
                        Mtp      = d,
                        Title    = d.FriendlyName,
                        Subtitle = d.DeviceId,
                        Badge    = isApple
                            ? "Windows Portable Devices — Camera Roll (DCIM) only"
                            : "Windows Portable Devices (MTP)"
                    });
                }

                if (ios.error != null)
                {
                    txtIosNote.Text       = $"iPhone/iPad full-library access unavailable: {ios.error}";
                    txtIosNote.Visibility = Visibility.Visible;
                }

                lstDevices.ItemsSource = items;
                if (items.Count == 0)
                    txtEmptyState.Visibility = Visibility.Visible;
                else
                    lstDevices.SelectedIndex = 0;
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

        private void LstDevices_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            chkEntirePartition.Visibility = lstDevices.SelectedItem is DevicePickerItem { Ios: not null }
                ? Visibility.Visible
                : Visibility.Collapsed;
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
            if (lstDevices.SelectedItem is not DevicePickerItem device)
            {
                MessageBox.Show("Select a device to load files from.", "No Device Selected",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (device.Ios is { IsPaired: false })
            {
                MessageBox.Show(
                    "This iPhone/iPad has not trusted this PC.\n\n" +
                    "HashDash never pairs with a device itself: pairing makes the device store a new host " +
                    "record, which is a change to the exhibit. If you decide that is acceptable, establish " +
                    "trust outside HashDash (for example by opening Apple Devices and tapping Trust on the " +
                    "device), record it in your notes, then click Refresh.",
                    "Device Not Trusted", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SelectedDevice           = device;
            ScanEntireMediaPartition = chkEntirePartition.IsChecked == true;
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
