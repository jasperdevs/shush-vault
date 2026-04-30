using System.Collections.ObjectModel;
using System.Security.Cryptography;
using Microsoft.UI.Xaml.Controls;
using ShushVault.Core;
using ShushVault.Windows;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ShushVault.Windows.Views
{
    public partial class MainPage : Page
    {
        private readonly VaultService vaultService = new();
        private readonly PlatformUnlockService platformUnlockService = new();
        private readonly List<SecretRecord> allRecords = [];
        private string? editingId;

        public ObservableCollection<SecretListItem> Secrets { get; } = [];

        public ObservableCollection<ImportPreviewListItem> ImportPreview { get; } = [];

        public MainPage()
        {
            this.InitializeComponent();
            App.MainWindow.SetTitleBar(TitleBarDragRegion);
            VaultPathText.Text = vaultService.FilePath;
            SetUnlockedUi(false);
            SetSelectionActions(false);
            _ = LoadSecretsAsync();
            _ = RefreshPlatformUnlockStateAsync();
        }

        private async Task<bool> LoadSecretsAsync()
        {
            try
            {
                allRecords.Clear();
                allRecords.AddRange(await vaultService.LoadAsync());
                RefreshWorkspaceChoices();
                ApplyFilters();
                return true;
            }
            catch (InvalidOperationException ex)
            {
                StatusText.Text = ex.Message;
                return false;
            }
            catch (CryptographicException)
            {
                vaultService.Lock();
                allRecords.Clear();
                Secrets.Clear();
                VaultStateText.Text = "Locked";
                SetUnlockedUi(false);
                StatusText.Text = "Could not unlock the vault. Check the passphrase.";
                return false;
            }
        }

        private async void OnUnlockClicked(object sender, RoutedEventArgs e)
        {
            var passphrase = PassphraseBox.Password;
            if (!await UnlockWithPassphraseAsync(passphrase))
            {
                return;
            }

            PassphraseBox.Password = string.Empty;
        }

        private async void OnPlatformUnlockClicked(object sender, RoutedEventArgs e)
        {
            var passphrase = await platformUnlockService.ReadPassphraseWithConsentAsync();
            if (passphrase is null)
            {
                StatusText.Text = "Windows Hello unlock was canceled or no passphrase is saved.";
                return;
            }

            await UnlockWithPassphraseAsync(passphrase);
        }

        private async void OnSavePlatformUnlockClicked(object sender, RoutedEventArgs e)
        {
            var passphrase = !string.IsNullOrWhiteSpace(SettingsPassphraseBox.Password)
                ? SettingsPassphraseBox.Password
                : PassphraseBox.Password;
            if (!await UnlockWithPassphraseAsync(passphrase))
            {
                return;
            }

            PassphraseBox.Password = string.Empty;
            SettingsPassphraseBox.Password = string.Empty;
            if (await platformUnlockService.SavePassphraseWithConsentAsync(passphrase))
            {
                StatusText.Text = "Saved passphrase to Windows Credential Manager for Windows Hello unlock.";
            }

            await RefreshPlatformUnlockStateAsync();
        }

        private async void OnRemovePlatformUnlockClicked(object sender, RoutedEventArgs e)
        {
            platformUnlockService.DeleteSavedPassphrase();
            await RefreshPlatformUnlockStateAsync();
            StatusText.Text = "Removed saved Windows Hello unlock.";
        }

        private void OnSettingsClicked(object sender, RoutedEventArgs e)
            => SettingsOverlay.Visibility = Visibility.Visible;

        private void OnCloseSettingsClicked(object sender, RoutedEventArgs e)
            => SettingsOverlay.Visibility = Visibility.Collapsed;

        private void OnNewSecretClicked(object sender, RoutedEventArgs e)
        {
            if (!vaultService.IsUnlocked)
            {
                StatusText.Text = "Unlock the vault first.";
                return;
            }

            ClearEditor();
            EditorExpander.Header = "Add secret";
            EditorExpander.IsExpanded = true;
            NameBox.Focus(FocusState.Programmatic);
        }

        private void OnSecretSelectionChanged(object sender, SelectionChangedEventArgs e)
            => SetSelectionActions(SecretsList.SelectedItem is SecretListItem);

        private void OnLockClicked(object sender, RoutedEventArgs e)
        {
            vaultService.Lock();
            allRecords.Clear();
            Secrets.Clear();
            ImportPreview.Clear();
            PassphraseBox.Password = string.Empty;
            SettingsPassphraseBox.Password = string.Empty;
            ClearEditor();
            VaultStateText.Text = "Locked";
            SettingsOverlay.Visibility = Visibility.Collapsed;
            SetUnlockedUi(false);
            StatusText.Text = "Locked.";
        }

        private async void OnSaveClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!TryUnlockFromInput())
                {
                    return;
                }

                IReadOnlyList<SecretRecord> records;
                if (editingId is null)
                {
                    records = await vaultService.AddAsync(SelectedWorkspace(), NameBox.Text, ValueBox.Password, SelectedEnvironment(), ProviderBox.Text, NotesBox.Text);
                    StatusText.Text = "Saved to the encrypted vault.";
                }
                else
                {
                    records = await vaultService.UpdateAsync(editingId, SelectedWorkspace(), NameBox.Text, ValueBox.Password, SelectedEnvironment(), ProviderBox.Text, NotesBox.Text);
                    StatusText.Text = "Updated the encrypted vault.";
                }

                allRecords.Clear();
                allRecords.AddRange(records);
                ClearEditor();
                RefreshWorkspaceChoices();
                ApplyFilters();
            }
            catch (ArgumentException ex)
            {
                StatusText.Text = ex.Message;
            }
            catch (CryptographicException)
            {
                StatusText.Text = "Could not unlock the vault. Check the passphrase.";
            }
            catch (InvalidOperationException ex)
            {
                StatusText.Text = ex.Message;
            }
        }

        private void OnClearClicked(object sender, RoutedEventArgs e)
            => ClearEditor();

        private void OnEditClicked(object sender, RoutedEventArgs e)
        {
            if (SecretsList.SelectedItem is not SecretListItem item)
            {
                StatusText.Text = "Select a secret to edit.";
                return;
            }

            var record = allRecords.First(record => record.Id == item.Id);
            editingId = record.Id;
            SelectComboValue(WorkspaceBox, record.Workspace);
            NameBox.Text = record.Name;
            ValueBox.Password = record.Value;
            ProviderBox.Text = record.Provider;
            NotesBox.Text = record.Notes;
            EnvironmentBox.SelectedIndex = record.Environment switch
            {
                "Staging" => 1,
                "Prod" => 2,
                _ => 0
            };
            SaveButton.Content = "Update";
            EditorExpander.Header = "Edit secret";
            EditorExpander.IsExpanded = true;
            StatusText.Text = "Editing selected secret.";
        }

        private async void OnDeleteClicked(object sender, RoutedEventArgs e)
        {
            if (!TryUnlockFromInput())
            {
                return;
            }

            if (SecretsList.SelectedItem is not SecretListItem item)
            {
                StatusText.Text = "Select a secret to delete.";
                return;
            }

            var records = await vaultService.DeleteAsync(item.Id);
            allRecords.Clear();
            allRecords.AddRange(records);
            ClearEditor();
            RefreshWorkspaceChoices();
            ApplyFilters();
            StatusText.Text = "Deleted selected secret.";
        }

        private void OnCopyClicked(object sender, RoutedEventArgs e)
        {
            if (!TryUnlockFromInput())
            {
                return;
            }

            if (SecretsList.SelectedItem is not SecretListItem item)
            {
                StatusText.Text = "Select a secret to copy.";
                return;
            }

            var record = allRecords.First(record => record.Id == item.Id);
            var package = new DataPackage();
            package.SetText(record.Value);
            Clipboard.SetContent(package);
            var clearSeconds = SelectedClipboardClearSeconds();
            _ = ClearClipboardLaterAsync(record.Value, clearSeconds);
            StatusText.Text = ClipboardStatus($"Copied {record.Name}.", clearSeconds);
        }

        private async void OnChooseEnvClicked(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };
            picker.FileTypeFilter.Add(".env");
            picker.FileTypeFilter.Add(".txt");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));

            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            EnvImportBox.Text = await File.ReadAllTextAsync(file.Path);
            RefreshImportPreview();
        }

        private async void OnImportClicked(object sender, RoutedEventArgs e)
        {
            if (!TryUnlockFromInput())
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(EnvImportBox.Text))
            {
                StatusText.Text = "Paste or choose a .env file first.";
                return;
            }

            var records = await vaultService.ImportEnvAsync(
                EnvImportBox.Text,
                SelectedWorkspace(),
                SelectedEnvironment(),
                ProviderBox.Text,
                SelectedConflictMode());

            allRecords.Clear();
            allRecords.AddRange(records);
            EnvImportBox.Text = string.Empty;
            ImportPreview.Clear();
            RefreshWorkspaceChoices();
            ApplyFilters();
            StatusText.Text = "Imported .env entries.";
        }

        private void OnExportClicked(object sender, RoutedEventArgs e)
        {
            if (!TryUnlockFromInput())
            {
                return;
            }

            var visibleIds = Secrets.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            var visibleRecords = allRecords.Where(record => visibleIds.Contains(record.Id)).ToList();
            var exported = vaultService.ExportEnv(visibleRecords);
            var package = new DataPackage();
            package.SetText(exported);
            Clipboard.SetContent(package);
            var clearSeconds = SelectedClipboardClearSeconds();
            _ = ClearClipboardLaterAsync(exported, clearSeconds);
            StatusText.Text = ClipboardStatus("Copied visible secrets as .env.", clearSeconds);
        }

        private void OnEnvImportTextChanged(object sender, TextChangedEventArgs e)
            => RefreshImportPreview();

        private void OnFilterChanged(object sender, object e)
            => ApplyFilters();

        private void RefreshImportPreview()
        {
            ImportPreview.Clear();

            foreach (var item in vaultService.PreviewEnv(EnvImportBox.Text, SelectedWorkspace(), SelectedEnvironment()))
            {
                ImportPreview.Add(ImportPreviewListItem.From(item));
            }
        }

        private void ApplyFilters()
        {
            var search = SearchBox?.Text.Trim() ?? string.Empty;
            var workspace = SelectedWorkspaceFilter();
            var environment = SelectedFilterEnvironment();

            var filtered = allRecords.Where(record =>
                (search.Length == 0 ||
                    record.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    record.Provider.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    record.Notes.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
                (workspace.Length == 0 || record.Workspace.Contains(workspace, StringComparison.OrdinalIgnoreCase)) &&
                (environment == "All" || record.Environment.Equals(environment, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(record => record.UpdatedAt)
                .ToList();

            Secrets.Clear();
            foreach (var record in filtered)
            {
                Secrets.Add(SecretListItem.From(record));
            }
            SetSelectionActions(false);

            if (StatusText is not null)
            {
                StatusText.Text = !vaultService.IsUnlocked
                    ? "Locked. Enter a passphrase to unlock or create your encrypted vault."
                    : filtered.Count == 0
                    ? "No secrets yet. Use New secret to add one."
                    : $"{filtered.Count} secret{(filtered.Count == 1 ? string.Empty : "s")} shown.";
            }
        }

        private void ClearEditor()
        {
            editingId = null;
            NameBox.Text = string.Empty;
            ValueBox.Password = string.Empty;
            ProviderBox.Text = string.Empty;
            NotesBox.Text = string.Empty;
            SaveButton.Content = "Save";
            EditorExpander.Header = "Add secret";
        }

        private bool TryUnlockFromInput()
        {
            if (vaultService.IsUnlocked)
            {
                return true;
            }

            try
            {
                vaultService.Unlock(PassphraseBox.Password);
                PassphraseBox.Password = string.Empty;
                return true;
            }
            catch (ArgumentException ex)
            {
                StatusText.Text = ex.Message;
                return false;
            }
        }

        private async Task<bool> UnlockWithPassphraseAsync(string passphrase)
        {
            try
            {
                vaultService.Unlock(passphrase);
            }
            catch (ArgumentException ex)
            {
                StatusText.Text = ex.Message;
                return false;
            }

            if (!await LoadSecretsAsync())
            {
                return false;
            }

            StatusText.Text = $"Unlocked {Path.GetFileName(vaultService.FilePath)}.";
            VaultStateText.Text = "Unlocked";
            SetUnlockedUi(true);
            return true;
        }

        private async Task RefreshPlatformUnlockStateAsync()
        {
            var state = await platformUnlockService.GetStateAsync();
            PlatformUnlockStatus.Text = state.HasSavedPassphrase
                ? $"{state.Message} Saved unlock is configured."
                : state.Message;
            PlatformUnlockButton.IsEnabled = state.IsAvailable && state.HasSavedPassphrase;
            SavePlatformUnlockButton.IsEnabled = state.IsAvailable;
            RemovePlatformUnlockButton.IsEnabled = state.HasSavedPassphrase;
        }

        private void SetUnlockedUi(bool isUnlocked)
        {
            UnlockPane.Visibility = isUnlocked ? Visibility.Collapsed : Visibility.Visible;
            SearchBox.IsEnabled = isUnlocked;
            WorkspaceFilterBox.IsEnabled = isUnlocked;
            EnvironmentFilterBox.IsEnabled = isUnlocked;
            SecretsList.IsEnabled = isUnlocked;
            NewSecretButton.IsEnabled = isUnlocked;
            EditorExpander.IsEnabled = isUnlocked;
            ExportButton.IsEnabled = isUnlocked && Secrets.Count > 0;
            if (!isUnlocked)
            {
                EditorExpander.IsExpanded = false;
                SetSelectionActions(false);
            }
        }

        private void SetSelectionActions(bool hasSelection)
        {
            EditButton.IsEnabled = hasSelection && vaultService.IsUnlocked;
            CopyButton.IsEnabled = hasSelection && vaultService.IsUnlocked;
            DeleteButton.IsEnabled = hasSelection && vaultService.IsUnlocked;
            ExportButton.IsEnabled = vaultService.IsUnlocked && Secrets.Count > 0;
        }

        private void RefreshWorkspaceChoices()
        {
            var values = allRecords
                .Select(record => record.Workspace)
                .Append("Default")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();

            ReplaceComboItems(WorkspaceBox, values, SelectedWorkspace());
            ReplaceComboItems(WorkspaceFilterBox, ["All", .. values], SelectedWorkspaceFilter().Length == 0 ? "All" : SelectedWorkspaceFilter());
        }

        private static void ReplaceComboItems(ComboBox comboBox, IReadOnlyList<string> values, string selected)
        {
            comboBox.Items.Clear();
            foreach (var value in values)
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
            if (current != expectedText)
            {
                return;
            }

            Clipboard.SetContent(new DataPackage());
        }

        private int SelectedClipboardClearSeconds()
        {
            if (ClipboardClearBox?.SelectedItem is ComboBoxItem item &&
                int.TryParse(item.Tag?.ToString(), out var seconds))
            {
                return seconds;
            }

            return 30;
        }

        private static string ClipboardStatus(string prefix, int clearSeconds)
            => clearSeconds > 0
                ? $"{prefix} Clipboard clears in {clearSeconds}s."
                : $"{prefix} Clipboard auto-clear is off.";

        private string SelectedWorkspace()
            => (WorkspaceBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Default";

        private string SelectedWorkspaceFilter()
        {
            var value = (WorkspaceFilterBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";
            return value == "All" ? string.Empty : value;
        }

        private string SelectedEnvironment()
            => (EnvironmentBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Dev";

        private string SelectedFilterEnvironment()
            => (EnvironmentFilterBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";

        private ImportConflictMode SelectedConflictMode()
            => ((ConflictBox.SelectedItem as ComboBoxItem)?.Content?.ToString()) switch
            {
                "Overwrite" => ImportConflictMode.Overwrite,
                "Rename" => ImportConflictMode.Rename,
                _ => ImportConflictMode.Skip
            };
    }

    public sealed class SecretListItem
    {
        public string Id { get; set; } = string.Empty;

        public string Workspace { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Environment { get; set; } = string.Empty;

        public string Provider { get; set; } = string.Empty;

        public string MaskedValue { get; set; } = string.Empty;

        public static SecretListItem From(SecretRecord record)
            => new()
            {
                Id = record.Id,
                Workspace = record.Workspace,
                Name = record.Name,
                Environment = record.Environment,
                Provider = string.IsNullOrWhiteSpace(record.Provider) ? "-" : record.Provider,
                MaskedValue = Mask(record.Value)
            };

        private static string Mask(string value)
        {
            if (value.Length <= 4)
            {
                return new string('•', Math.Max(value.Length, 1));
            }

            return $"{new string('•', Math.Min(value.Length, 12))}{value[^4..]}";
        }
    }

    public sealed class ImportPreviewListItem
    {
        public string Line { get; set; } = string.Empty;

        public string Key { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public static ImportPreviewListItem From(ImportPreviewItem item)
            => new()
            {
                Line = item.Line.ToString(),
                Key = string.IsNullOrWhiteSpace(item.Key) ? "-" : item.Key,
                Status = item.Status.ToString()
            };
    }
}
