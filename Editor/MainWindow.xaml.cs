using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace WinOS_Editor
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private AppDocument _document = null!;
        private AppElementDefinition? _selectedElement;
        private AppEventDefinition? _selectedEvent;
        private AppBindingDefinition? _selectedBinding;
        private string _statusMessage = "WinOS-Editor : Application init success!";
        private readonly List<DispatcherTimer> _previewTimers = new();
        private readonly string? _solutionRoot;
        private TextBox? _dragValueTextBox;
        private double _dragValueStartX;
        private string _dragValueStartText = string.Empty;
        private int _dragValueLastStep;

        public MainWindow()
            : this(null, null)
        {
        }

        public MainWindow(AppDocument? initialDocument, string? initialPackagePath)
        {
            InitializeComponent();
            DataContext = this;

            _solutionRoot = ProjectPaths.FindSolutionRoot(AppContext.BaseDirectory);
            LoadDocument(initialDocument ?? AppDocument.CreateDefault(_solutionRoot), initialPackagePath);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public Array ElementTypes => Enum.GetValues(typeof(AppElementType));

        public Array AnchorPresets => Enum.GetValues(typeof(AnchorPreset));

        public Array EventTypes => Enum.GetValues(typeof(AppEventType));

        public Array ActionTypes => Enum.GetValues(typeof(AppActionType));

        public Array PropertyTypes => Enum.GetValues(typeof(AppPropertyType));

        public IReadOnlyList<string> SystemStateKeys => AppSystemStateCatalog.Keys;

        public AppDocument Document
        {
            get => _document;
            set
            {
                if (ReferenceEquals(_document, value))
                {
                    return;
                }

                if (_document != null)
                {
                    _document.Changed -= OnDocumentChanged;
                }

                _document = value;
                _document.Changed += OnDocumentChanged;
                OnPropertyChanged(nameof(Document));
            }
        }

        public AppElementDefinition? SelectedElement
        {
            get => _selectedElement;
            set
            {
                if (ReferenceEquals(_selectedElement, value))
                {
                    return;
                }

                _selectedElement = value;
                SelectedEvent = value?.Events.FirstOrDefault();
                OnPropertyChanged(nameof(SelectedElement));
                OnPropertyChanged(nameof(HasSelectedElement));
                RenderPreview();
            }
        }

        public AppEventDefinition? SelectedEvent
        {
            get => _selectedEvent;
            set
            {
                if (ReferenceEquals(_selectedEvent, value))
                {
                    return;
                }

                _selectedEvent = value;
                SelectedBinding = value?.Bindings.FirstOrDefault();
                OnPropertyChanged(nameof(SelectedEvent));
                OnPropertyChanged(nameof(HasSelectedEvent));
            }
        }

        public AppBindingDefinition? SelectedBinding
        {
            get => _selectedBinding;
            set
            {
                if (ReferenceEquals(_selectedBinding, value))
                {
                    return;
                }

                if (_selectedBinding != null)
                {
                    _selectedBinding.PropertyChanged -= OnSelectedBindingPropertyChanged;
                }

                _selectedBinding = value;
                if (_selectedBinding != null)
                {
                    _selectedBinding.PropertyChanged += OnSelectedBindingPropertyChanged;
                }
                OnPropertyChanged(nameof(SelectedBinding));
                OnPropertyChanged(nameof(HasSelectedBinding));
                OnPropertyChanged(nameof(SelectedBindingUsesSystemStateKeySelection));
            }
        }

        public bool HasSelectedElement => SelectedElement != null;

        public bool HasSelectedEvent => SelectedEvent != null;

        public bool HasSelectedBinding => SelectedBinding != null;

        public bool SelectedBindingUsesSystemStateKeySelection =>
            SelectedBinding?.UsesSystemStateKeySelection == true;

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                if (_statusMessage == value)
                {
                    return;
                }

                _statusMessage = value;
                OnPropertyChanged(nameof(StatusMessage));
            }
        }

        private void OnDocumentChanged(object? sender, EventArgs e)
        {
            RenderPreview();
        }

        private void OnSelectedBindingPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppBindingDefinition.ActionType) ||
                e.PropertyName == nameof(AppBindingDefinition.UsesSystemStateKeySelection))
            {
                OnPropertyChanged(nameof(SelectedBindingUsesSystemStateKeySelection));
            }
        }

        private void LoadDocument(AppDocument document, string? sourcePackagePath)
        {
            Document = document;
            SelectedElement = Document.Elements.FirstOrDefault();
            RenderPreview();

            if (!string.IsNullOrWhiteSpace(sourcePackagePath))
            {
                StatusMessage = $"WinOS-Editor : {System.IO.Path.GetFileName(sourcePackagePath)} imported!";
            }
        }

        private void AddPanel_Click(object sender, RoutedEventArgs e) => AddElement(AppElementType.Panel);

        private void AddCanvas_Click(object sender, RoutedEventArgs e) => AddElement(AppElementType.Canvas);

        private void AddCanvasGroup_Click(object sender, RoutedEventArgs e) => AddElement(AppElementType.CanvasGroup);

        private void AddGridLayoutGroup_Click(object sender, RoutedEventArgs e) => AddElement(AppElementType.GridLayoutGroup);

        private void AddLabel_Click(object sender, RoutedEventArgs e) => AddElement(AppElementType.Label);

        private void AddButton_Click(object sender, RoutedEventArgs e) => AddElement(AppElementType.Button);

        private void AddSlider_Click(object sender, RoutedEventArgs e) => AddElement(AppElementType.Slider);

        private void AddTextField_Click(object sender, RoutedEventArgs e) => AddElement(AppElementType.TextField);

        private void AddToggle_Click(object sender, RoutedEventArgs e) => AddElement(AppElementType.Toggle);

        private void AddDropdown_Click(object sender, RoutedEventArgs e) => AddElement(AppElementType.Dropdown);

        private void AddImage_Click(object sender, RoutedEventArgs e) => AddElement(AppElementType.Image);

        private void AddElement(AppElementType type)
        {
            var root = Document.Elements.First(element => element.Type == AppElementType.RootWindow);
            var parent = SelectedElement != null &&
                         (SelectedElement.Type == AppElementType.Panel ||
                          SelectedElement.Type == AppElementType.RootWindow ||
                          SelectedElement.Type == AppElementType.Canvas ||
                          SelectedElement.Type == AppElementType.CanvasGroup ||
                          SelectedElement.Type == AppElementType.GridLayoutGroup)
                ? SelectedElement
                : root;
            var id = Document.NextElementId();

            var element = new AppElementDefinition
            {
                Id = id,
                ParentId = type == AppElementType.RootWindow ? -1 : parent.Id,
                ZIndex = Document.Elements.Count,
                Type = type,
                Flags = AppElementFlags.Visible | (type == AppElementType.Button || type == AppElementType.Slider || type == AppElementType.TextField || type == AppElementType.Toggle || type == AppElementType.Dropdown ? AppElementFlags.Interactive : AppElementFlags.None),
                X = 24,
                Y = 24 + (id * 6),
                Width = type == AppElementType.Label ? 180 : ((type == AppElementType.Canvas || type == AppElementType.CanvasGroup || type == AppElementType.GridLayoutGroup) ? 220 : 160),
                Height = type == AppElementType.Label ? 24 : (type == AppElementType.TextField ? 34 : ((type == AppElementType.Canvas || type == AppElementType.CanvasGroup || type == AppElementType.GridLayoutGroup) ? 140 : 42)),
                BackgroundColor = type switch
                {
                    AppElementType.Panel => 0x223047,
                    AppElementType.Canvas => 0x1A2538,
                    AppElementType.CanvasGroup => 0x1F2937,
                    AppElementType.GridLayoutGroup => 0x1F3446,
                    AppElementType.Button => 0x355B88,
                    AppElementType.Slider => 0x0E1520,
                    AppElementType.TextField => 0x101826,
                    AppElementType.Toggle => 0x142030,
                    AppElementType.Dropdown => 0x142030,
                    AppElementType.Image => 0x223047,
                    _ => 0x1B2333,
                },
                ForegroundColor = type == AppElementType.Slider ? 0x61C48Bu : 0xF4F8FCu,
                BorderColor = 0x8DA0B4,
                Text = type switch
                {
                    AppElementType.Dropdown => "Option A|Option B|Option C",
                    AppElementType.Canvas => "Canvas",
                    AppElementType.CanvasGroup => "Canvas Group",
                    AppElementType.GridLayoutGroup => "Grid Layout Group",
                    _ => type.ToString(),
                },
                MinValue = 0,
                MaxValue = type == AppElementType.Toggle ? 1 : 100,
                Value = type == AppElementType.Toggle ? 0 : (type == AppElementType.Dropdown ? 0 : 50),
                OpacityPercent = type == AppElementType.CanvasGroup ? 90 : 100,
                GridColumns = type == AppElementType.GridLayoutGroup ? 4 : 0,
            };

            Document.Elements.Add(element);
            SelectedElement = element;
            StatusMessage = $"WinOS-Editor : Added {type} element #{id}.";
        }

        private void DeleteElement_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedElement == null || SelectedElement.Type == AppElementType.RootWindow)
            {
                return;
            }

            var deletedId = SelectedElement.Id;
            Document.DeleteElement(SelectedElement);
            SelectedElement = Document.Elements.LastOrDefault();
            StatusMessage = $"WinOS-Editor : Deleted element #{deletedId}.";
        }

        private void AddEvent_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedElement == null)
            {
                return;
            }

            var appEvent = new AppEventDefinition { EventType = AppEventType.Click };
            SelectedElement.Events.Add(appEvent);
            SelectedEvent = appEvent;
            StatusMessage = $"WinOS-Editor : Added event to element #{SelectedElement.Id}.";
        }

        private void RemoveEvent_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedElement == null || SelectedEvent == null)
            {
                return;
            }

            SelectedElement.Events.Remove(SelectedEvent);
            SelectedEvent = SelectedElement.Events.FirstOrDefault();
            StatusMessage = $"WinOS-Editor : Removed event from element #{SelectedElement.Id}.";
        }

        private void AddBinding_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedElement == null || SelectedEvent == null)
            {
                return;
            }

            var rootId = Document.Elements.First(element => element.Type == AppElementType.RootWindow).Id;
            var binding = new AppBindingDefinition
            {
                ActionType = AppActionType.SetProperty,
                TargetElementId = rootId,
                PropertyType = AppPropertyType.Text,
            };
            SelectedEvent.Bindings.Add(binding);
            SelectedBinding = binding;
            StatusMessage = $"WinOS-Editor : Added binding to {SelectedEvent.EventType} on element #{SelectedElement.Id}.";
        }

        private void RemoveBinding_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedEvent == null || SelectedBinding == null)
            {
                return;
            }

            SelectedEvent.Bindings.Remove(SelectedBinding);
            SelectedBinding = SelectedEvent.Bindings.FirstOrDefault();
            StatusMessage = "WinOS-Editor : Removed binding.";
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            OpenDocumentFromDialog();
        }

        private void Open_Executed(object sender, System.Windows.Input.ExecutedRoutedEventArgs e)
        {
            OpenDocumentFromDialog();
        }

        private void OpenDocumentFromDialog()
        {
            try
            {
                var dialog = new OpenFileDialog
                {
                    Filter = "WinOS App Package (*.wdexe)|*.wdexe|All Files (*.*)|*.*",
                    CheckFileExists = true,
                    Title = "Open WinOS GUI Application",
                };

                if (dialog.ShowDialog(this) != true)
                {
                    return;
                }

                OpenDocument(dialog.FileName);
            }
            catch (Exception ex)
            {
                StatusMessage = $"WinOS-Editor : Open failed: {ex.Message}";
            }
        }

        public void OpenDocument(string packagePath)
        {
            var solutionRoot = ProjectPaths.FindSolutionRoot(AppContext.BaseDirectory);
            var importedDocument = WdexeImporter.ImportDocument(packagePath, solutionRoot);
            LoadDocument(importedDocument, packagePath);
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var solutionRoot = ProjectPaths.FindSolutionRoot(AppContext.BaseDirectory);
                if (solutionRoot == null)
                {
                    throw new InvalidOperationException("WinOS-Editor : Could not locate the WinOS solution root.");
                }

                var result = WdexeExporter.ExportDocument(Document, solutionRoot, Document.PackageAlias);
                Document.PackageAlias = result.PackageAlias;
                StatusMessage = $"WinOS-Editor : Exported {result.PackageAlias} to {result.PackagePath} with {result.AssetCount} copied asset(s).";
            }
            catch (Exception ex)
            {
                StatusMessage = $"WinOS-Editor : Export failed: {ex.Message}";
            }
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void InspectorTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox textBox && !textBox.IsReadOnly)
            {
                textBox.SelectAll();
            }
        }

        private void InspectorTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not TextBox textBox || textBox.IsReadOnly || textBox.IsKeyboardFocusWithin)
            {
                return;
            }

            e.Handled = true;
            textBox.Focus();
            textBox.SelectAll();
        }

        private void InspectorTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is TextBox textBox && StepTextBoxValue(textBox, e.Delta > 0 ? 1 : -1))
            {
                e.Handled = true;
            }
        }

        private void InspectorLabel_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (TryFindSiblingTextBox(sender as DependencyObject, out var textBox) &&
                StepTextBoxValue(textBox, e.Delta > 0 ? 1 : -1))
            {
                e.Handled = true;
            }
        }

        private void InspectorLabel_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!TryFindSiblingTextBox(sender as DependencyObject, out var textBox) || !CanStepText(textBox.Text))
            {
                return;
            }

            _dragValueTextBox = textBox;
            _dragValueStartX = e.GetPosition(this).X;
            _dragValueStartText = textBox.Text;
            _dragValueLastStep = 0;
            Mouse.Capture(sender as IInputElement);
            e.Handled = true;
        }

        private void InspectorLabel_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragValueTextBox == null || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            var step = (int)((e.GetPosition(this).X - _dragValueStartX) / 8.0);
            if (step == _dragValueLastStep)
            {
                return;
            }

            _dragValueTextBox.Text = _dragValueStartText;
            StepTextBoxValue(_dragValueTextBox, step);
            _dragValueLastStep = step;
            e.Handled = true;
        }

        private void InspectorLabel_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragValueTextBox == null)
            {
                return;
            }

            _dragValueTextBox.Focus();
            _dragValueTextBox.SelectAll();
            _dragValueTextBox = null;
            Mouse.Capture(null);
            e.Handled = true;
        }

        private static bool TryFindSiblingTextBox(DependencyObject? source, out TextBox textBox)
        {
            textBox = null!;
            if (source == null)
            {
                return false;
            }

            var parent = VisualTreeHelper.GetParent(source);
            while (parent != null && parent is not Panel)
            {
                parent = VisualTreeHelper.GetParent(parent);
            }

            if (parent is not Panel panel)
            {
                return false;
            }

            textBox = panel.Children.OfType<TextBox>().FirstOrDefault()!;
            return textBox != null && !textBox.IsReadOnly;
        }

        private static bool StepTextBoxValue(TextBox textBox, int delta)
        {
            if (textBox.IsReadOnly || delta == 0)
            {
                return false;
            }

            var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            delta *= step;

            var trimmed = textBox.Text.Trim();
            var isPercent = trimmed.EndsWith("%", StringComparison.Ordinal);
            var numberText = isPercent ? trimmed[..^1].Trim() : trimmed;

            if (!int.TryParse(numberText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                return false;
            }

            textBox.Text = (value + delta).ToString(System.Globalization.CultureInfo.InvariantCulture) + (isPercent ? "%" : string.Empty);
            textBox.CaretIndex = textBox.Text.Length;
            return true;
        }

        private static bool CanStepText(string text)
        {
            var trimmed = text.Trim();
            if (trimmed.EndsWith("%", StringComparison.Ordinal))
            {
                trimmed = trimmed[..^1].Trim();
            }

            return int.TryParse(trimmed, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _);
        }

        private void ComboBox_SelectionChanged(object sender)
        {

        }
    }
}
