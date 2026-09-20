using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace WinOS_Editor
{
    [Flags]
    public enum AppElementFlags : uint
    {
        None = 0,
        Visible = 1 << 0,
        Interactive = 1 << 1,
        DraggableRegion = 1 << 2,
        ClipChildren = 1 << 3,
        WindowFrame = 1 << 4,
        AlwaysOnTop = 1 << 5,
    }

    public enum AppElementType : uint
    {
        RootWindow = 0,
        Panel = 1,
        Image = 2,
        Label = 3,
        Button = 4,
        Slider = 5,
        TextField = 6,
        Toggle = 7,
        Dropdown = 8,
        Canvas = 9,
        CanvasGroup = 10,
        GridLayoutGroup = 11,
    }

    public enum AnchorPreset
    {
        Custom = 0,
        TopLeft = 1,
        TopStretch = 2,
        TopRight = 3,
        MiddleLeft = 4,
        Center = 5,
        MiddleStretch = 6,
        MiddleRight = 7,
        BottomLeft = 8,
        BottomStretch = 9,
        BottomRight = 10,
        StretchAll = 11,
    }

    public enum AppEventType : uint
    {
        PointerDown = 0,
        PointerUp = 1,
        PointerMove = 2,
        Click = 3,
        ValueChanged = 4,
        FocusChanged = 5,
        Update = 6,
    }

    public enum AppActionType : uint
    {
        None = 0,
        SetProperty = 1,
        SetTextFromSourceValue = 2,
        CloseWindow = 3,
        BringToFront = 4,
        Shutdown = 5,
        Restart = 6,
        LaunchApp = 7,
        PlayTone = 8,
        MinimizeWindow = 9,
        MaximizeWindow = 10,
        RestoreWindow = 11,
        SetTextFromSystemState = 12,
        LaunchNextApp = 13,
        LaunchAppFromSourceText = 14,
    }

    public static class LayoutValueCodec
    {
        public const int PercentBase = -1000000;
        private const int PercentRange = 10000;

        public static bool TryParse(string? text, int fallback, out int value)
        {
            value = fallback;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var trimmed = text.Trim();
            if (trimmed.EndsWith("%", StringComparison.Ordinal))
            {
                var numberText = trimmed[..^1].Trim();
                if (int.TryParse(numberText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent))
                {
                    percent = Math.Clamp(percent, -PercentRange, PercentRange);
                    value = PercentBase - percent;
                    return true;
                }

                return false;
            }

            if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var absolute))
            {
                value = absolute;
                return true;
            }

            return false;
        }

        public static string Format(int value)
        {
            if (IsPercent(value))
            {
                return $"{PercentBase - value}%";
            }

            return value.ToString(CultureInfo.InvariantCulture);
        }

        public static bool IsPercent(int value) => value >= PercentBase - PercentRange && value <= PercentBase + PercentRange;
    }

    public enum AppPropertyType : uint
    {
        None = 0,
        BackgroundColor = 1,
        ForegroundColor = 2,
        BorderColor = 3,
        Text = 4,
        Value = 5,
        Visible = 6,
    }

    public readonly record struct AppRect(int X, int Y, int Width, int Height);

    public abstract class NotifyBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public static class ColorCodec
    {
        public static string Format(uint color)
        {
            return $"#{color & 0x00FFFFFF:X6}";
        }

        public static uint Parse(string? text, uint fallback)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return fallback;
            }

            var trimmed = text.Trim();
            if (trimmed.StartsWith('#'))
            {
                trimmed = trimmed[1..];
            }

            return uint.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed)
                ? parsed & 0x00FFFFFF
                : fallback;
        }
    }

    public static class AppSystemStateCatalog
    {
        public static IReadOnlyList<string> Keys { get; } = BuildKeys();

        private static IReadOnlyList<string> BuildKeys()
        {
            var keys = new List<string>
            {
                "TIME_GMT",
                "CURRENT_APP",
                "CURRENT_APP_TITLE",
                "APP_COUNT",
                "DISCOVERED_APPS",
                "RUNNING_APPS",
                "RUNNING_APP_TITLES",
                "RUNNING_WINDOW_COUNT",
                "WINDOW_STATE",
                "WINDOW_FRAME_STYLE",
                "WINDOW_TOPMOST",
                "WORKAREA_SIZE",
                "SCREEN_SIZE",
            };

            for (var index = 0; index < 8; index++)
            {
                keys.Add($"RUNNING_APP_SLOT_{index}");
            }

            return keys;
        }
    }

    public sealed class AppBindingDefinition : NotifyBase
    {
        private AppActionType _actionType = AppActionType.SetProperty;
        private int _targetElementId;
        private AppPropertyType _propertyType = AppPropertyType.Text;
        private int _intValue;
        private uint _colorValue = 0xFFFFFF;
        private string _stringValue = string.Empty;

        public event EventHandler? Changed;

        public AppActionType ActionType
        {
            get => _actionType;
            set
            {
                if (SetField(ref _actionType, value))
                {
                    OnPropertyChanged(nameof(DisplayName));
                    OnBindingUsageChanged();
                    RaiseChanged();
                }
            }
        }

        public int TargetElementId
        {
            get => _targetElementId;
            set
            {
                if (SetField(ref _targetElementId, value))
                {
                    OnPropertyChanged(nameof(DisplayName));
                    RaiseChanged();
                }
            }
        }

        public AppPropertyType PropertyType
        {
            get => _propertyType;
            set
            {
                if (SetField(ref _propertyType, value))
                {
                    OnBindingUsageChanged();
                    RaiseChanged();
                }
            }
        }

        public int IntValue
        {
            get => _intValue;
            set
            {
                if (SetField(ref _intValue, value))
                {
                    OnPropertyChanged(nameof(BoolValue));
                    RaiseChanged();
                }
            }
        }

        public uint ColorValue
        {
            get => _colorValue;
            set
            {
                if (SetField(ref _colorValue, value))
                {
                    OnPropertyChanged(nameof(ColorHex));
                    RaiseChanged();
                }
            }
        }

        public string ColorHex
        {
            get => ColorCodec.Format(ColorValue);
            set => ColorValue = ColorCodec.Parse(value, ColorValue);
        }

        public string StringValue
        {
            get => _stringValue;
            set
            {
                if (SetField(ref _stringValue, value))
                {
                    RaiseChanged();
                }
            }
        }

        public string DisplayName => $"{ActionType} -> {TargetElementId}";

        public bool UsesTargetElementId =>
            ActionType == AppActionType.SetProperty ||
            ActionType == AppActionType.SetTextFromSourceValue ||
            ActionType == AppActionType.SetTextFromSystemState;

        public bool UsesPropertyType => ActionType == AppActionType.SetProperty;

        public bool UsesIntValue =>
            ActionType == AppActionType.PlayTone ||
            (ActionType == AppActionType.SetProperty &&
             PropertyType != AppPropertyType.None &&
             PropertyType != AppPropertyType.Text &&
             PropertyType != AppPropertyType.BackgroundColor &&
             PropertyType != AppPropertyType.ForegroundColor &&
             PropertyType != AppPropertyType.BorderColor &&
             PropertyType != AppPropertyType.Visible);

        public bool UsesColorValue =>
            ActionType == AppActionType.SetProperty &&
            (PropertyType == AppPropertyType.BackgroundColor ||
             PropertyType == AppPropertyType.ForegroundColor ||
             PropertyType == AppPropertyType.BorderColor);

        public bool UsesStringValue =>
            ActionType == AppActionType.LaunchApp ||
            ActionType == AppActionType.SetTextFromSystemState ||
            (ActionType == AppActionType.SetProperty && PropertyType == AppPropertyType.Text);

        public bool UsesSystemStateKeySelection => ActionType == AppActionType.SetTextFromSystemState;

        public bool UsesPlainStringValue => UsesStringValue && !UsesSystemStateKeySelection;

        public bool UsesBoolValue =>
            ActionType == AppActionType.SetProperty &&
            PropertyType == AppPropertyType.Visible;

        public bool BoolValue
        {
            get => IntValue != 0;
            set => IntValue = value ? 1 : 0;
        }

        public string TargetElementIdLabel =>
            ActionType == AppActionType.SetTextFromSystemState ? "Label Target Id" : "Target Id";

        public string PropertyTypeLabel => "Property";

        public string IntValueLabel => ActionType switch
        {
            AppActionType.PlayTone => "Tone Frequency (Hz)",
            _ => "Value"
        };

        public string ColorValueLabel => "Color";

        public string StringValueLabel => ActionType switch
        {
            AppActionType.LaunchApp => "Package Alias",
            AppActionType.SetTextFromSystemState => "System State Key",
            AppActionType.SetProperty when PropertyType == AppPropertyType.Text => "Text",
            _ => "Text"
        };

        public bool StringValueMultiline =>
            ActionType == AppActionType.SetProperty && PropertyType == AppPropertyType.Text;

        private void OnBindingUsageChanged()
        {
            OnPropertyChanged(nameof(UsesTargetElementId));
            OnPropertyChanged(nameof(UsesPropertyType));
            OnPropertyChanged(nameof(UsesIntValue));
            OnPropertyChanged(nameof(UsesColorValue));
            OnPropertyChanged(nameof(UsesStringValue));
            OnPropertyChanged(nameof(UsesSystemStateKeySelection));
            OnPropertyChanged(nameof(UsesPlainStringValue));
            OnPropertyChanged(nameof(UsesBoolValue));
            OnPropertyChanged(nameof(TargetElementIdLabel));
            OnPropertyChanged(nameof(PropertyTypeLabel));
            OnPropertyChanged(nameof(IntValueLabel));
            OnPropertyChanged(nameof(ColorValueLabel));
            OnPropertyChanged(nameof(StringValueLabel));
            OnPropertyChanged(nameof(StringValueMultiline));
        }

        private void RaiseChanged()
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public sealed class AppEventDefinition : NotifyBase
    {
        private AppEventType _eventType = AppEventType.Click;

        public AppEventDefinition()
        {
            Bindings.CollectionChanged += OnBindingsChanged;
        }

        public event EventHandler? Changed;

        public ObservableCollection<AppBindingDefinition> Bindings { get; } = new ObservableCollection<AppBindingDefinition>();

        public AppEventType EventType
        {
            get => _eventType;
            set
            {
                if (SetField(ref _eventType, value))
                {
                    OnPropertyChanged(nameof(DisplayName));
                    RaiseChanged();
                }
            }
        }

        public string DisplayName => EventType.ToString();

        private void OnBindingsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (AppBindingDefinition binding in e.OldItems)
                {
                    binding.Changed -= OnBindingChanged;
                }
            }

            if (e.NewItems != null)
            {
                foreach (AppBindingDefinition binding in e.NewItems)
                {
                    binding.Changed += OnBindingChanged;
                }
            }

            RaiseChanged();
        }

        private void OnBindingChanged(object? sender, EventArgs e)
        {
            RaiseChanged();
        }

        private void RaiseChanged()
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public sealed class AppElementDefinition : NotifyBase
    {
        private int _id;
        private int _parentId = -1;
        private int _zIndex;
        private AppElementType _type;
        private AppElementFlags _flags = AppElementFlags.Visible;
        private int _x;
        private int _y;
        private int _width = 120;
        private int _height = 40;
        private int _anchorMinXPercent;
        private int _anchorMinYPercent;
        private int _anchorMaxXPercent;
        private int _anchorMaxYPercent;
        private int _translateX;
        private int _translateY;
        private int _scaleXPercent = 100;
        private int _scaleYPercent = 100;
        private int _pivotXPercent = 50;
        private int _pivotYPercent = 50;
        private int _rotationDegrees;
        private int _opacityPercent = 100;
        private int _gridColumns;
        private int _gridSpacingX = 6;
        private int _gridSpacingY = 6;
        private uint _backgroundColor = 0x1B2333;
        private uint _foregroundColor = 0xF4F8FC;
        private uint _borderColor = 0xC8D3DE;
        private string _text = string.Empty;
        private string _assetPath = string.Empty;
        private int _value;
        private int _minValue;
        private int _maxValue = 100;

        public AppElementDefinition()
        {
            Events.CollectionChanged += OnEventsChanged;
        }

        public event EventHandler? Changed;

        public ObservableCollection<AppEventDefinition> Events { get; } = new ObservableCollection<AppEventDefinition>();

        public int Id
        {
            get => _id;
            set
            {
                if (SetField(ref _id, value))
                {
                    OnPropertyChanged(nameof(DisplayName));
                    RaiseChanged();
                }
            }
        }

        public int ParentId
        {
            get => _parentId;
            set
            {
                if (SetField(ref _parentId, value))
                {
                    RaiseChanged();
                }
            }
        }

        public int ZIndex
        {
            get => _zIndex;
            set
            {
                if (SetField(ref _zIndex, value))
                {
                    RaiseChanged();
                }
            }
        }

        public AppElementType Type
        {
            get => _type;
            set
            {
                if (SetField(ref _type, value))
                {
                    OnPropertyChanged(nameof(DisplayName));
                    RaiseChanged();
                }
            }
        }

        public AppElementFlags Flags
        {
            get => _flags;
            set
            {
                if (SetField(ref _flags, value))
                {
                    OnPropertyChanged(nameof(IsVisible));
                    OnPropertyChanged(nameof(IsInteractive));
                    OnPropertyChanged(nameof(IsDraggableRegion));
                    OnPropertyChanged(nameof(ClipsChildren));
                    OnPropertyChanged(nameof(HasWindowFrame));
                    OnPropertyChanged(nameof(IsAlwaysOnTop));
                    RaiseChanged();
                }
            }
        }

        public bool IsVisible
        {
            get => Flags.HasFlag(AppElementFlags.Visible);
            set => SetFlag(AppElementFlags.Visible, value);
        }

        public bool IsInteractive
        {
            get => Flags.HasFlag(AppElementFlags.Interactive);
            set => SetFlag(AppElementFlags.Interactive, value);
        }

        public bool IsDraggableRegion
        {
            get => Flags.HasFlag(AppElementFlags.DraggableRegion);
            set => SetFlag(AppElementFlags.DraggableRegion, value);
        }

        public bool ClipsChildren
        {
            get => Flags.HasFlag(AppElementFlags.ClipChildren);
            set => SetFlag(AppElementFlags.ClipChildren, value);
        }

        public bool HasWindowFrame
        {
            get => Flags.HasFlag(AppElementFlags.WindowFrame);
            set => SetFlag(AppElementFlags.WindowFrame, value);
        }

        public bool IsAlwaysOnTop
        {
            get => Flags.HasFlag(AppElementFlags.AlwaysOnTop);
            set => SetFlag(AppElementFlags.AlwaysOnTop, value);
        }

        public int X
        {
            get => _x;
            set
            {
                if (SetField(ref _x, value))
                {
                    OnPropertyChanged(nameof(XInput));
                    RaiseChanged();
                }
            }
        }

        public string XInput
        {
            get => LayoutValueCodec.Format(X);
            set
            {
                if (LayoutValueCodec.TryParse(value, X, out var parsed))
                {
                    X = parsed;
                }
            }
        }

        public int Y
        {
            get => _y;
            set
            {
                if (SetField(ref _y, value))
                {
                    OnPropertyChanged(nameof(YInput));
                    RaiseChanged();
                }
            }
        }

        public string YInput
        {
            get => LayoutValueCodec.Format(Y);
            set
            {
                if (LayoutValueCodec.TryParse(value, Y, out var parsed))
                {
                    Y = parsed;
                }
            }
        }

        public int Width
        {
            get => _width;
            set
            {
                if (SetField(ref _width, NormalizeSizeValue(value)))
                {
                    OnPropertyChanged(nameof(WidthInput));
                    RaiseChanged();
                }
            }
        }

        public string WidthInput
        {
            get => LayoutValueCodec.Format(Width);
            set
            {
                if (LayoutValueCodec.TryParse(value, Width, out var parsed))
                {
                    Width = parsed;
                }
            }
        }

        public int Height
        {
            get => _height;
            set
            {
                if (SetField(ref _height, NormalizeSizeValue(value)))
                {
                    OnPropertyChanged(nameof(HeightInput));
                    RaiseChanged();
                }
            }
        }

        public string HeightInput
        {
            get => LayoutValueCodec.Format(Height);
            set
            {
                if (LayoutValueCodec.TryParse(value, Height, out var parsed))
                {
                    Height = parsed;
                }
            }
        }

        public int AnchorMinXPercent
        {
            get => _anchorMinXPercent;
            set
            {
                if (SetField(ref _anchorMinXPercent, ClampPercent(value, 0, 100, 0)))
                {
                    OnPropertyChanged(nameof(AnchorPreset));
                    RaiseChanged();
                }
            }
        }

        public int AnchorMinYPercent
        {
            get => _anchorMinYPercent;
            set
            {
                if (SetField(ref _anchorMinYPercent, ClampPercent(value, 0, 100, 0)))
                {
                    OnPropertyChanged(nameof(AnchorPreset));
                    RaiseChanged();
                }
            }
        }

        public int AnchorMaxXPercent
        {
            get => _anchorMaxXPercent;
            set
            {
                if (SetField(ref _anchorMaxXPercent, ClampPercent(value, 0, 100, 0)))
                {
                    OnPropertyChanged(nameof(AnchorPreset));
                    RaiseChanged();
                }
            }
        }

        public int AnchorMaxYPercent
        {
            get => _anchorMaxYPercent;
            set
            {
                if (SetField(ref _anchorMaxYPercent, ClampPercent(value, 0, 100, 0)))
                {
                    OnPropertyChanged(nameof(AnchorPreset));
                    RaiseChanged();
                }
            }
        }

        public AnchorPreset AnchorPreset
        {
            get => DetectAnchorPreset();
            set
            {
                ApplyAnchorPreset(value);
                OnPropertyChanged(nameof(AnchorPreset));
                RaiseChanged();
            }
        }

        public int TranslateX
        {
            get => _translateX;
            set
            {
                if (SetField(ref _translateX, value))
                {
                    OnPropertyChanged(nameof(TranslateXInput));
                    RaiseChanged();
                }
            }
        }

        public string TranslateXInput
        {
            get => LayoutValueCodec.Format(TranslateX);
            set
            {
                if (LayoutValueCodec.TryParse(value, TranslateX, out var parsed))
                {
                    TranslateX = parsed;
                }
            }
        }

        public int TranslateY
        {
            get => _translateY;
            set
            {
                if (SetField(ref _translateY, value))
                {
                    OnPropertyChanged(nameof(TranslateYInput));
                    RaiseChanged();
                }
            }
        }

        public string TranslateYInput
        {
            get => LayoutValueCodec.Format(TranslateY);
            set
            {
                if (LayoutValueCodec.TryParse(value, TranslateY, out var parsed))
                {
                    TranslateY = parsed;
                }
            }
        }

        public int ScaleXPercent
        {
            get => _scaleXPercent;
            set
            {
                if (SetField(ref _scaleXPercent, ClampPercent(value, 1, 2000, 100)))
                {
                    RaiseChanged();
                }
            }
        }

        public int ScaleYPercent
        {
            get => _scaleYPercent;
            set
            {
                if (SetField(ref _scaleYPercent, ClampPercent(value, 1, 2000, 100)))
                {
                    RaiseChanged();
                }
            }
        }

        public int PivotXPercent
        {
            get => _pivotXPercent;
            set
            {
                if (SetField(ref _pivotXPercent, ClampPercent(value, 0, 100, 50)))
                {
                    RaiseChanged();
                }
            }
        }

        public int PivotYPercent
        {
            get => _pivotYPercent;
            set
            {
                if (SetField(ref _pivotYPercent, ClampPercent(value, 0, 100, 50)))
                {
                    RaiseChanged();
                }
            }
        }

        public int RotationDegrees
        {
            get => _rotationDegrees;
            set
            {
                if (SetField(ref _rotationDegrees, value))
                {
                    RaiseChanged();
                }
            }
        }

        public int OpacityPercent
        {
            get => _opacityPercent;
            set
            {
                if (SetField(ref _opacityPercent, ClampPercent(value, 0, 100, 100)))
                {
                    RaiseChanged();
                }
            }
        }

        public int GridColumns
        {
            get => _gridColumns;
            set
            {
                if (SetField(ref _gridColumns, Math.Max(0, value)))
                {
                    RaiseChanged();
                }
            }
        }

        public int GridSpacingX
        {
            get => _gridSpacingX;
            set
            {
                if (SetField(ref _gridSpacingX, Math.Max(0, value)))
                {
                    RaiseChanged();
                }
            }
        }

        public int GridSpacingY
        {
            get => _gridSpacingY;
            set
            {
                if (SetField(ref _gridSpacingY, Math.Max(0, value)))
                {
                    RaiseChanged();
                }
            }
        }

        public uint BackgroundColor
        {
            get => _backgroundColor;
            set
            {
                if (SetField(ref _backgroundColor, value))
                {
                    OnPropertyChanged(nameof(BackgroundColorHex));
                    RaiseChanged();
                }
            }
        }

        public string BackgroundColorHex
        {
            get => ColorCodec.Format(BackgroundColor);
            set => BackgroundColor = ColorCodec.Parse(value, BackgroundColor);
        }

        public uint ForegroundColor
        {
            get => _foregroundColor;
            set
            {
                if (SetField(ref _foregroundColor, value))
                {
                    OnPropertyChanged(nameof(ForegroundColorHex));
                    RaiseChanged();
                }
            }
        }

        public string ForegroundColorHex
        {
            get => ColorCodec.Format(ForegroundColor);
            set => ForegroundColor = ColorCodec.Parse(value, ForegroundColor);
        }

        public uint BorderColor
        {
            get => _borderColor;
            set
            {
                if (SetField(ref _borderColor, value))
                {
                    OnPropertyChanged(nameof(BorderColorHex));
                    RaiseChanged();
                }
            }
        }

        public string BorderColorHex
        {
            get => ColorCodec.Format(BorderColor);
            set => BorderColor = ColorCodec.Parse(value, BorderColor);
        }

        public string Text
        {
            get => _text;
            set
            {
                if (SetField(ref _text, value))
                {
                    OnPropertyChanged(nameof(DisplayName));
                    RaiseChanged();
                }
            }
        }

        public string AssetPath
        {
            get => _assetPath;
            set
            {
                if (SetField(ref _assetPath, value))
                {
                    RaiseChanged();
                }
            }
        }

        public int Value
        {
            get => _value;
            set
            {
                if (SetField(ref _value, value))
                {
                    RaiseChanged();
                }
            }
        }

        public int MinValue
        {
            get => _minValue;
            set
            {
                if (SetField(ref _minValue, value))
                {
                    RaiseChanged();
                }
            }
        }

        public int MaxValue
        {
            get => _maxValue;
            set
            {
                if (SetField(ref _maxValue, Math.Max(value, MinValue)))
                {
                    RaiseChanged();
                }
            }
        }

        public string DisplayName => $"{Id}: {Type}{(string.IsNullOrWhiteSpace(Text) ? string.Empty : $" - {Text}")}";

        private void SetFlag(AppElementFlags flag, bool enabled)
        {
            Flags = enabled ? Flags | flag : Flags & ~flag;
        }

        private static int NormalizeSizeValue(int value)
        {
            if (LayoutValueCodec.IsPercent(value))
            {
                return value;
            }

            return Math.Max(1, value);
        }

        private static int ClampPercent(int value, int minimum, int maximum, int fallback)
        {
            return Math.Clamp(value, minimum, maximum);
        }

        private AnchorPreset DetectAnchorPreset()
        {
            if (AnchorMinXPercent == 0 && AnchorMinYPercent == 0 && AnchorMaxXPercent == 0 && AnchorMaxYPercent == 0) return AnchorPreset.TopLeft;
            if (AnchorMinXPercent == 0 && AnchorMinYPercent == 0 && AnchorMaxXPercent == 100 && AnchorMaxYPercent == 0) return AnchorPreset.TopStretch;
            if (AnchorMinXPercent == 100 && AnchorMinYPercent == 0 && AnchorMaxXPercent == 100 && AnchorMaxYPercent == 0) return AnchorPreset.TopRight;
            if (AnchorMinXPercent == 0 && AnchorMinYPercent == 50 && AnchorMaxXPercent == 0 && AnchorMaxYPercent == 50) return AnchorPreset.MiddleLeft;
            if (AnchorMinXPercent == 50 && AnchorMinYPercent == 50 && AnchorMaxXPercent == 50 && AnchorMaxYPercent == 50) return AnchorPreset.Center;
            if (AnchorMinXPercent == 0 && AnchorMinYPercent == 50 && AnchorMaxXPercent == 100 && AnchorMaxYPercent == 50) return AnchorPreset.MiddleStretch;
            if (AnchorMinXPercent == 100 && AnchorMinYPercent == 50 && AnchorMaxXPercent == 100 && AnchorMaxYPercent == 50) return AnchorPreset.MiddleRight;
            if (AnchorMinXPercent == 0 && AnchorMinYPercent == 100 && AnchorMaxXPercent == 0 && AnchorMaxYPercent == 100) return AnchorPreset.BottomLeft;
            if (AnchorMinXPercent == 0 && AnchorMinYPercent == 100 && AnchorMaxXPercent == 100 && AnchorMaxYPercent == 100) return AnchorPreset.BottomStretch;
            if (AnchorMinXPercent == 100 && AnchorMinYPercent == 100 && AnchorMaxXPercent == 100 && AnchorMaxYPercent == 100) return AnchorPreset.BottomRight;
            if (AnchorMinXPercent == 0 && AnchorMinYPercent == 0 && AnchorMaxXPercent == 100 && AnchorMaxYPercent == 100) return AnchorPreset.StretchAll;
            return AnchorPreset.Custom;
        }

        private void ApplyAnchorPreset(AnchorPreset preset)
        {
            switch (preset)
            {
                case AnchorPreset.TopLeft:
                    AnchorMinXPercent = 0; AnchorMinYPercent = 0; AnchorMaxXPercent = 0; AnchorMaxYPercent = 0; break;
                case AnchorPreset.TopStretch:
                    AnchorMinXPercent = 0; AnchorMinYPercent = 0; AnchorMaxXPercent = 100; AnchorMaxYPercent = 0; break;
                case AnchorPreset.TopRight:
                    AnchorMinXPercent = 100; AnchorMinYPercent = 0; AnchorMaxXPercent = 100; AnchorMaxYPercent = 0; break;
                case AnchorPreset.MiddleLeft:
                    AnchorMinXPercent = 0; AnchorMinYPercent = 50; AnchorMaxXPercent = 0; AnchorMaxYPercent = 50; break;
                case AnchorPreset.Center:
                    AnchorMinXPercent = 50; AnchorMinYPercent = 50; AnchorMaxXPercent = 50; AnchorMaxYPercent = 50; break;
                case AnchorPreset.MiddleStretch:
                    AnchorMinXPercent = 0; AnchorMinYPercent = 50; AnchorMaxXPercent = 100; AnchorMaxYPercent = 50; break;
                case AnchorPreset.MiddleRight:
                    AnchorMinXPercent = 100; AnchorMinYPercent = 50; AnchorMaxXPercent = 100; AnchorMaxYPercent = 50; break;
                case AnchorPreset.BottomLeft:
                    AnchorMinXPercent = 0; AnchorMinYPercent = 100; AnchorMaxXPercent = 0; AnchorMaxYPercent = 100; break;
                case AnchorPreset.BottomStretch:
                    AnchorMinXPercent = 0; AnchorMinYPercent = 100; AnchorMaxXPercent = 100; AnchorMaxYPercent = 100; break;
                case AnchorPreset.BottomRight:
                    AnchorMinXPercent = 100; AnchorMinYPercent = 100; AnchorMaxXPercent = 100; AnchorMaxYPercent = 100; break;
                case AnchorPreset.StretchAll:
                    AnchorMinXPercent = 0; AnchorMinYPercent = 0; AnchorMaxXPercent = 100; AnchorMaxYPercent = 100; break;
                default:
                    break;
            }
        }

        private void OnEventsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (AppEventDefinition appEvent in e.OldItems)
                {
                    appEvent.Changed -= OnEventChanged;
                }
            }

            if (e.NewItems != null)
            {
                foreach (AppEventDefinition appEvent in e.NewItems)
                {
                    appEvent.Changed += OnEventChanged;
                }
            }

            RaiseChanged();
        }

        private void OnEventChanged(object? sender, EventArgs e)
        {
            RaiseChanged();
        }

        private void RaiseChanged()
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public sealed class AppDocument : NotifyBase
    {
        private string _name = "Boot App";
        private string _packageAlias = "BOOTAPP";
        private int _sampleDisplayWidth = 1920;
        private int _sampleDisplayHeight = 1080;
        private int _canvasWidthValue = 540;
        private int _canvasHeightValue = 340;

        public AppDocument()
        {
            Elements.CollectionChanged += OnElementsChanged;
        }

        public event EventHandler? Changed;

        public ObservableCollection<AppElementDefinition> Elements { get; } = new ObservableCollection<AppElementDefinition>();

        public string Name
        {
            get => _name;
            set
            {
                if (SetField(ref _name, value))
                {
                    RaiseChanged();
                }
            }
        }

        public string PackageAlias
        {
            get => _packageAlias;
            set
            {
                var normalized = WdexeExporter.NormalizePackageAlias(value);
                if (SetField(ref _packageAlias, normalized))
                {
                    RaiseChanged();
                }
            }
        }

        public int SampleDisplayWidth
        {
            get => _sampleDisplayWidth;
            set
            {
                if (SetField(ref _sampleDisplayWidth, Math.Max(1, value)))
                {
                    OnPropertyChanged(nameof(CanvasWidth));
                    OnPropertyChanged(nameof(CanvasWidthInput));
                    RaiseChanged();
                }
            }
        }

        public int SampleDisplayHeight
        {
            get => _sampleDisplayHeight;
            set
            {
                if (SetField(ref _sampleDisplayHeight, Math.Max(1, value)))
                {
                    OnPropertyChanged(nameof(CanvasHeight));
                    OnPropertyChanged(nameof(CanvasHeightInput));
                    RaiseChanged();
                }
            }
        }

        public int CanvasWidth
        {
            get => ResolveCanvasLength(_canvasWidthValue, SampleDisplayWidth);
            set
            {
                if (SetField(ref _canvasWidthValue, NormalizeCanvasLength(value)))
                {
                    OnPropertyChanged(nameof(CanvasWidthInput));
                    RaiseChanged();
                }
            }
        }

        public string CanvasWidthInput
        {
            get => LayoutValueCodec.Format(_canvasWidthValue);
            set
            {
                if (LayoutValueCodec.TryParse(value, _canvasWidthValue, out var parsed))
                {
                    CanvasWidth = parsed;
                }
            }
        }

        public int CanvasHeight
        {
            get => ResolveCanvasLength(_canvasHeightValue, SampleDisplayHeight);
            set
            {
                if (SetField(ref _canvasHeightValue, NormalizeCanvasLength(value)))
                {
                    OnPropertyChanged(nameof(CanvasHeightInput));
                    RaiseChanged();
                }
            }
        }

        public string CanvasHeightInput
        {
            get => LayoutValueCodec.Format(_canvasHeightValue);
            set
            {
                if (LayoutValueCodec.TryParse(value, _canvasHeightValue, out var parsed))
                {
                    CanvasHeight = parsed;
                }
            }
        }

        public IReadOnlyList<AppElementDefinition> GetOrderedElements()
        {
            return Elements.OrderBy(element => element.ZIndex).ThenBy(element => element.Id).ToList();
        }

        public AppElementDefinition? FindElement(int id)
        {
            return Elements.FirstOrDefault(element => element.Id == id);
        }

        public int NextElementId()
        {
            return Elements.Count == 0 ? 0 : Elements.Max(element => element.Id) + 1;
        }

        public void DeleteElement(AppElementDefinition element)
        {
            if (element.Type == AppElementType.RootWindow)
            {
                return;
            }

            var children = Elements.Where(item => item.ParentId == element.Id).ToList();
            foreach (var child in children)
            {
                child.ParentId = element.ParentId;
            }

            Elements.Remove(element);
            RaiseChanged();
        }

        public AppRect GetAbsoluteBounds(AppElementDefinition element)
        {
            var layout = AppTransformMath.ResolveElementLayout(this, element);
            return new AppRect(layout.X, layout.Y, layout.Width, layout.Height);
        }

        /// <summary>
        /// Resolves a canvas scaler value against the editor-only sample display size.
        /// </summary>
        private static int ResolveCanvasLength(int rawValue, int sampleLength)
        {
            return Math.Max(1, AppTransformMath.ResolveLayoutValue(rawValue, Math.Max(1, sampleLength), true));
        }

        /// <summary>
        /// Preserves percent canvas values while preventing invalid absolute canvas sizes.
        /// </summary>
        private static int NormalizeCanvasLength(int value)
        {
            return LayoutValueCodec.IsPercent(value) ? value : Math.Max(1, value);
        }

        public static AppDocument CreateDefault(string? solutionRoot = null)
        {
            var document = new AppDocument
            {
                Name = "WinOS Editor Demo",
                PackageAlias = "BOOTAPP",
                CanvasWidth = 540,
                CanvasHeight = 340,
            };

            var logoPath = ProjectPaths.TryGetDefaultLogoPath(solutionRoot);
            var root = new AppElementDefinition
            {
                Id = 0,
                ParentId = -1,
                ZIndex = 0,
                Type = AppElementType.RootWindow,
                Flags = AppElementFlags.Visible | AppElementFlags.WindowFrame,
                X = 0,
                Y = 0,
                Width = 540,
                Height = 340,
                BackgroundColor = 0x172131,
                ForegroundColor = 0xF4F8FC,
                BorderColor = 0xD5E0EA,
                Text = "Boot App",
                MinValue = 320,
                MaxValue = 200,
            };

            var titleBar = new AppElementDefinition
            {
                Id = 1,
                ParentId = 0,
                ZIndex = 1,
                Type = AppElementType.Panel,
                Flags = AppElementFlags.Visible | AppElementFlags.DraggableRegion,
                X = 0,
                Y = 0,
                Width = 540,
                Height = 42,
                BackgroundColor = 0x2F7D55,
                ForegroundColor = 0xF4F8FC,
                BorderColor = 0x2F7D55,
                Text = "Drag Region",
            };

            var title = new AppElementDefinition
            {
                Id = 2,
                ParentId = 0,
                ZIndex = 2,
                Type = AppElementType.Label,
                Flags = AppElementFlags.Visible,
                X = 18,
                Y = 12,
                Width = 220,
                Height = 18,
                BackgroundColor = 0x2F7D55,
                ForegroundColor = 0xF4F8FC,
                BorderColor = 0x2F7D55,
                Text = "WinOS Boot App",
            };

            var closeButton = new AppElementDefinition
            {
                Id = 3,
                ParentId = 0,
                ZIndex = 3,
                Type = AppElementType.Button,
                Flags = AppElementFlags.Visible | AppElementFlags.Interactive,
                X = 492,
                Y = 8,
                Width = 34,
                Height = 24,
                BackgroundColor = 0xA63A3A,
                ForegroundColor = 0xFFFFFF,
                BorderColor = 0xD58282,
                Text = "X",
            };
            var closeEvent = new AppEventDefinition { EventType = AppEventType.Click };
            closeEvent.Bindings.Add(new AppBindingDefinition
            {
                ActionType = AppActionType.CloseWindow,
                TargetElementId = 0,
                PropertyType = AppPropertyType.None,
            });
            closeButton.Events.Add(closeEvent);

            var minimizeButton = new AppElementDefinition
            {
                Id = 13,
                ParentId = 0,
                ZIndex = 3,
                Type = AppElementType.Button,
                Flags = AppElementFlags.Visible | AppElementFlags.Interactive,
                X = 412,
                Y = 8,
                Width = 34,
                Height = 24,
                BackgroundColor = 0x40576D,
                ForegroundColor = 0xFFFFFF,
                BorderColor = 0x8DAED2,
                Text = "_",
            };
            var minimizeEvent = new AppEventDefinition { EventType = AppEventType.Click };
            minimizeEvent.Bindings.Add(new AppBindingDefinition
            {
                ActionType = AppActionType.MinimizeWindow,
                TargetElementId = 0,
                PropertyType = AppPropertyType.None,
            });
            minimizeButton.Events.Add(minimizeEvent);

            var maximizeButton = new AppElementDefinition
            {
                Id = 14,
                ParentId = 0,
                ZIndex = 3,
                Type = AppElementType.Button,
                Flags = AppElementFlags.Visible | AppElementFlags.Interactive,
                X = 452,
                Y = 8,
                Width = 34,
                Height = 24,
                BackgroundColor = 0x355B88,
                ForegroundColor = 0xFFFFFF,
                BorderColor = 0x8DAED2,
                Text = "[]",
            };
            var maximizeEvent = new AppEventDefinition { EventType = AppEventType.Click };
            maximizeEvent.Bindings.Add(new AppBindingDefinition
            {
                ActionType = AppActionType.MaximizeWindow,
                TargetElementId = 0,
                PropertyType = AppPropertyType.None,
            });
            maximizeButton.Events.Add(maximizeEvent);

            var body = new AppElementDefinition
            {
                Id = 4,
                ParentId = 0,
                ZIndex = 4,
                Type = AppElementType.Panel,
                Flags = AppElementFlags.Visible,
                X = 18,
                Y = 58,
                Width = 504,
                Height = 264,
                BackgroundColor = 0x223047,
                ForegroundColor = 0xF4F8FC,
                BorderColor = 0x40576D,
                Text = "Body",
            };

            var statusLabel = new AppElementDefinition
            {
                Id = 5,
                ParentId = 4,
                ZIndex = 5,
                Type = AppElementType.Label,
                Flags = AppElementFlags.Visible,
                X = 20,
                Y = 18,
                Width = 420,
                Height = 18,
                BackgroundColor = 0x223047,
                ForegroundColor = 0xF4F8FC,
                BorderColor = 0x223047,
                Text = "Drag the green bar. Export from WinOS-Editor.",
            };

            var slider = new AppElementDefinition
            {
                Id = 6,
                ParentId = 4,
                ZIndex = 6,
                Type = AppElementType.Slider,
                Flags = AppElementFlags.Visible | AppElementFlags.Interactive,
                X = 20,
                Y = 132,
                Width = 260,
                Height = 28,
                BackgroundColor = 0x0E1520,
                ForegroundColor = 0x61C48B,
                BorderColor = 0x92A8BD,
                Text = "Activity",
                MinValue = 0,
                MaxValue = 100,
                Value = 35,
            };
            var sliderEvent = new AppEventDefinition { EventType = AppEventType.ValueChanged };
            sliderEvent.Bindings.Add(new AppBindingDefinition
            {
                ActionType = AppActionType.SetTextFromSourceValue,
                TargetElementId = 7,
                PropertyType = AppPropertyType.Text,
            });
            slider.Events.Add(sliderEvent);

            var sliderValueLabel = new AppElementDefinition
            {
                Id = 7,
                ParentId = 4,
                ZIndex = 7,
                Type = AppElementType.Label,
                Flags = AppElementFlags.Visible,
                X = 292,
                Y = 137,
                Width = 64,
                Height = 18,
                BackgroundColor = 0x223047,
                ForegroundColor = 0xF4F8FC,
                BorderColor = 0x223047,
                Text = "35",
            };

            var textField = new AppElementDefinition
            {
                Id = 8,
                ParentId = 4,
                ZIndex = 8,
                Type = AppElementType.TextField,
                Flags = AppElementFlags.Visible | AppElementFlags.Interactive,
                X = 20,
                Y = 70,
                Width = 260,
                Height = 34,
                BackgroundColor = 0x101826,
                ForegroundColor = 0xF4F8FC,
                BorderColor = 0x8DAED2,
                Text = "Type here",
            };

            var pingButton = new AppElementDefinition
            {
                Id = 9,
                ParentId = 4,
                ZIndex = 9,
                Type = AppElementType.Button,
                Flags = AppElementFlags.Visible | AppElementFlags.Interactive,
                X = 20,
                Y = 188,
                Width = 120,
                Height = 34,
                BackgroundColor = 0x355B88,
                ForegroundColor = 0xFFFFFF,
                BorderColor = 0x8DAED2,
                Text = "Ping UI",
            };
            var pingEvent = new AppEventDefinition { EventType = AppEventType.Click };
            pingEvent.Bindings.Add(new AppBindingDefinition
            {
                ActionType = AppActionType.SetProperty,
                TargetElementId = 5,
                PropertyType = AppPropertyType.Text,
                StringValue = "Mini event fired from the boot app.",
            });
            pingButton.Events.Add(pingEvent);

            var restartButton = new AppElementDefinition
            {
                Id = 10,
                ParentId = 4,
                ZIndex = 10,
                Type = AppElementType.Button,
                Flags = AppElementFlags.Visible | AppElementFlags.Interactive,
                X = 156,
                Y = 188,
                Width = 104,
                Height = 34,
                BackgroundColor = 0x2F7D55,
                ForegroundColor = 0xFFFFFF,
                BorderColor = 0x61C48B,
                Text = "Restart",
            };
            var restartEvent = new AppEventDefinition { EventType = AppEventType.Click };
            restartEvent.Bindings.Add(new AppBindingDefinition
            {
                ActionType = AppActionType.Restart,
                TargetElementId = 0,
                PropertyType = AppPropertyType.None,
            });
            restartButton.Events.Add(restartEvent);

            var shutdownButton = new AppElementDefinition
            {
                Id = 11,
                ParentId = 4,
                ZIndex = 11,
                Type = AppElementType.Button,
                Flags = AppElementFlags.Visible | AppElementFlags.Interactive,
                X = 272,
                Y = 188,
                Width = 112,
                Height = 34,
                BackgroundColor = 0x7A3E2C,
                ForegroundColor = 0xFFFFFF,
                BorderColor = 0xD58282,
                Text = "Shutdown",
            };
            var shutdownEvent = new AppEventDefinition { EventType = AppEventType.Click };
            shutdownEvent.Bindings.Add(new AppBindingDefinition
            {
                ActionType = AppActionType.Shutdown,
                TargetElementId = 0,
                PropertyType = AppPropertyType.None,
            });
            shutdownButton.Events.Add(shutdownEvent);

            var clockLabel = new AppElementDefinition
            {
                Id = 15,
                ParentId = 4,
                ZIndex = 12,
                Type = AppElementType.Label,
                Flags = AppElementFlags.Visible,
                X = 20,
                Y = 232,
                Width = 180,
                Height = 18,
                BackgroundColor = 0x223047,
                ForegroundColor = 0xF4F8FC,
                BorderColor = 0x223047,
                Text = "00:00:00 GMT",
            };

            var appInfoLabel = new AppElementDefinition
            {
                Id = 16,
                ParentId = 4,
                ZIndex = 13,
                Type = AppElementType.Label,
                Flags = AppElementFlags.Visible,
                X = 220,
                Y = 232,
                Width = 120,
                Height = 18,
                BackgroundColor = 0x223047,
                ForegroundColor = 0xF4F8FC,
                BorderColor = 0x223047,
                Text = "BOOTAPP",
            };

            var appCountLabel = new AppElementDefinition
            {
                Id = 17,
                ParentId = 4,
                ZIndex = 14,
                Type = AppElementType.Label,
                Flags = AppElementFlags.Visible,
                X = 360,
                Y = 232,
                Width = 120,
                Height = 18,
                BackgroundColor = 0x223047,
                ForegroundColor = 0xF4F8FC,
                BorderColor = 0x223047,
                Text = "1",
            };

            var nextAppButton = new AppElementDefinition
            {
                Id = 18,
                ParentId = 4,
                ZIndex = 15,
                Type = AppElementType.Button,
                Flags = AppElementFlags.Visible | AppElementFlags.Interactive,
                X = 396,
                Y = 188,
                Width = 108,
                Height = 34,
                BackgroundColor = 0x5B4F96,
                ForegroundColor = 0xFFFFFF,
                BorderColor = 0xAB9CF4,
                Text = "Next App",
            };
            var nextAppEvent = new AppEventDefinition { EventType = AppEventType.Click };
            nextAppEvent.Bindings.Add(new AppBindingDefinition
            {
                ActionType = AppActionType.LaunchNextApp,
                TargetElementId = 0,
                PropertyType = AppPropertyType.None,
            });
            nextAppButton.Events.Add(nextAppEvent);

            var clockBindingEvent = new AppEventDefinition { EventType = AppEventType.Update };
            clockBindingEvent.Bindings.Add(new AppBindingDefinition
            {
                ActionType = AppActionType.SetTextFromSystemState,
                TargetElementId = 15,
                PropertyType = AppPropertyType.Text,
                StringValue = "TIME_GMT",
            });
            clockBindingEvent.Bindings.Add(new AppBindingDefinition
            {
                ActionType = AppActionType.SetTextFromSystemState,
                TargetElementId = 16,
                PropertyType = AppPropertyType.Text,
                StringValue = "CURRENT_APP",
            });
            clockBindingEvent.Bindings.Add(new AppBindingDefinition
            {
                ActionType = AppActionType.SetTextFromSystemState,
                TargetElementId = 17,
                PropertyType = AppPropertyType.Text,
                StringValue = "APP_COUNT",
            });
            root.Events.Add(clockBindingEvent);

            document.Elements.Add(root);
            document.Elements.Add(titleBar);
            document.Elements.Add(title);
            document.Elements.Add(minimizeButton);
            document.Elements.Add(maximizeButton);
            document.Elements.Add(closeButton);
            document.Elements.Add(body);
            document.Elements.Add(statusLabel);
            document.Elements.Add(slider);
            document.Elements.Add(sliderValueLabel);
            document.Elements.Add(textField);
            document.Elements.Add(pingButton);
            document.Elements.Add(restartButton);
            document.Elements.Add(shutdownButton);
            document.Elements.Add(clockLabel);
            document.Elements.Add(appInfoLabel);
            document.Elements.Add(appCountLabel);
            document.Elements.Add(nextAppButton);

            if (!string.IsNullOrWhiteSpace(logoPath))
            {
                document.Elements.Add(new AppElementDefinition
                {
                    Id = 12,
                    ParentId = 4,
                    ZIndex = 12,
                    Type = AppElementType.Image,
                    Flags = AppElementFlags.Visible,
                    X = 364,
                    Y = 42,
                    Width = 116,
                    Height = 116,
                    BackgroundColor = 0x223047,
                    ForegroundColor = 0x223047,
                    BorderColor = 0x223047,
                    Text = "Logo",
                    AssetPath = logoPath,
                });
            }

            return document;
        }

        private void OnElementsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (AppElementDefinition element in e.OldItems)
                {
                    element.Changed -= OnElementChanged;
                }
            }

            if (e.NewItems != null)
            {
                foreach (AppElementDefinition element in e.NewItems)
                {
                    element.Changed += OnElementChanged;
                }
            }

            RaiseChanged();
        }

        private void OnElementChanged(object? sender, EventArgs e)
        {
            RaiseChanged();
        }

        private void RaiseChanged()
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public static class ProjectPaths
    {
        public static string? FindSolutionRoot(string startDirectory)
        {
            var directory = new DirectoryInfo(startDirectory);
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "Run", "VirtualDrive")) &&
                    File.Exists(Path.Combine(directory.FullName, "WinOS.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            return null;
        }

        public static string? TryGetDefaultLogoPath(string? solutionRoot)
        {
            if (string.IsNullOrWhiteSpace(solutionRoot))
            {
                solutionRoot = FindSolutionRoot(AppContext.BaseDirectory);
            }

            if (string.IsNullOrWhiteSpace(solutionRoot))
            {
                return null;
            }

            var path = Path.Combine(solutionRoot, "Run", "VirtualDrive", "OSRESOURCES", "WinOS-Logo.bmp");
            return File.Exists(path) ? path : null;
        }
    }

    public static class AssetReferenceResolver
    {
        public static bool IsAliasReference(string? reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                return false;
            }

            var trimmed = reference.Trim();
            if (trimmed.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                return false;
            }

            if (trimmed.Contains(Path.DirectorySeparatorChar) ||
                trimmed.Contains(Path.AltDirectorySeparatorChar) ||
                Path.IsPathRooted(trimmed))
            {
                return false;
            }

            foreach (var character in trimmed)
            {
                if (!char.IsLetterOrDigit(character) &&
                    character != '_' &&
                    character != '-' &&
                    character != '.')
                {
                    return false;
                }
            }

            return true;
        }

        public static string NormalizeAlias(string reference)
        {
            return WdexeExporter.NormalizePackageAlias(reference);
        }

        public static string? ResolveIndexedAliasPath(string assetAlias, string? solutionRoot)
        {
            solutionRoot ??= ProjectPaths.FindSolutionRoot(AppContext.BaseDirectory);
            if (string.IsNullOrWhiteSpace(solutionRoot))
            {
                return null;
            }

            var virtualDriveRoot = Path.Combine(solutionRoot, "Run", "VirtualDrive");
            var indexPath = Path.Combine(virtualDriveRoot, "index.wfs");
            if (!File.Exists(indexPath))
            {
                return null;
            }

            foreach (var line in File.ReadLines(indexPath))
            {
                var separator = line.IndexOf(':');
                if (separator <= 0)
                {
                    continue;
                }

                var alias = line[..separator].Trim();
                if (!string.Equals(alias, assetAlias, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var relativePath = line[(separator + 1)..].Trim();
                var candidate = Path.Combine(virtualDriveRoot, relativePath.Replace('\\', Path.DirectorySeparatorChar));
                return File.Exists(candidate) ? candidate : null;
            }

            return null;
        }

        public static string? ResolveAssetPath(string? reference, string? solutionRoot)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                return null;
            }

            var trimmed = reference.Trim();
            if (File.Exists(trimmed))
            {
                return Path.GetFullPath(trimmed);
            }

            if (!IsAliasReference(trimmed))
            {
                return null;
            }

            return ResolveIndexedAliasPath(NormalizeAlias(trimmed), solutionRoot);
        }
    }

    public sealed record ExportResult(string PackageAlias, string PackagePath, string IndexPath, int AssetCount);

    public static class WdexeExporter
    {
        public const string DefaultPackageAlias = "BOOTAPP";
        private const uint InvalidOffset = 0xFFFFFFFF;
        private const uint InvalidAssetIndex = 0xFFFFFFFF;

        public static ExportResult ExportDocument(AppDocument document, string solutionRoot)
        {
            return ExportDocument(document, solutionRoot, document.PackageAlias);
        }

        public static ExportResult ExportDocument(AppDocument document, string solutionRoot, string? packageAlias)
        {
            var normalizedAlias = NormalizePackageAlias(packageAlias);
            var virtualDriveRoot = Path.Combine(solutionRoot, "Run", "VirtualDrive");
            var packageRoot = Path.Combine(virtualDriveRoot, normalizedAlias);
            var assetRoot = Path.Combine(packageRoot, "Assets");
            Directory.CreateDirectory(packageRoot);
            Directory.CreateDirectory(assetRoot);
            foreach (var existingAsset in Directory.GetFiles(assetRoot))
            {
                try
                {
                    File.SetAttributes(existingAsset, FileAttributes.Normal);
                    File.Delete(existingAsset);
                }
                catch (IOException)
                {
                    // Skip locked files instead of crashing
                    continue;
                }
            }

            var assetCopies = BuildAssetCopies(document, assetRoot, normalizedAlias);
            var packagePath = Path.Combine(packageRoot, $"{normalizedAlias}.wdexe");
            WritePackage(document, packagePath, assetCopies);

            var indexPath = Path.Combine(virtualDriveRoot, "index.wfs");
            RewriteIndex(indexPath, assetCopies, normalizedAlias);

            return new ExportResult(normalizedAlias, packagePath, indexPath, assetCopies.Count);
        }

        public static string NormalizePackageAlias(string? alias)
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                return DefaultPackageAlias;
            }

            var builder = new StringBuilder(alias.Length);
            foreach (var character in alias.Trim())
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(char.ToUpperInvariant(character));
                }
                else if (character == '_' || character == '-' || char.IsWhiteSpace(character))
                {
                    if (builder.Length > 0 && builder[^1] != '_')
                    {
                        builder.Append('_');
                    }
                }
            }

            var normalized = builder.ToString().Trim('_');
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return DefaultPackageAlias;
            }

            if (char.IsDigit(normalized[0]))
            {
                normalized = $"APP_{normalized}";
            }

            return normalized;
        }

        private static List<AssetCopyRecord> BuildAssetCopies(AppDocument document, string assetRoot, string packageAlias)
        {
            var copies = new List<AssetCopyRecord>();
            var bySource = new Dictionary<string, AssetCopyRecord>(StringComparer.OrdinalIgnoreCase);

            foreach (var element in document.GetOrderedElements())
            {
                if (string.IsNullOrWhiteSpace(element.AssetPath))
                {
                    continue;
                }

                var reference = element.AssetPath.Trim();
                if (File.Exists(reference))
                {
                    var sourcePath = Path.GetFullPath(reference);

                    if (bySource.ContainsKey(sourcePath))
                        continue;

                    var extension = Path.GetExtension(sourcePath);
                    var alias = $"{packageAlias}_ASSET_{element.Id}";
                    var targetPath = Path.Combine(assetRoot, $"{alias}{extension}");

                    if (Path.GetFullPath(sourcePath) == Path.GetFullPath(targetPath))
                        continue;

                    using (var sourceStream = new FileStream(
                        sourcePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite))
                    using (var destStream = new FileStream(
                        targetPath,
                        FileMode.Create,
                        FileAccess.Write))
                    {
                        sourceStream.CopyTo(destStream);
                    }

                    var relativePath = $"{packageAlias}\\Assets\\{alias}{extension}";
                    var record = new AssetCopyRecord(sourcePath, alias, relativePath);

                    copies.Add(record);
                    bySource[sourcePath] = record;
                    continue;
                }

                if (!AssetReferenceResolver.IsAliasReference(reference))
                {
                    continue;
                }

                var normalizedAlias = AssetReferenceResolver.NormalizeAlias(reference);
                var aliasKey = $"alias:{normalizedAlias}";
                if (bySource.ContainsKey(aliasKey))
                {
                    continue;
                }

                var aliasRecord = new AssetCopyRecord(aliasKey, normalizedAlias, null);
                copies.Add(aliasRecord);
                bySource[aliasKey] = aliasRecord;
            }

            return copies;
        }

        private static string? ResolveAssetReferenceKey(string? assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return null;
            }

            var reference = assetPath.Trim();
            if (File.Exists(reference))
            {
                return Path.GetFullPath(reference);
            }

            if (AssetReferenceResolver.IsAliasReference(reference))
            {
                return $"alias:{AssetReferenceResolver.NormalizeAlias(reference)}";
            }

            return null;
        }

        private static void WritePackage(AppDocument document, string packagePath, IReadOnlyList<AssetCopyRecord> assetCopies)
        {
            var root = document.Elements.FirstOrDefault(element => element.Type == AppElementType.RootWindow)
                ?? throw new InvalidOperationException("The document must contain a RootWindow element.");

            var orderedElements = document.GetOrderedElements();
            var stringPool = new StringPool();
            var assetIndexBySource = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            var assetRecords = new List<AssetRecord>(assetCopies.Count);

            for (var index = 0; index < assetCopies.Count; index++)
            {
                var copy = assetCopies[index];
                assetRecords.Add(new AssetRecord { AliasOffset = stringPool.Add(copy.Alias) });
                assetIndexBySource[copy.SourceKey] = (uint)index;
            }

            var elementRecords = new List<ElementRecord>(orderedElements.Count);
            var eventRecords = new List<EventRecord>();
            var bindingRecords = new List<BindingRecord>();

            foreach (var element in orderedElements)
            {
                var textOffset = string.IsNullOrWhiteSpace(element.Text) ? InvalidOffset : stringPool.Add(element.Text);
                var assetIndex = InvalidAssetIndex;
                var assetKey = ResolveAssetReferenceKey(element.AssetPath);
                if (assetKey != null && assetIndexBySource.TryGetValue(assetKey, out var resolvedAssetIndex))
                {
                    assetIndex = resolvedAssetIndex;
                }

                elementRecords.Add(new ElementRecord
                {
                    Id = (uint)element.Id,
                    ParentId = element.ParentId,
                    Type = (uint)element.Type,
                    Flags = (uint)element.Flags,
                    X = element.X,
                    Y = element.Y,
                    Width = element.Width,
                    Height = element.Height,
                    AnchorMinXPercent = element.AnchorMinXPercent,
                    AnchorMinYPercent = element.AnchorMinYPercent,
                    AnchorMaxXPercent = element.AnchorMaxXPercent,
                    AnchorMaxYPercent = element.AnchorMaxYPercent,
                    TranslateX = element.TranslateX,
                    TranslateY = element.TranslateY,
                    ScaleXPercent = element.ScaleXPercent,
                    ScaleYPercent = element.ScaleYPercent,
                    PivotXPercent = element.PivotXPercent,
                    PivotYPercent = element.PivotYPercent,
                    RotationDegrees = element.RotationDegrees,
                    OpacityPercent = element.OpacityPercent,
                    GridColumns = element.GridColumns,
                    GridSpacingX = element.GridSpacingX,
                    GridSpacingY = element.GridSpacingY,
                    ZIndex = element.ZIndex,
                    BackgroundColor = element.BackgroundColor,
                    ForegroundColor = element.ForegroundColor,
                    BorderColor = element.BorderColor,
                    Value = element.Value,
                    MinValue = element.MinValue,
                    MaxValue = element.MaxValue,
                    TextOffset = textOffset,
                    AssetIndex = assetIndex,
                });

                foreach (var appEvent in element.Events)
                {
                    var firstBindingIndex = bindingRecords.Count;
                    foreach (var binding in appEvent.Bindings)
                    {
                        bindingRecords.Add(new BindingRecord
                        {
                            ActionType = (uint)binding.ActionType,
                            TargetElementId = (uint)Math.Max(binding.TargetElementId, 0),
                            PropertyType = (uint)binding.PropertyType,
                            IntValue = binding.IntValue,
                            ColorValue = binding.ColorValue,
                            StringOffset = string.IsNullOrWhiteSpace(binding.StringValue)
                                ? InvalidOffset
                                : stringPool.Add(binding.StringValue),
                        });
                    }

                    eventRecords.Add(new EventRecord
                    {
                        SourceElementId = (uint)element.Id,
                        EventType = (uint)appEvent.EventType,
                        FirstBindingIndex = (uint)firstBindingIndex,
                        BindingCount = (uint)appEvent.Bindings.Count,
                    });
                }
            }

            var strings = stringPool.ToArray();
            const ushort headerSize = 60;
            var elementOffset = headerSize;
            var eventOffset = elementOffset + (elementRecords.Count * ElementRecord.Size);
            var bindingOffset = eventOffset + (eventRecords.Count * EventRecord.Size);
            var assetOffset = bindingOffset + (bindingRecords.Count * BindingRecord.Size);
            var stringOffset = assetOffset + (assetRecords.Count * AssetRecord.Size);

            using (var stream = File.Create(packagePath))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, false))
            {
                writer.Write(Encoding.ASCII.GetBytes("WDXE"));
                writer.Write((ushort)3);
                writer.Write(headerSize);
                writer.Write((uint)document.CanvasWidth);
                writer.Write((uint)document.CanvasHeight);
                writer.Write((uint)root.Id);
                writer.Write((uint)elementRecords.Count);
                writer.Write((uint)eventRecords.Count);
                writer.Write((uint)bindingRecords.Count);
                writer.Write((uint)assetRecords.Count);
                writer.Write((uint)strings.Length);
                writer.Write((uint)elementOffset);
                writer.Write((uint)eventOffset);
                writer.Write((uint)bindingOffset);
                writer.Write((uint)assetOffset);
                writer.Write((uint)stringOffset);

                foreach (var record in elementRecords)
                {
                    record.WriteTo(writer);
                }

                foreach (var record in eventRecords)
                {
                    record.WriteTo(writer);
                }

                foreach (var record in bindingRecords)
                {
                    record.WriteTo(writer);
                }

                foreach (var record in assetRecords)
                {
                    record.WriteTo(writer);
                }

                writer.Write(strings);
            }
        }

        private static void RewriteIndex(string indexPath, IReadOnlyList<AssetCopyRecord> assetCopies, string packageAlias)
        {
            var preservedLines = new List<string>();
            if (File.Exists(indexPath))
            {
                preservedLines.AddRange(
                    File.ReadAllLines(indexPath)
                        .Where(line =>
                            !line.StartsWith($"{packageAlias}:", StringComparison.OrdinalIgnoreCase) &&
                            !line.StartsWith($"{packageAlias}_ASSET_", StringComparison.OrdinalIgnoreCase)));
            }

            preservedLines.Add($"{packageAlias}:{packageAlias}\\{packageAlias}.wdexe");
            foreach (var copy in assetCopies
                .Where(item => !string.IsNullOrWhiteSpace(item.RelativePath))
                .OrderBy(item => item.Alias, StringComparer.Ordinal))
            {
                preservedLines.Add($"{copy.Alias}:{copy.RelativePath}");
            }

            File.WriteAllLines(indexPath, preservedLines);
        }

        private sealed record AssetCopyRecord(string SourceKey, string Alias, string? RelativePath);

        private sealed class StringPool
        {
            private readonly Dictionary<string, uint> _offsets = new Dictionary<string, uint>(StringComparer.Ordinal);
            private readonly List<byte> _bytes = new List<byte> { 0 };

            public uint Add(string value)
            {
                if (_offsets.TryGetValue(value, out var offset))
                {
                    return offset;
                }

                offset = (uint)_bytes.Count;
                _offsets[value] = offset;
                _bytes.AddRange(Encoding.UTF8.GetBytes(value));
                _bytes.Add(0);
                return offset;
            }

            public byte[] ToArray()
            {
                return _bytes.ToArray();
            }
        }

        private sealed class ElementRecord
        {
            public const int Size = 128;

            public uint Id { get; init; }
            public int ParentId { get; init; }
            public uint Type { get; init; }
            public uint Flags { get; init; }
            public int X { get; init; }
            public int Y { get; init; }
            public int Width { get; init; }
            public int Height { get; init; }
            public int AnchorMinXPercent { get; init; }
            public int AnchorMinYPercent { get; init; }
            public int AnchorMaxXPercent { get; init; }
            public int AnchorMaxYPercent { get; init; }
            public int TranslateX { get; init; }
            public int TranslateY { get; init; }
            public int ScaleXPercent { get; init; }
            public int ScaleYPercent { get; init; }
            public int PivotXPercent { get; init; }
            public int PivotYPercent { get; init; }
            public int RotationDegrees { get; init; }
            public int OpacityPercent { get; init; }
            public int GridColumns { get; init; }
            public int GridSpacingX { get; init; }
            public int GridSpacingY { get; init; }
            public int ZIndex { get; init; }
            public uint BackgroundColor { get; init; }
            public uint ForegroundColor { get; init; }
            public uint BorderColor { get; init; }
            public int Value { get; init; }
            public int MinValue { get; init; }
            public int MaxValue { get; init; }
            public uint TextOffset { get; init; }
            public uint AssetIndex { get; init; }

            public void WriteTo(BinaryWriter writer)
            {
                writer.Write(Id);
                writer.Write(ParentId);
                writer.Write(Type);
                writer.Write(Flags);
                writer.Write(X);
                writer.Write(Y);
                writer.Write(Width);
                writer.Write(Height);
                writer.Write(AnchorMinXPercent);
                writer.Write(AnchorMinYPercent);
                writer.Write(AnchorMaxXPercent);
                writer.Write(AnchorMaxYPercent);
                writer.Write(TranslateX);
                writer.Write(TranslateY);
                writer.Write(ScaleXPercent);
                writer.Write(ScaleYPercent);
                writer.Write(PivotXPercent);
                writer.Write(PivotYPercent);
                writer.Write(RotationDegrees);
                writer.Write(OpacityPercent);
                writer.Write(GridColumns);
                writer.Write(GridSpacingX);
                writer.Write(GridSpacingY);
                writer.Write(ZIndex);
                writer.Write(BackgroundColor);
                writer.Write(ForegroundColor);
                writer.Write(BorderColor);
                writer.Write(Value);
                writer.Write(MinValue);
                writer.Write(MaxValue);
                writer.Write(TextOffset);
                writer.Write(AssetIndex);
            }
        }

        private sealed class EventRecord
        {
            public const int Size = 16;

            public uint SourceElementId { get; init; }
            public uint EventType { get; init; }
            public uint FirstBindingIndex { get; init; }
            public uint BindingCount { get; init; }

            public void WriteTo(BinaryWriter writer)
            {
                writer.Write(SourceElementId);
                writer.Write(EventType);
                writer.Write(FirstBindingIndex);
                writer.Write(BindingCount);
            }
        }

        private sealed class BindingRecord
        {
            public const int Size = 24;

            public uint ActionType { get; init; }
            public uint TargetElementId { get; init; }
            public uint PropertyType { get; init; }
            public int IntValue { get; init; }
            public uint ColorValue { get; init; }
            public uint StringOffset { get; init; }

            public void WriteTo(BinaryWriter writer)
            {
                writer.Write(ActionType);
                writer.Write(TargetElementId);
                writer.Write(PropertyType);
                writer.Write(IntValue);
                writer.Write(ColorValue);
                writer.Write(StringOffset);
            }
        }

        private sealed class AssetRecord
        {
            public const int Size = 4;

            public uint AliasOffset { get; init; }

            public void WriteTo(BinaryWriter writer)
            {
                writer.Write(AliasOffset);
            }
        }
    }

    public static class WdexeImporter
    {
        private const uint InvalidOffset = 0xFFFFFFFF;
        private const uint InvalidAssetIndex = 0xFFFFFFFF;
        private const int HeaderSize = 60;
        private const int ElementRecordSizeV1 = 68;
        private const int ElementRecordSizeV2 = 100;
        private const int ElementRecordSizeV3 = 128;
        private const int EventRecordSize = 16;
        private const int BindingRecordSize = 24;
        private const int AssetRecordSize = 4;

        public static AppDocument ImportDocument(string packagePath, string? solutionRoot = null)
        {
            if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
            {
                throw new FileNotFoundException("The selected .wdexe package could not be found.", packagePath);
            }

            using var stream = File.OpenRead(packagePath);
            using var reader = new BinaryReader(stream, Encoding.UTF8, false);

            var magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
            var version = reader.ReadUInt16();
            var headerSize = reader.ReadUInt16();
            var canvasWidth = reader.ReadUInt32();
            var canvasHeight = reader.ReadUInt32();
            var rootElementId = reader.ReadUInt32();
            var elementCount = reader.ReadUInt32();
            var eventCount = reader.ReadUInt32();
            var bindingCount = reader.ReadUInt32();
            var assetCount = reader.ReadUInt32();
            var stringTableSize = reader.ReadUInt32();
            var elementOffset = reader.ReadUInt32();
            var eventOffset = reader.ReadUInt32();
            var bindingOffset = reader.ReadUInt32();
            var assetOffset = reader.ReadUInt32();
            var stringOffset = reader.ReadUInt32();

            if (magic != "WDXE" || (version != 1 && version != 2 && version != 3) || headerSize < HeaderSize)
            {
                throw new InvalidDataException("This file is not a supported WinOS .wdexe package.");
            }

            var elementRecordSize = version switch
            {
                >= 3 => ElementRecordSizeV3,
                2 => ElementRecordSizeV2,
                _ => ElementRecordSizeV1,
            };

            if (!SectionFits(elementOffset, checked((uint)(elementCount * elementRecordSize)), stream.Length) ||
                !SectionFits(eventOffset, checked((uint)(eventCount * EventRecordSize)), stream.Length) ||
                !SectionFits(bindingOffset, checked((uint)(bindingCount * BindingRecordSize)), stream.Length) ||
                !SectionFits(assetOffset, checked((uint)(assetCount * AssetRecordSize)), stream.Length) ||
                !SectionFits(stringOffset, stringTableSize, stream.Length))
            {
                throw new InvalidDataException("The .wdexe package is truncated or has invalid section offsets.");
            }

            var packageAlias = WdexeExporter.NormalizePackageAlias(Path.GetFileNameWithoutExtension(packagePath));
            var document = new AppDocument
            {
                Name = packageAlias,
                PackageAlias = packageAlias,
                CanvasWidth = (int)canvasWidth,
                CanvasHeight = (int)canvasHeight,
            };

            var assetAliases = new List<string>((int)assetCount);
            reader.BaseStream.Position = assetOffset;
            for (var assetIndex = 0; assetIndex < assetCount; assetIndex++)
            {
                var aliasOffset = reader.ReadUInt32();
                assetAliases.Add(ReadString(reader, stringOffset, stringTableSize, aliasOffset));
            }

            var elementsById = new Dictionary<int, AppElementDefinition>();
            for (var elementIndex = 0; elementIndex < elementCount; elementIndex++)
            {
                reader.BaseStream.Position = elementOffset + (elementIndex * elementRecordSize);
                var element = new AppElementDefinition
                {
                    Id = (int)reader.ReadUInt32(),
                    ParentId = reader.ReadInt32(),
                    Type = (AppElementType)reader.ReadUInt32(),
                    Flags = (AppElementFlags)reader.ReadUInt32(),
                    X = reader.ReadInt32(),
                    Y = reader.ReadInt32(),
                    Width = reader.ReadInt32(),
                    Height = reader.ReadInt32(),
                };

                if (version >= 3)
                {
                    element.AnchorMinXPercent = reader.ReadInt32();
                    element.AnchorMinYPercent = reader.ReadInt32();
                    element.AnchorMaxXPercent = reader.ReadInt32();
                    element.AnchorMaxYPercent = reader.ReadInt32();
                    element.TranslateX = reader.ReadInt32();
                    element.TranslateY = reader.ReadInt32();
                    element.ScaleXPercent = reader.ReadInt32();
                    element.ScaleYPercent = reader.ReadInt32();
                    element.PivotXPercent = reader.ReadInt32();
                    element.PivotYPercent = reader.ReadInt32();
                    element.RotationDegrees = reader.ReadInt32();
                    element.OpacityPercent = reader.ReadInt32();
                    element.GridColumns = reader.ReadInt32();
                    element.GridSpacingX = reader.ReadInt32();
                    element.GridSpacingY = reader.ReadInt32();
                }
                else if (version >= 2)
                {
                    element.TranslateX = reader.ReadInt32();
                    element.TranslateY = reader.ReadInt32();
                    element.ScaleXPercent = reader.ReadInt32();
                    element.ScaleYPercent = reader.ReadInt32();
                    element.PivotXPercent = reader.ReadInt32();
                    element.PivotYPercent = reader.ReadInt32();
                    element.RotationDegrees = reader.ReadInt32();
                    element.OpacityPercent = reader.ReadInt32();
                }

                element.ZIndex = reader.ReadInt32();
                element.BackgroundColor = reader.ReadUInt32();
                element.ForegroundColor = reader.ReadUInt32();
                element.BorderColor = reader.ReadUInt32();
                element.Value = reader.ReadInt32();
                element.MinValue = reader.ReadInt32();
                element.MaxValue = reader.ReadInt32();

                var textOffset = reader.ReadUInt32();
                var assetIndex = reader.ReadUInt32();
                element.Text = ReadString(reader, stringOffset, stringTableSize, textOffset);
                if (assetIndex != InvalidAssetIndex && assetIndex < assetAliases.Count)
                {
                    element.AssetPath = ResolveAssetReference(packagePath, assetAliases[(int)assetIndex], solutionRoot) ?? string.Empty;
                }

                document.Elements.Add(element);
                elementsById[element.Id] = element;

                if ((uint)element.Id == rootElementId && !string.IsNullOrWhiteSpace(element.Text))
                {
                    document.Name = element.Text;
                }
            }

            var bindingRecords = new List<ImportedBinding>((int)bindingCount);
            reader.BaseStream.Position = bindingOffset;
            for (var bindingIndex = 0; bindingIndex < bindingCount; bindingIndex++)
            {
                var actionType = (AppActionType)reader.ReadUInt32();
                var targetElementId = (int)reader.ReadUInt32();
                var propertyType = (AppPropertyType)reader.ReadUInt32();
                var intValue = reader.ReadInt32();
                var colorValue = reader.ReadUInt32();
                var stringValueOffset = reader.ReadUInt32();
                bindingRecords.Add(new ImportedBinding(
                    actionType,
                    targetElementId,
                    propertyType,
                    intValue,
                    colorValue,
                    ReadString(reader, stringOffset, stringTableSize, stringValueOffset)));
            }

            reader.BaseStream.Position = eventOffset;
            for (var eventIndex = 0; eventIndex < eventCount; eventIndex++)
            {
                var sourceElementId = (int)reader.ReadUInt32();
                var eventType = (AppEventType)reader.ReadUInt32();
                var firstBindingIndex = (int)reader.ReadUInt32();
                var localBindingCount = (int)reader.ReadUInt32();

                if (!elementsById.TryGetValue(sourceElementId, out var sourceElement))
                {
                    continue;
                }

                var appEvent = new AppEventDefinition { EventType = eventType };
                for (var offset = 0; offset < localBindingCount; offset++)
                {
                    var index = firstBindingIndex + offset;
                    if (index < 0 || index >= bindingRecords.Count)
                    {
                        break;
                    }

                    var imported = bindingRecords[index];
                    appEvent.Bindings.Add(new AppBindingDefinition
                    {
                        ActionType = imported.ActionType,
                        TargetElementId = imported.TargetElementId,
                        PropertyType = imported.PropertyType,
                        IntValue = imported.IntValue,
                        ColorValue = imported.ColorValue,
                        StringValue = imported.StringValue,
                    });
                }

                sourceElement.Events.Add(appEvent);
            }

            return document;
        }

        private static bool SectionFits(uint offset, uint size, long streamLength)
        {
            if (offset > streamLength)
            {
                return false;
            }

            return size <= (streamLength - offset);
        }

        private static string ReadString(BinaryReader reader, uint stringOffset, uint stringTableSize, uint relativeOffset)
        {
            if (relativeOffset == InvalidOffset)
            {
                return string.Empty;
            }

            if (relativeOffset >= stringTableSize)
            {
                return string.Empty;
            }

            var absoluteOffset = stringOffset + relativeOffset;
            if (absoluteOffset >= reader.BaseStream.Length)
            {
                return string.Empty;
            }

            var originalPosition = reader.BaseStream.Position;
            reader.BaseStream.Position = absoluteOffset;
            var bytes = new List<byte>();
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                var value = reader.ReadByte();
                if (value == 0)
                {
                    break;
                }

                bytes.Add(value);
            }

            reader.BaseStream.Position = originalPosition;
            return bytes.Count == 0 ? string.Empty : Encoding.UTF8.GetString(bytes.ToArray());
        }

        private static string? ResolveAssetReference(string packagePath, string assetAlias, string? solutionRoot)
        {
            var packageDirectory = Path.GetDirectoryName(packagePath);
            if (!string.IsNullOrWhiteSpace(packageDirectory))
            {
                var assetDirectory = Path.Combine(packageDirectory, "Assets");
                if (Directory.Exists(assetDirectory))
                {
                    var directMatch = Directory.GetFiles(assetDirectory, $"{assetAlias}.*").FirstOrDefault();
                    if (directMatch != null)
                    {
                        return directMatch;
                    }
                }
            }

            return AssetReferenceResolver.ResolveIndexedAliasPath(assetAlias, solutionRoot) != null
                ? assetAlias
                : null;
        }

        private sealed record ImportedBinding(
            AppActionType ActionType,
            int TargetElementId,
            AppPropertyType PropertyType,
            int IntValue,
            uint ColorValue,
            string StringValue);
    }
}
