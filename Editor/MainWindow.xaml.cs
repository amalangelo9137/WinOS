using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinOS_Editor.toolchains;

namespace WinOS_Editor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private FrameworkProject? CurrentProject; // currently loaded project
        private string? CurrentProjectPath; // obvious lol

        private readonly DocMgr _documentManager = new();

        private readonly ToolchainManager _toolchainManager = new();

        public MainWindow()
        {
            InitializeComponent();

            if (App.FilePath != string.Empty)
                OpenProject(App.FilePath);

            _toolchainManager.DetectLLVM();
        }

        // Props set;
        public void FrameworkNameSet(string content)
        { CurrentProject?.Name = content; }
        public void FrameworkVersionSet(string content)
        { CurrentProject?.Version = content; }
        public void FrameworkDescriptionSet(string content)
        { CurrentProject?.Description = content; }

        /// <summary>
        /// Shhows the Framework metadata window.
        /// </summary>
        /// <param name="sender">sender</param>
        /// <param name="e">eventargs</param>
        private void ShowFrameworkMetadata(object sender, RoutedEventArgs? e)
        {
            FMeta window = new() { Owner = this };

            if (CurrentProject != null)
            {
                window.FrameworkNameSet(CurrentProject.Name);
                window.FrameworkVersionSet(CurrentProject.Version);
                window.FrameworkDescriptionSet(CurrentProject.Description);
            }

            window.ShowDialog();
        }


        private bool PrepareForProjectSwitch()
        {
            if (CurrentProject == null)
                return true;

            foreach (TabItem tab in EditorTabs.Items
                         .Cast<TabItem>().ToList())
            {
                if (tab.Tag is Document document)
                {
                    if (!ConfirmDocumentClose(document))
                        return false;
                }
            }

            if (!SaveProject())
                return false;

            // Clear the previous project's workspace
            EditorTabs.Items.Clear();
            _documentManager.Clear();
            ProjectTree.Items.Clear();

            return true;
        }

        /// <summary>
        /// Creates a new project.
        /// </summary>
        /// <param name="sender">sender</param>
        /// <param name="e">eventargs</param>

        private void NewProject(object? sender, RoutedEventArgs? e)
        {
            // Safely close the previous project
            if (!PrepareForProjectSwitch()) return;

            // Create and save the new project
            CurrentProject = new FrameworkProject();
            CurrentProjectPath = string.Empty;

            if (!SaveProject())
            {
                CurrentProject = null;
                CurrentProjectPath = null;
                return;
            }

            // Generate demo.cpp from the template
            string projectDirectory =
                Path.GetDirectoryName(CurrentProjectPath)!;

            string templatePath =
                Path.Combine(AppContext.BaseDirectory,
                             "Templates", "demo.cpp");

            string demoPath =
                Path.Combine(projectDirectory, "demo.cpp");

            try
            {
                File.Copy(templatePath, demoPath);

                CurrentProject.SourceFiles.Add("demo.cpp");

                RefreshProjectTree();
                OpenDocumentInEditor(demoPath);

                SaveProject();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to create starter source file:\n{ex.Message}",
                    "New Project",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            ShowFrameworkMetadata(this, null);
        }

        /// <summary>
        /// Opens a project from path (file picker)
        /// </summary>
        /// <param name="sender">sender</param>
        /// <param name="e">eventargs</param>
        // overload 1 : OG
        public void OpenProject(string projectPath)
        {
            string path;

            if (projectPath != string.Empty)
            {
                path = projectPath;
            }
            else
            {
                // setup file picker
                OpenFileDialog openFileDialog = new()
                {
                    Title = "Open WinOS Framework Project",
                    Filter = "WinDroid Framework Project (*.wdfp)|*.wdfp",
                    DefaultExt = ".wdfp",
                    AddExtension = true,
                };

                // open window and return if closed without picking
                if (openFileDialog.ShowDialog(this) == false) return;
                else if (openFileDialog.FileName == string.Empty) // return if empty path
                {
                    MessageBox.Show("Path cannot be empty!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                path = openFileDialog.FileName;
            }

            if (!PrepareForProjectSwitch()) return;

            // temporary, so the current project wont get pre cooked
            FrameworkProject? tmp = ProjectManager.Load(path);
            if (tmp != null)
            {
                // write current project and its path
                CurrentProjectPath = path;
                CurrentProject = tmp;

                // rebuild project hierarchy
                RefreshProjectTree();

                // restore previously open documents
                string projectDirectory = Path.GetDirectoryName(CurrentProjectPath)!;

                foreach (string relativePath in CurrentProject.OpenDocuments)
                {
                    string fullPath = Path.GetFullPath(
                        Path.Combine(projectDirectory, relativePath)
                    );

                    if (File.Exists(fullPath))
                        OpenDocumentInEditor(fullPath);
                }

                // restore previously active document
                if (!string.IsNullOrEmpty(CurrentProject.ActiveDocumentPath))
                {
                    string activePath = Path.GetFullPath(
                        Path.Combine(projectDirectory, CurrentProject.ActiveDocumentPath)
                    );

                    foreach (TabItem tab in EditorTabs.Items)
                    {
                        if (tab.Tag is Document document &&
                            Path.GetFullPath(document.FilePath) == activePath)
                        {
                            EditorTabs.SelectedItem = tab;
                            break;
                        }
                    }
                }
            }
        }
        // overload 2 : for button
        private void OpenProject(object? sender, RoutedEventArgs e)
        {
            OpenProject(string.Empty);
        }

        /// <summary>
        /// Saves the project. Opens file picker if necessary.
        /// </summary>
        /// <param name="sender">sender</param>
        /// <param name="e">eventargs</param>
        // overload 1 : returns bool
        private bool SaveProject()
        {
            // js save to existing if path is already there
            if (CurrentProjectPath != string.Empty && CurrentProject != null)
            {
                UpdateWorkspaceState();
                return ProjectManager.Save(CurrentProject, CurrentProjectPath);
            }
            // else if no path but project is present
            else
            {
                {
                    // setup file picker
                    SaveFileDialog saveFileDialog = new()
                    {
                        Title = "Save WinOS Framework Project",
                        Filter = "WinDroid Framework Project (*.wdfp)|*.wdfp",
                        DefaultExt = ".wdfp",
                        AddExtension = true,
                    };

                    // open window and return if closed without picking
                    if (saveFileDialog.ShowDialog(this) == false) return false;
                    else if (saveFileDialog.FileName == string.Empty) // return if empty path
                    {
                        MessageBox.Show("Path cannot be empty!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return false;
                    }

                    UpdateWorkspaceState();
                    if (ProjectManager.Save(CurrentProject, saveFileDialog.FileName))
                    {
                        CurrentProjectPath = saveFileDialog.FileName;
                        return true;
                    }

                    return false;
                }
            }
        }
        // overload 2 : returns nothing, for button
        private void SaveProject(object? sender, RoutedEventArgs? e) { SaveProject(); }

        private void Exit(object sender, RoutedEventArgs? e)
        {
            Application.Current.Shutdown();
        }

        private void SaveplusExit(object sender, RoutedEventArgs e)
        {
            if (SaveProject() == true) Exit(this, null);
        }

        // tree double click
        private void FWTreeDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {

        }
        private void ProjectTreeDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (ProjectTree.SelectedItem is not TreeViewItem item)
                return;

            if (item.Tag is not string filePath)
                return;

            if (!File.Exists(filePath))
                return;

            OpenDocumentInEditor(filePath);
        }

        private void CreateEditorTab(Document document)
        {
            // used to filter out the TextChanged which happens
            // when the document is initially opened.
            bool initializing = true;

            // create the editor UI, mainly the textbox
            TextBox editor = new()
            {
                Name = "Code",
                TextWrapping = TextWrapping.Wrap,
                Margin = new(24,4,4,4),
                Background = SystemColors.DesktopBrush,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
                BorderBrush = SystemColors.AccentColorDark2Brush,
                Cursor = Cursors.IBeam,
                AcceptsReturn = true,
                AcceptsTab = true
            };

            // create the tab for that file
            TabItem tab = new()
            {
                Header = document.FileName,
                Content = editor,
                Tag = document
            };

            editor.TextChanged += (sender, e) =>
            {
                if (initializing)
                    return; // here.

                document.Content = editor.Text;
                document.IsModified = true;

                tab.Header = document.FileName + "*"; // marker to indicate unsaved
            };

            initializing = false;

            // add  it to the TabControl and activate it
            EditorTabs.Items.Add(tab);
            EditorTabs.SelectedItem = tab;
        }

        private void OpenDocumentInEditor(string filePath)
        {
            Document document = _documentManager.OpenDocument(filePath);

            // Check whether this document already has a tab.
            foreach (TabItem tab in EditorTabs.Items)
            {
                if (tab.Tag == document)
                {
                    EditorTabs.SelectedItem = tab;
                    return;
                }
            }

            // No existing tab: create one.
            CreateEditorTab(document);
        }

        private void SaveActiveDocument()
        {
            if (_documentManager.ActiveDocument is not Document document)
                return;

            try
            {
                _documentManager.SaveDocument(document);

                if (EditorTabs.SelectedItem is TabItem tab)
                {
                    tab.Header = document.FileName;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Save Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        // editor tab delta
        private void EditorTabsSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EditorTabs.SelectedItem is TabItem tab &&
                tab.Tag is Document document)
            {
                _documentManager.SetActiveDocument(document);
            }
        }

        // editor toolbar button click functions
        private void SaveActiveDocumentToolbarClick(object sender, RoutedEventArgs e) { SaveActiveDocument(); }
        private void SaveAllDocumentsToolbarClick(object sender, RoutedEventArgs e)
        {
            try
            {
                _documentManager.SaveAllDocuments();

                foreach (TabItem tab in EditorTabs.Items)
                {
                    if (tab.Tag is Document document)
                        tab.Header = document.FileName;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Save All Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void CloseActiveTabToolbarClick(object sender, RoutedEventArgs e)
        {
            if (EditorTabs.SelectedItem is not TabItem tab)
                return;

            if (tab.Tag is Document document)
            {
                if (!ConfirmDocumentClose(document))
                    return;

                _documentManager.CloseDocument(document);
            }

            EditorTabs.Items.Remove(tab);
        }
        private void CloseAllTabsToolbarClick(object sender, RoutedEventArgs e)
        {
            foreach (TabItem tab in EditorTabs.Items
                 .Cast<TabItem>().ToList())
            {
                if (tab.Tag is Document document)
                {
                    if (!ConfirmDocumentClose(document))
                        return;

                    _documentManager.CloseDocument(document);
                }

                EditorTabs.Items.Remove(tab);
            }
        }

        private bool ConfirmDocumentClose(Document document)
        {
            if (!document.IsModified)
                return true;

            MessageBoxResult result = MessageBox.Show(
                $"Save changes to {document.FileName}?",
                "Unsaved Changes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning
            );

            if (result == MessageBoxResult.Cancel)
                return false;

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    _documentManager.SaveDocument(document);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Save Failed",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
            }

            return true;
        }

        private void RefreshProjectTree()
        {
            ProjectTree.Items.Clear();

            if (CurrentProject == null ||
                string.IsNullOrEmpty(CurrentProjectPath))
                return;

            string projectDirectory = Path.GetDirectoryName(CurrentProjectPath)!;

            foreach (string source in CurrentProject.SourceFiles)
            {
                string fullPath = Path.GetFullPath(
                    Path.Combine(projectDirectory, source));

                TreeViewItem item = new()
                {
                    Header = Path.GetFileName(source),
                    Tag = fullPath
                };

                ProjectTree.Items.Add(item);
            }
        }

        // project hierarchy > add
        private void AddExistingSource(object sender, RoutedEventArgs e)
        {
            if (CurrentProject == null || string.IsNullOrEmpty(CurrentProjectPath))
            {
                MessageBox.Show("Open or create a framework project first.");
                return;
            }

            OpenFileDialog dialog = new()
            {
                Title = "Add Existing Source File",
                Filter = "C++ Source Files (*.cpp)|*.cpp|Assembly Files (*.asm)|*.asm|All Files (*.*)|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog() != true)
                return;

            foreach (string file in dialog.FileNames)
            {
                string projectDirectory = Path.GetDirectoryName(CurrentProjectPath)!;
                string relativePath = Path.GetRelativePath(projectDirectory, file);

                if (!CurrentProject.SourceFiles.Contains(relativePath))
                    CurrentProject.SourceFiles.Add(relativePath);
            }

            RefreshProjectTree();
        }

        private void UpdateWorkspaceState()
        {
            if (CurrentProject == null ||
                string.IsNullOrEmpty(CurrentProjectPath))
                return;

            string projectDirectory =
                Path.GetDirectoryName(CurrentProjectPath)!;

            CurrentProject.OpenDocuments.Clear();

            foreach (TabItem tab in EditorTabs.Items)
            {
                if (tab.Tag is Document document)
                {
                    string relativePath = Path.GetRelativePath(
                        projectDirectory,
                        document.FilePath
                    );

                    CurrentProject.OpenDocuments.Add(relativePath);
                }
            }

            if (EditorTabs.SelectedItem is TabItem activeTab &&
                activeTab.Tag is Document activeDocument)
            {
                CurrentProject.ActiveDocumentPath = Path.GetRelativePath(
                    projectDirectory,
                    activeDocument.FilePath
                );
            }
            else
            {
                CurrentProject.ActiveDocumentPath = null;
            }
        }

        private async Task TestClangAsync()
        {
            if (!_toolchainManager.IsAvailable)
            {
                MessageBox.Show("LLVM is not available.");
                return;
            }

            if (CurrentProject == null || CurrentProjectPath == null)
            {
                MessageBox.Show("Open a framework project first.");
                return;
            }

            string sourceFile = Path.Combine(CurrentProjectPath, "demo.cpp");

            if (!File.Exists(sourceFile))
            {
                MessageBox.Show("demo.cpp was not found.");
                return;
            }

            var result = await _toolchainManager.RunClangAsync(sourceFile);

            MessageBox.Show(
                $"Exit code: {result.ExitCode}\n\n" +
                $"Output:\n{result.Output}\n\n" +
                $"Errors:\n{result.Errors}"
            );
        }
    }
}