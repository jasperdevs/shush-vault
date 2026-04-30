using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml;
using ShushVault.Core;
using ShushVault.Windows;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace ShushVault.Windows.Views
{
    public partial class MainPage : Page, INotifyPropertyChanged
    {
        private static readonly HttpClient FaviconClient = new();
        private readonly VaultService vaultService = new();
        private readonly PlatformUnlockService platformUnlockService = new();
        private readonly AppSettingsStore settingsStore = new();
        private readonly AppSettings settings;
        private readonly List<SecretRecord> allRecords = [];
        private readonly List<string> workspaces = ["Default"];
        private SettingsWindow? settingsWindow;
        private string? editingId;
        private string environmentFilter = "All";
        private string secretEnvironment = "Dev";
        private string importEnvironment = "Dev";
        private string devicePassphrase = string.Empty;
        private bool refreshingWorkspaces;
        private bool initialized;
        private string currentIconBase64 = string.Empty;
        private bool iconIsManual;
        private CancellationTokenSource? faviconCts;

        public ObservableCollection<SecretListItem> Secrets { get; } = [];
        public ObservableCollection<ImportPreviewListItem> ImportPreview { get; } = [];
        public Visibility SecretsListVisibility => Secrets.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        public event PropertyChangedEventHandler? PropertyChanged;

        public MainPage()
        {
            this.InitializeComponent();
            App.MainWindow.SetTitleBar(TitleBar);
            settings = settingsStore.Load();
            devicePassphrase = platformUnlockService.GetOrCreateDevicePassphrase();
            vaultService.Unlock(devicePassphrase);
            initialized = true;
            Loaded += (_, _) => CursorHelper.ApplyToTree(this);
            _ = LoadSecretsAsync();
        }

        private void OnRowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is UIElement element)
            {
                CursorHelper.ApplyHand(element);
                CursorHelper.ApplyToTree(element);
            }
        }

        private async Task LoadSecretsAsync()
        {
            try
            {
                allRecords.Clear();
                allRecords.AddRange(await vaultService.LoadAsync());
                RefreshWorkspaceChoices();
                ApplyFilters();
            }
            catch (CryptographicException)
            {
                // Vault unreadable. Silent.
            }
        }

        private void OnSettingsClicked(object sender, RoutedEventArgs e)
        {
            if (settingsWindow is null)
            {
                settingsWindow = new SettingsWindow(
                    vaultService.FilePath,
                    settings,
                    settingsStore);
                settingsWindow.Closed += (_, _) => settingsWindow = null;
            }

            settingsWindow.Activate();
        }

        private void OnNewSecretClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                editingId = null;
                ClearSecretForm();
                SecretDialogTitle.Text = "New secret";
                SaveButton.Content = "Save";
                ShowDialog(SecretDialog);
                DispatcherQueue.TryEnqueue(() => NameBox.Focus(FocusState.Programmatic));
            }
            catch (Exception ex)
            {
                App.LogCrash(ex);
            }
        }

        private void OnImportEnvClicked(object sender, RoutedEventArgs e)
        {
            ImportPreview.Clear();
            EnvImportBox.Text = string.Empty;
            SelectComboValue(ImportWorkspaceBox, "Default");
            SelectComboValue(ImportEnvironmentBox, "Dev");
            importEnvironment = "Dev";
            ShowDialog(ImportDialog);
            EnvImportBox.Focus(FocusState.Programmatic);
        }

        private void OnAddWorkspaceClicked(object sender, RoutedEventArgs e)
        {
            NewWorkspaceBox.Text = string.Empty;
            ShowDialog(WorkspaceDialog);
            NewWorkspaceBox.Focus(FocusState.Programmatic);
        }

        private void OnWorkspaceSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (refreshingWorkspaces || SelectedWorkspace(WorkspaceBox) != "Add workspace")
            {
                return;
            }

            SelectComboValue(WorkspaceBox, "Default");
            OnAddWorkspaceClicked(sender, new RoutedEventArgs());
        }

        private void OnCancelWorkspaceClicked(object sender, RoutedEventArgs e)
            => ShowDialog(SecretDialog);

        private void OnCreateWorkspaceClicked(object sender, RoutedEventArgs e)
        {
            var workspace = NewWorkspaceBox.Text.Trim();
            if (workspace.Length == 0)
            {
                return;
            }

            if (!workspaces.Contains(workspace, StringComparer.OrdinalIgnoreCase))
            {
                workspaces.Add(workspace);
                workspaces.Sort(StringComparer.OrdinalIgnoreCase);
            }

            RefreshWorkspaceCombos(workspace);
            ShowDialog(SecretDialog);
        }

        private async void OnSaveClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                var website = WebsiteBox.Text?.Trim() ?? string.Empty;
                IReadOnlyList<SecretRecord> records;
                if (editingId is null)
                {
                    records = await vaultService.AddAsync(SelectedWorkspace(WorkspaceBox), NameBox.Text, ValueBox.Password, secretEnvironment, ProviderBox.Text, NotesBox.Text, website, currentIconBase64);
                }
                else
                {
                    records = await vaultService.UpdateAsync(editingId, SelectedWorkspace(WorkspaceBox), NameBox.Text, ValueBox.Password, secretEnvironment, ProviderBox.Text, NotesBox.Text, website, currentIconBase64);
                }

                allRecords.Clear();
                allRecords.AddRange(records);
                HideDialogs();
                ClearSecretForm();
                RefreshWorkspaceChoices();
                ApplyFilters();
            }
            catch (ArgumentException)
            {
                // Invalid input.
            }
            catch (CryptographicException)
            {
                // Encryption error.
            }
        }

        private void OnRowCopyClicked(object sender, RoutedEventArgs e)
            => CopyRecord(FindRecord(sender), sender as FrameworkElement);

        private void OnRowTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            FrameworkElement? copyButton = null;
            if (e.OriginalSource is DependencyObject source)
            {
                var current = source;
                while (current is not null)
                {
                    if (current is Button)
                    {
                        return;
                    }
                    current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
                }
            }

            if (sender is FrameworkElement row)
            {
                copyButton = FindCopyButton(row);
            }

            CopyRecord(FindRecord(sender), copyButton);
        }

        private static FrameworkElement? FindCopyButton(DependencyObject root)
        {
            var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
                if (child is Button { Name: "" } button)
                {
                    if (ToolTipService.GetToolTip(button) is string tip && tip == "Copy value")
                    {
                        return button;
                    }
                }

                if (FindCopyButton(child) is { } found)
                {
                    return found;
                }
            }

            return null;
        }

        private void CopyRecord(SecretRecord? record, FrameworkElement? anchor)
        {
            if (record is null)
            {
                return;
            }

            var package = new DataPackage();
            package.SetText(record.Value);
            Clipboard.SetContent(package);
            if (anchor is not null)
            {
                CopyFlyout.Show(anchor);
            }
            _ = ClearClipboardLaterAsync(record.Value, settings.ClipboardClearSeconds);
        }

        private void OnRowPointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Grid grid && Application.Current.Resources.TryGetValue("RowHoverBrush", out var brush) && brush is Brush hover)
            {
                grid.Background = hover;
            }
        }

        private void OnRowPointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Grid grid)
            {
                grid.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }

        private void OnRowEditClicked(object sender, RoutedEventArgs e)
        {
            if (FindRecord(sender) is not { } record)
            {
                return;
            }

            editingId = record.Id;
            SelectComboValue(WorkspaceBox, record.Workspace);
            NameBox.Text = record.Name;
            ValueBox.Password = record.Value;
            ProviderBox.Text = record.Provider;
            NotesBox.Text = record.Notes;
            WebsiteBox.Text = record.Website;
            ResetIconPreview();
            if (!string.IsNullOrEmpty(record.IconBase64))
            {
                _ = LoadIconFromBase64Async(record.IconBase64, manual: true);
            }
            SetSecretEnvironment(record.Environment);
            SecretDialogTitle.Text = "Edit secret";
            SaveButton.Content = "Save";
            ShowDialog(SecretDialog);
        }

        private async void OnRowDeleteClicked(object sender, RoutedEventArgs e)
        {
            if (FindRecord(sender) is not { } record)
            {
                return;
            }

            var confirm = new ContentDialog
            {
                Title = "Delete secret?",
                Content = $"Permanently remove {record.Name}? This cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            allRecords.Clear();
            allRecords.AddRange(await vaultService.DeleteAsync(record.Id));
            ApplyFilters();
        }

        private async void OnImportClicked(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(EnvImportBox.Text))
            {
                return;
            }

            allRecords.Clear();
            allRecords.AddRange(await vaultService.ImportEnvAsync(
                EnvImportBox.Text,
                SelectedWorkspace(ImportWorkspaceBox),
                importEnvironment,
                ImportProviderBox.Text,
                SelectedConflictMode()));
            HideDialogs();
            ImportPreview.Clear();
            RefreshWorkspaceChoices();
            ApplyFilters();
        }

        private void OnCloseDialogClicked(object sender, RoutedEventArgs e)
        {
            HideDialogs();
            ClearSecretForm();
        }

        private void OnFilterChanged(object sender, object e)
            => ApplyFilters();

        private void OnEnvFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!initialized)
            {
                return;
            }

            if (EnvFilterBox.SelectedItem is ComboBoxItem item && item.Tag is string env)
            {
                environmentFilter = env;
                ApplyFilters();
            }
        }

        private void OnSecretEnvironmentChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!initialized)
            {
                return;
            }

            secretEnvironment = (EnvironmentBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Dev";
        }

        private void OnImportEnvironmentChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!initialized)
            {
                return;
            }

            importEnvironment = (ImportEnvironmentBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Dev";
            RefreshImportPreview();
        }

        private void OnEnvImportTextChanged(object sender, TextChangedEventArgs e)
            => RefreshImportPreview();

        private void RefreshImportPreview()
        {
            ImportPreview.Clear();
            foreach (var item in vaultService.PreviewEnv(EnvImportBox.Text, SelectedWorkspace(ImportWorkspaceBox), importEnvironment))
            {
                ImportPreview.Add(ImportPreviewListItem.From(item));
            }
        }

        private void OnIconDragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }

        private async void OnIconDrop(object sender, DragEventArgs e)
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }

            var items = await e.DataView.GetStorageItemsAsync();
            if (items.FirstOrDefault() is StorageFile file)
            {
                await SetIconFromFileAsync(file);
            }
        }

        private async void OnIconBrowseClicked(object sender, RoutedEventArgs e)
        {
            var picker = new global::Windows.Storage.Pickers.FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".svg");
            picker.FileTypeFilter.Add(".ico");
            var file = await picker.PickSingleFileAsync();
            if (file is not null)
            {
                await SetIconFromFileAsync(file);
            }
        }

        private async Task SetIconFromFileAsync(StorageFile file)
        {
            try
            {
                var buffer = await FileIO.ReadBufferAsync(file);
                using var reader = DataReader.FromBuffer(buffer);
                var bytes = new byte[buffer.Length];
                reader.ReadBytes(bytes);
                await ApplyIconBytesAsync(bytes, manual: true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Icon load failed: {ex.Message}");
            }
        }

        private async Task ApplyIconBytesAsync(byte[] bytes, bool manual)
        {
            try
            {
                var raStream = new InMemoryRandomAccessStream();
                using (var writer = new DataWriter(raStream))
                {
                    writer.WriteBytes(bytes);
                    await writer.StoreAsync();
                    await writer.FlushAsync();
                    writer.DetachStream();
                }
                raStream.Seek(0);
                var bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(raStream);
                IconPreview.Source = bitmap;
                IconPreview.Visibility = Visibility.Visible;
                IconHint.Visibility = Visibility.Collapsed;
                currentIconBase64 = Convert.ToBase64String(bytes);
                if (manual)
                {
                    iconIsManual = true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Icon decode failed: {ex.Message}");
            }
        }

        private async Task LoadIconFromBase64Async(string base64, bool manual)
        {
            try
            {
                var bytes = Convert.FromBase64String(base64);
                await ApplyIconBytesAsync(bytes, manual);
            }
            catch (FormatException)
            {
                // Stored value isn't valid base64.
            }
        }

        private void ResetIconPreview()
        {
            IconPreview.Source = null;
            IconPreview.Visibility = Visibility.Collapsed;
            IconHint.Visibility = Visibility.Visible;
            currentIconBase64 = string.Empty;
            iconIsManual = false;
            faviconCts?.Cancel();
            faviconCts = null;
        }

        private void OnWebsiteChanged(object sender, TextChangedEventArgs e)
        {
            if (iconIsManual)
            {
                return;
            }

            var input = WebsiteBox.Text?.Trim() ?? string.Empty;
            faviconCts?.Cancel();
            if (input.Length == 0 || ExtractDomain(input) is not { } domain)
            {
                if (!iconIsManual)
                {
                    IconPreview.Source = null;
                    IconPreview.Visibility = Visibility.Collapsed;
                    IconHint.Visibility = Visibility.Visible;
                    currentIconBase64 = string.Empty;
                }
                return;
            }

            var cts = new CancellationTokenSource();
            faviconCts = cts;
            _ = FetchFaviconAsync(domain, cts.Token);
        }

        private async Task FetchFaviconAsync(string domain, CancellationToken token)
        {
            try
            {
                await Task.Delay(350, token);
                var url = $"https://www.google.com/s2/favicons?domain={Uri.EscapeDataString(domain)}&sz=64";
                var bytes = await FaviconClient.GetByteArrayAsync(url, token);
                if (token.IsCancellationRequested || iconIsManual || bytes.Length == 0)
                {
                    return;
                }

                await ApplyIconBytesAsync(bytes, manual: false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Favicon fetch failed: {ex.Message}");
            }
        }

        private static string? ExtractDomain(string input)
        {
            var candidate = input.Contains("://", StringComparison.Ordinal) ? input : $"https://{input}";
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            {
                return null;
            }

            var host = uri.Host;
            return string.IsNullOrWhiteSpace(host) ? null : host;
        }

        private void ApplyFilters()
        {
            var search = SearchBox?.Text.Trim() ?? string.Empty;
            var filtered = allRecords.Where(record =>
                (search.Length == 0 ||
                    record.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    record.Provider.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    record.Notes.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
                (environmentFilter == "All" || record.Environment.Equals(environmentFilter, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(record => record.UpdatedAt)
                .ToList();

            Secrets.Clear();
            foreach (var record in filtered)
            {
                Secrets.Add(SecretListItem.From(record));
            }

            EmptyState.Visibility = Secrets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyTitle.Text = allRecords.Count == 0
                ? "No secrets"
                : search.Length > 0
                    ? "No matches"
                    : "Nothing here";
            OnPropertyChanged(nameof(SecretsListVisibility));
        }

        private void ShowDialog(FrameworkElement dialog)
        {
            DialogLayer.Visibility = Visibility.Visible;
            SecretDialog.Visibility = Visibility.Collapsed;
            ImportDialog.Visibility = Visibility.Collapsed;
            WorkspaceDialog.Visibility = Visibility.Collapsed;
            dialog.Visibility = Visibility.Visible;
        }

        private void HideDialogs()
        {
            DialogLayer.Visibility = Visibility.Collapsed;
            SecretDialog.Visibility = Visibility.Collapsed;
            ImportDialog.Visibility = Visibility.Collapsed;
            WorkspaceDialog.Visibility = Visibility.Collapsed;
        }

        private void ClearSecretForm()
        {
            editingId = null;
            SelectComboValue(WorkspaceBox, "Default");
            NameBox.Text = string.Empty;
            ValueBox.Password = string.Empty;
            ProviderBox.Text = string.Empty;
            NotesBox.Text = string.Empty;
            WebsiteBox.Text = string.Empty;
            ResetIconPreview();
            SetSecretEnvironment("Dev");
        }

        private void SetSecretEnvironment(string environment)
        {
            secretEnvironment = environment;
            SelectComboValue(EnvironmentBox, environment);
        }

        private SecretRecord? FindRecord(object sender)
        {
            if (sender is FrameworkElement { Tag: string id })
            {
                return allRecords.FirstOrDefault(record => record.Id == id);
            }

            return null;
        }

        private void RefreshWorkspaceChoices()
        {
            foreach (var workspace in allRecords.Select(record => record.Workspace))
            {
                if (!workspaces.Contains(workspace, StringComparer.OrdinalIgnoreCase))
                {
                    workspaces.Add(workspace);
                }
            }

            workspaces.Sort(StringComparer.OrdinalIgnoreCase);
            RefreshWorkspaceCombos(SelectedWorkspace(WorkspaceBox));
        }

        private void RefreshWorkspaceCombos(string selected)
        {
            refreshingWorkspaces = true;
            try
            {
                ReplaceComboItems(WorkspaceBox, [.. workspaces, "Add workspace"], selected);
                ReplaceComboItems(ImportWorkspaceBox, workspaces, selected);
            }
            finally
            {
                refreshingWorkspaces = false;
            }
        }

        private static void ReplaceComboItems(ComboBox comboBox, IReadOnlyList<string> values, string selected)
        {
            comboBox.Items.Clear();
            foreach (var value in values.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                comboBox.Items.Add(new ComboBoxItem { Content = value });
            }

            SelectComboValue(comboBox, selected);
            if (comboBox.SelectedIndex < 0 && comboBox.Items.Count > 0)
            {
                comboBox.SelectedIndex = 0;
            }
        }

        private static void SelectComboValue(ComboBox comboBox, string value)
        {
            for (var index = 0; index < comboBox.Items.Count; index++)
            {
                if (comboBox.Items[index] is ComboBoxItem item &&
                    string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedIndex = index;
                    return;
                }
            }
        }

        private static string SelectedWorkspace(ComboBox comboBox)
        {
            return (comboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Default";
        }

        private ImportConflictMode SelectedConflictMode()
            => ((ConflictBox.SelectedItem as ComboBoxItem)?.Content?.ToString()) switch
            {
                "Overwrite" => ImportConflictMode.Overwrite,
                "Rename" => ImportConflictMode.Rename,
                _ => ImportConflictMode.Skip
            };

        private static async Task ClearClipboardLaterAsync(string expectedText, int clearSeconds)
        {
            if (clearSeconds <= 0)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(clearSeconds));
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text))
            {
                return;
            }

            var current = await content.GetTextAsync();
            if (current == expectedText)
            {
                Clipboard.SetContent(new DataPackage());
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public sealed class SecretListItem
    {
        public string Id { get; set; } = string.Empty;
        public string Workspace { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Environment { get; set; } = string.Empty;
        public string Provider { get; set; } = string.Empty;
        public string MaskedValue { get; set; } = string.Empty;
        public string IconBase64 { get; set; } = string.Empty;
        public string Initial { get; set; } = string.Empty;
        public Microsoft.UI.Xaml.Media.Imaging.BitmapImage? IconSource { get; set; }
        public Visibility IconVisibility { get; set; } = Visibility.Collapsed;
        public Visibility InitialVisibility { get; set; } = Visibility.Visible;

        public static SecretListItem From(SecretRecord record)
        {
            var item = new SecretListItem
            {
                Id = record.Id,
                Workspace = record.Workspace,
                Name = record.Name,
                Environment = record.Environment,
                Provider = record.Provider?.Trim() ?? string.Empty,
                MaskedValue = Mask(record.Value),
                IconBase64 = record.IconBase64,
                Initial = string.IsNullOrEmpty(record.Name) ? "?" : record.Name[..1].ToUpperInvariant()
            };

            if (!string.IsNullOrEmpty(record.IconBase64))
            {
                try
                {
                    var bytes = Convert.FromBase64String(record.IconBase64);
                    var ms = new MemoryStream(bytes);
                    var stream = ms.AsRandomAccessStream();
                    var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                    var op = bmp.SetSourceAsync(stream);
                    op.Completed = (_, _) =>
                    {
                        stream.Dispose();
                        ms.Dispose();
                    };
                    item.IconSource = bmp;
                    item.IconVisibility = Visibility.Visible;
                    item.InitialVisibility = Visibility.Collapsed;
                }
                catch
                {
                }
            }

            return item;
        }

        private static string Mask(string value)
            => string.IsNullOrEmpty(value) ? "•" : new string('•', 10);
    }

    public sealed class ImportPreviewListItem
    {
        private static readonly SolidColorBrush ReadyBrush = new(Microsoft.UI.Colors.LightGray);
        private static readonly SolidColorBrush ConflictBrush = new(Microsoft.UI.Colors.Goldenrod);
        private static readonly SolidColorBrush IgnoredBrush = new(Microsoft.UI.Colors.DimGray);
        private static readonly SolidColorBrush InvalidBrush = new(Microsoft.UI.Colors.IndianRed);

        public string Line { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public string MaskedValue { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public Brush StatusForeground { get; set; } = IgnoredBrush;

        public static ImportPreviewListItem From(ImportPreviewItem item)
            => new()
            {
                Line = item.Line.ToString(),
                Key = string.IsNullOrWhiteSpace(item.Key) ? "-" : item.Key,
                MaskedValue = string.IsNullOrWhiteSpace(item.Value) ? "-" : SecretListItem.From(SecretRecord.Create("Default", item.Key, item.Value, "Dev", "", "")).MaskedValue,
                Status = item.Status.ToString(),
                StatusForeground = item.Status switch
                {
                    ImportPreviewStatus.Ready => ReadyBrush,
                    ImportPreviewStatus.Conflict => ConflictBrush,
                    ImportPreviewStatus.Invalid => InvalidBrush,
                    _ => IgnoredBrush
                }
            };
    }
}
