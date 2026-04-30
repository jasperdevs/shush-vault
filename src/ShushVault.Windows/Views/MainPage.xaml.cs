using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using ShushVault.Core;
using ShushVault.Windows;
using Windows.ApplicationModel.DataTransfer;

namespace ShushVault.Windows.Views
{
    public partial class MainPage : Page, INotifyPropertyChanged
    {
        private readonly VaultService vaultService = new();
        private readonly PlatformUnlockService platformUnlockService = new();
        private readonly List<SecretRecord> allRecords = [];
        private readonly List<string> workspaces = ["Default"];
        private SettingsWindow? settingsWindow;
        private string? editingId;
        private string environmentFilter = "All";
        private string secretEnvironment = "Dev";
        private string importEnvironment = "Dev";
        private int clipboardClearSeconds = 30;
        private bool refreshingWorkspaces;

        public ObservableCollection<SecretListItem> Secrets { get; } = [];
        public ObservableCollection<ImportPreviewListItem> ImportPreview { get; } = [];
        public Visibility SecretsListVisibility => Secrets.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        public event PropertyChangedEventHandler? PropertyChanged;

        public MainPage()
        {
            this.InitializeComponent();
            App.MainWindow.SetTitleBar(TitleBar);
            vaultService.Unlock(platformUnlockService.GetOrCreateDevicePassphrase());
            _ = LoadSecretsAsync();
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
                StatusText.Text = "Could not open the encrypted vault. If this was created with an old passphrase, reset or migrate it from Settings.";
            }
        }

        private void OnSettingsClicked(object sender, RoutedEventArgs e)
        {
            if (settingsWindow is null)
            {
                settingsWindow = new SettingsWindow(
                    vaultService.FilePath,
                    clipboardClearSeconds,
                    seconds => clipboardClearSeconds = seconds);
                settingsWindow.Closed += (_, _) => settingsWindow = null;
            }

            settingsWindow.Activate();
        }

        private void OnNewSecretClicked(object sender, RoutedEventArgs e)
        {
            editingId = null;
            ClearSecretForm();
            SecretDialogTitle.Text = "New Secret";
            SaveButton.Content = "Save";
            ShowDialog(SecretDialog);
            NameBox.Focus(FocusState.Programmatic);
        }

        private void OnImportEnvClicked(object sender, RoutedEventArgs e)
        {
            ImportPreview.Clear();
            EnvImportBox.Text = string.Empty;
            SelectComboValue(ImportWorkspaceBox, "Default");
            SetImportEnvironment("Dev");
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
            if (refreshingWorkspaces || SelectedWorkspace(WorkspaceBox) != "Add workspace...")
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
                StatusText.Text = "Workspace name is required.";
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
                IReadOnlyList<SecretRecord> records;
                if (editingId is null)
                {
                    records = await vaultService.AddAsync(SelectedWorkspace(WorkspaceBox), NameBox.Text, ValueBox.Password, secretEnvironment, ProviderBox.Text, NotesBox.Text);
                    StatusText.Text = $"Saved {NameBox.Text.Trim()}.";
                }
                else
                {
                    records = await vaultService.UpdateAsync(editingId, SelectedWorkspace(WorkspaceBox), NameBox.Text, ValueBox.Password, secretEnvironment, ProviderBox.Text, NotesBox.Text);
                    StatusText.Text = $"Updated {NameBox.Text.Trim()}.";
                }

                allRecords.Clear();
                allRecords.AddRange(records);
                HideDialogs();
                ClearSecretForm();
                RefreshWorkspaceChoices();
                ApplyFilters();
            }
            catch (ArgumentException ex)
            {
                StatusText.Text = ex.Message;
            }
            catch (CryptographicException)
            {
                StatusText.Text = "Could not save to the encrypted vault.";
            }
        }

        private void OnEditClicked(object sender, RoutedEventArgs e)
        {
            if (SecretsList.SelectedItem is not SecretListItem item)
            {
                return;
            }

            var record = allRecords.First(record => record.Id == item.Id);
            editingId = record.Id;
            SelectComboValue(WorkspaceBox, record.Workspace);
            NameBox.Text = record.Name;
            ValueBox.Password = record.Value;
            ProviderBox.Text = record.Provider;
            NotesBox.Text = record.Notes;
            SetSecretEnvironment(record.Environment);
            SecretDialogTitle.Text = "Edit Secret";
            SaveButton.Content = "Save";
            ShowDialog(SecretDialog);
        }

        private async void OnDeleteClicked(object sender, RoutedEventArgs e)
        {
            if (SecretsList.SelectedItem is not SecretListItem item)
            {
                return;
            }

            allRecords.Clear();
            allRecords.AddRange(await vaultService.DeleteAsync(item.Id));
            ApplyFilters();
            StatusText.Text = "Deleted selected secret.";
        }

        private void OnCopyClicked(object sender, RoutedEventArgs e)
        {
            if (SecretsList.SelectedItem is not SecretListItem item)
            {
                return;
            }

            var record = allRecords.First(record => record.Id == item.Id);
            var package = new DataPackage();
            package.SetText(record.Value);
            Clipboard.SetContent(package);
            _ = ClearClipboardLaterAsync(record.Value, clipboardClearSeconds);
            StatusText.Text = ClipboardStatus($"Copied {record.Name}.", clipboardClearSeconds);
        }

        private void OnExportClicked(object sender, RoutedEventArgs e)
        {
            var visibleIds = Secrets.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            var exported = vaultService.ExportEnv(allRecords.Where(record => visibleIds.Contains(record.Id)));
            var package = new DataPackage();
            package.SetText(exported);
            Clipboard.SetContent(package);
            _ = ClearClipboardLaterAsync(exported, clipboardClearSeconds);
            StatusText.Text = ClipboardStatus("Copied visible secrets as .env.", clipboardClearSeconds);
        }

        private async void OnImportClicked(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(EnvImportBox.Text))
            {
                StatusText.Text = "Paste KEY=value lines first.";
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
            StatusText.Text = "Imported .env entries.";
        }

        private void OnCloseDialogClicked(object sender, RoutedEventArgs e)
        {
            HideDialogs();
            ClearSecretForm();
        }

        private void OnSecretSelectionChanged(object sender, SelectionChangedEventArgs e)
            => SetSelectionActions(SecretsList.SelectedItem is SecretListItem);

        private void OnFilterChanged(object sender, object e)
            => ApplyFilters();

        private void OnFilterEnvironmentClicked(object sender, RoutedEventArgs e)
        {
            environmentFilter = (sender as Button)?.Content?.ToString() ?? "All";
            ApplyFilters();
        }

        private void OnSecretEnvironmentClicked(object sender, RoutedEventArgs e)
            => SetSecretEnvironment((sender as ToggleButton)?.Content?.ToString() ?? "Dev");

        private void OnImportEnvironmentClicked(object sender, RoutedEventArgs e)
        {
            SetImportEnvironment((sender as ToggleButton)?.Content?.ToString() ?? "Dev");
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

            SetSelectionActions(false);
            ExportButton.IsEnabled = Secrets.Count > 0;
            EmptyState.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            OnPropertyChanged(nameof(SecretsListVisibility));
            StatusText.Text = filtered.Count == 0
                ? string.Empty
                : $"{filtered.Count} secret{(filtered.Count == 1 ? string.Empty : "s")} shown.";
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
            SetSecretEnvironment("Dev");
        }

        private void SetSecretEnvironment(string environment)
        {
            secretEnvironment = environment;
            DevToggle.IsChecked = environment == "Dev";
            StagingToggle.IsChecked = environment == "Staging";
            ProdToggle.IsChecked = environment == "Prod";
        }

        private void SetImportEnvironment(string environment)
        {
            importEnvironment = environment;
            ImportDevToggle.IsChecked = environment == "Dev";
            ImportStagingToggle.IsChecked = environment == "Staging";
            ImportProdToggle.IsChecked = environment == "Prod";
        }

        private void SetSelectionActions(bool hasSelection)
        {
            EditButton.IsEnabled = hasSelection;
            CopyButton.IsEnabled = hasSelection;
            DeleteButton.IsEnabled = hasSelection;
            ExportButton.IsEnabled = Secrets.Count > 0;
            ActionBar.Visibility = hasSelection || Secrets.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
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
                ReplaceComboItems(WorkspaceBox, [.. workspaces, "Add workspace..."], selected);
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

        private static string ClipboardStatus(string prefix, int clearSeconds)
            => clearSeconds > 0
                ? $"{prefix} Clipboard clears in {clearSeconds}s."
                : $"{prefix} Clipboard auto-clear is off.";

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
            => value.Length <= 4
                ? new string('•', Math.Max(value.Length, 1))
                : $"{new string('•', Math.Min(value.Length, 12))}{value[^4..]}";
    }

    public sealed class ImportPreviewListItem
    {
        public string Line { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public string MaskedValue { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;

        public static ImportPreviewListItem From(ImportPreviewItem item)
            => new()
            {
                Line = item.Line.ToString(),
                Key = string.IsNullOrWhiteSpace(item.Key) ? "-" : item.Key,
                MaskedValue = string.IsNullOrWhiteSpace(item.Value) ? "-" : SecretListItem.From(SecretRecord.Create("Default", item.Key, item.Value, "Dev", "", "")).MaskedValue,
                Status = item.Status.ToString()
            };
    }
}
