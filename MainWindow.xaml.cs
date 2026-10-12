using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HashDash.Models;
using HashDash.ViewModels;
using Microsoft.Win32;

namespace HashDash
{

public class VersionExtension : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        // 1. Try to get the "Informational Version" (the one from the <Version> tag)
        var infoVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrEmpty(infoVersion))
        {
            // The compiler sometimes adds a commit hash after a '+' sign (e.g., 1.2.3+a1b2c3d)
            // This line trims it off so you only see the version number
            return $"Version {infoVersion.Split('+')[0]}";
        }

        // 2. Fallback to the standard numeric version if the above fails
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return $"Version {version?.Major}.{version?.Minor}.{version?.Build}";
    }
}



    public partial class MainWindow : Window
    {
        private MainViewModel ViewModel => (MainViewModel)DataContext;
        private AppSettings _appSettings = AppSettings.Load();

        public MainWindow()
    {
        InitializeComponent();

        RestoreColumnVisibility();
        RestoreColumnWidths();

        Closing += (_, _) => SaveColumnLayout();

        // Wire column resize to AFTER Run completes, not to file loading.
        // Hash values only exist once IsProcessing transitions to false.
        if (DataContext is MainViewModel vm)
        {
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.IsProcessing)
                    && !vm.IsProcessing)
                {
                    RefreshColumnWidths();
                }
            };
        }
    }

    private void RestoreColumnVisibility()
    {
        if (!_appSettings.ColumnVisibilityConfigured) return;

        foreach (var col in dgFiles.Columns)
        {
            if (col.Header is not string colName || string.IsNullOrEmpty(colName)) continue;
            col.Visibility = _appSettings.HiddenColumns.Contains(colName)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }

    private void RestoreColumnWidths()
    {
        if (_appSettings.ColumnWidths.Count == 0) return;

        foreach (var col in dgFiles.Columns)
        {
            if (col.Header is not string colName || string.IsNullOrEmpty(colName)) continue;
            if (_appSettings.ColumnWidths.TryGetValue(colName, out double width) && width > 0)
                col.Width = new DataGridLength(width);
        }
    }

    private void SaveColumnLayout()
    {
        // Reload from disk first so we only overwrite column-layout fields and
        // never clobber settings (e.g. LogDirectory) changed via SettingsWindow.
        _appSettings = AppSettings.Load();

        _appSettings.HiddenColumns.Clear();
        _appSettings.ColumnWidths.Clear();

        foreach (var col in dgFiles.Columns)
        {
            if (col.Header is not string colName || string.IsNullOrEmpty(colName)) continue;

            if (col.Visibility == Visibility.Collapsed)
                _appSettings.HiddenColumns.Add(colName);

            if (col.ActualWidth > 0)
                _appSettings.ColumnWidths[colName] = col.ActualWidth;
        }

        _appSettings.ColumnVisibilityConfigured = true;
        _appSettings.Save();
    }


        // ═══════════════════════════════════════════════════════════════════
        // DRAG & DROP
        // ═══════════════════════════════════════════════════════════════════

        // FIX 10 NOTE: Window_DragEnter is defined below but AllowDrop="False"
        // on the Window means it can never fire. Either:
        //   (a) set AllowDrop="True" on the Window if you intend to support
        //       drag-drop, and wire the event in XAML, OR
        //   (b) delete this handler entirely.
        // The handler is kept here as a reminder only.
        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }


        // ═══════════════════════════════════════════════════════════════════
        // SCREENSHOT
        // ═══════════════════════════════════════════════════════════════════

        private void BtnScreenshot_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new SaveFileDialog
                {
                    Filter      = "PNG Image (*.png)|*.png",
                    FileName    = $"HashDash_Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png",
                    DefaultExt  = ".png"
                };

                if (dlg.ShowDialog() != true) return;

                var renderBitmap = new RenderTargetBitmap(
                    (int)ActualWidth, (int)ActualHeight,
                    96, 96,
                    PixelFormats.Pbgra32);

                renderBitmap.Render(this);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(renderBitmap));

                using var stream = File.Create(dlg.FileName);
                encoder.Save(stream);

                MessageBox.Show($"Screenshot saved to:\n{dlg.FileName}",
                    "Screenshot Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error capturing screenshot: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        // ═══════════════════════════════════════════════════════════════════
        // HELP MENU
        // ═══════════════════════════════════════════════════════════════════

        private void BtnHelp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button { ContextMenu: { } menu })
            {
                menu.PlacementTarget = (System.Windows.Controls.Button)sender;
                menu.IsOpen = true;
            }
        }

        private void MenuAbout_Click(object sender, RoutedEventArgs e)
        {
            new AboutWindow { Owner = this }.ShowDialog();
        }

        private void MenuChangeLog_Click(object sender, RoutedEventArgs e)
        {
            try   { new ChangeLogWindow  { Owner = this }.ShowDialog(); }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening change log: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MenuUserManual_Click(object sender, RoutedEventArgs e)
        {
            try   { new UserManualWindow { Owner = this }.ShowDialog(); }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening user manual: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MenuLogs_Click(object sender, RoutedEventArgs e)
            => ViewModel.OpenLogsWindow();


        // ═══════════════════════════════════════════════════════════════════
        // AUTOMATION TOGGLE
        // ═══════════════════════════════════════════════════════════════════

        private void BtnAutomation_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Primitives.ToggleButton btn) return;

            // Revert the automatic visual toggle; ViewModel drives IsChecked.
            btn.IsChecked = ViewModel.AutomationEnabled;
            ViewModel.ToggleAutomation();
        }


         /// <summary>
    /// Forces DataGrid auto-sized columns to re-measure against ALL current
    /// row content, not just the visible (virtualised) subset.
    /// Technique: pin each column at its current pixel width, then release
    /// back to Auto — this triggers a fresh full-content measurement pass.
    /// Runs at Background priority so it fires after the grid has finished
    /// rendering the new rows.
    /// </summary>
   /// <summary>
    /// Forces SizeToCells columns to re-measure after new cell content
    /// appears (e.g. hash values populated by Run).
    /// Standard WPF technique: pin each column at its current pixel width,
    /// then immediately release back to SizeToCells — this triggers a
    /// fresh full measurement pass against the now-populated cell content.
    /// Runs at Background priority so the grid has finished rendering first.
    /// </summary>
    // ═══════════════════════════════════════════════════════════════════
    // COLUMN VISIBILITY CONTEXT MENU
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Right-clicking any column header opens a checkable menu that toggles
    /// column visibility. StaysOpenOnClick lets the user toggle multiple
    /// columns in a single interaction.
    /// </summary>
    private void ColumnHeader_RightClick(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu();

        menu.Items.Add(new MenuItem { Header = "Visible Columns", IsEnabled = false });
        menu.Items.Add(new Separator());

        foreach (var col in dgFiles.Columns)
        {
            // Skip the checkbox select-all column (no string header) and the # row-number column
            if (col.Header is not string colName || string.IsNullOrEmpty(colName) || colName == "#")
                continue;

            var item = new MenuItem
            {
                Header           = colName,
                IsCheckable      = true,
                IsChecked        = col.Visibility == Visibility.Visible,
                StaysOpenOnClick = true
            };
            var capturedCol = col;
            item.Checked   += (_, _) => { capturedCol.Visibility = Visibility.Visible;   SaveColumnLayout(); };
            item.Unchecked += (_, _) => { capturedCol.Visibility = Visibility.Collapsed; SaveColumnLayout(); };
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
        e.Handled   = true;
    }


    // ═══════════════════════════════════════════════════════════════════
    // DIRECTORY / THUMBNAIL VIEWS
    // ═══════════════════════════════════════════════════════════════════

    private void TvFolders_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FolderNode node)
            ViewModel.SelectedFolder = node;
    }

    private const double ThumbnailTileWidth = 176; // 168 tile + 2 × 4 margin

    private void ThumbnailHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Leave room for the vertical scrollbar and list padding.
        ViewModel.ThumbnailColumns = (int)((e.NewSize.Width - 40) / ThumbnailTileWidth);
    }

    private void ThumbnailTile_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ThumbnailTile tile })
            ViewModel.RequestThumbnail(tile);
    }

    // Recycled row containers get a new tile without raising Loaded again.
    private void ThumbnailTile_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is ThumbnailTile tile && sender is FrameworkElement { IsLoaded: true })
            ViewModel.RequestThumbnail(tile);
    }


    private void RefreshColumnWidths()
    {
        Dispatcher.InvokeAsync(() =>
        {
            foreach (var col in dgFiles.Columns)
            {
                // Leave star columns (Filename) alone.
                if (col.Width.IsStar) continue;

                double current = col.ActualWidth;
                if (current <= 0) continue;

                col.Width = new DataGridLength(current);        // pin to pixels
               col.Width = DataGridLength.Auto;         // release → re-measure
            }
        }, System.Windows.Threading.DispatcherPriority.Background);
    }




    }
}