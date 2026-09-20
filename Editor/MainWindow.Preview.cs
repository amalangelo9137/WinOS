using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace WinOS_Editor
{
    public partial class MainWindow
    {
        // The preview path mirrors the kernel's layout rules closely enough to make
        // authoring transforms feel predictable before boot-testing.
        /// <summary>
        /// Rebuilds the WPF preview canvas from the current document model.
        /// </summary>
        private void RenderPreview()
        {
            if (!IsInitialized)
            {
                return;
            }

            PreviewCanvas.Width = Document.CanvasWidth;
            PreviewCanvas.Height = Document.CanvasHeight;
            StopPreviewAnimations();
            PreviewCanvas.Children.Clear();

            foreach (var element in Document.GetOrderedElements().Where(item => item.IsVisible))
            {
                var layout = AppTransformMath.ResolveElementLayout(Document, element);
                var visual = CreateVisual(element, layout);
                if (visual == null)
                {
                    continue;
                }

                System.Windows.Controls.Canvas.SetLeft(visual, layout.X);
                System.Windows.Controls.Canvas.SetTop(visual, layout.Y);
                PreviewCanvas.Children.Add(visual);
            }

            if (SelectedElement != null)
            {
                var selection = AppTransformMath.ResolveElementLayout(Document, SelectedElement);
                var highlight = new Rectangle
                {
                    Width = selection.Width,
                    Height = selection.Height,
                    Stroke = new SolidColorBrush(Color.FromRgb(97, 196, 139)),
                    StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection(new[] { 6.0, 4.0 }),
                    Fill = Brushes.Transparent,
                    IsHitTestVisible = false,
                    RenderTransformOrigin = new Point(selection.PivotXPercent / 100.0, selection.PivotYPercent / 100.0),
                    RenderTransform = selection.RotationDegrees != 0 ? new RotateTransform(selection.RotationDegrees) : Transform.Identity,
                    Opacity = Math.Max(0.2, selection.OpacityPercent / 100.0),
                };

                System.Windows.Controls.Canvas.SetLeft(highlight, selection.X);
                System.Windows.Controls.Canvas.SetTop(highlight, selection.Y);
                PreviewCanvas.Children.Add(highlight);
            }
        }

        /// <summary>
        /// Creates the WPF visual that corresponds to a single runtime element.
        /// </summary>
        private FrameworkElement? CreateVisual(AppElementDefinition element, AppResolvedLayout layout)
        {
            FrameworkElement? visual = element.Type switch
            {
                AppElementType.RootWindow => CreateBorderVisual(element, layout, 12),
                AppElementType.Panel => CreateBorderVisual(element, layout, element.IsDraggableRegion ? 10 : 6),
                AppElementType.Canvas => CreateCanvasVisual(element, layout, false),
                AppElementType.CanvasGroup => CreateCanvasVisual(element, layout, true),
                AppElementType.GridLayoutGroup => CreateGridLayoutGroupVisual(element, layout),
                AppElementType.Label => CreateLabelVisual(element, layout),
                AppElementType.Button => CreateButtonVisual(element, layout),
                AppElementType.Slider => CreateSliderVisual(element, layout),
                AppElementType.TextField => CreateTextFieldVisual(element, layout),
                AppElementType.Toggle => CreateToggleVisual(element, layout),
                AppElementType.Dropdown => CreateDropdownVisual(element, layout),
                AppElementType.Image => CreateImageVisual(element, layout),
                _ => null
            };

            if (visual == null)
            {
                return null;
            }

            visual.Opacity = layout.OpacityPercent / 100.0;
            visual.RenderTransformOrigin = new Point(layout.PivotXPercent / 100.0, layout.PivotYPercent / 100.0);
            visual.RenderTransform = layout.RotationDegrees != 0 ? new RotateTransform(layout.RotationDegrees) : Transform.Identity;
            return visual;
        }

        /// <summary>
        /// Draws panel-like elements that have background, border, and rounded corners.
        /// </summary>
        private static FrameworkElement CreateBorderVisual(AppElementDefinition element, AppResolvedLayout layout, double cornerRadius)
        {
            return new System.Windows.Controls.Border
            {
                Width = layout.Width,
                Height = layout.Height,
                Background = new SolidColorBrush(ToColor(element.BackgroundColor)),
                BorderBrush = new SolidColorBrush(ToColor(element.BorderColor)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(cornerRadius),
            };
        }

        /// <summary>
        /// Draws canvas containers as lightweight labeled authoring surfaces.
        /// </summary>
        private static FrameworkElement CreateCanvasVisual(AppElementDefinition element, AppResolvedLayout layout, bool isGroup)
        {
            var border = new System.Windows.Controls.Border
            {
                Width = layout.Width,
                Height = layout.Height,
                Background = new SolidColorBrush(ToColor(element.BackgroundColor)),
                BorderBrush = new SolidColorBrush(ToColor(element.BorderColor)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8),
            };

            border.Child = new System.Windows.Controls.TextBlock
            {
                Text = string.IsNullOrWhiteSpace(element.Text) ? (isGroup ? "Canvas Group" : "Canvas") : element.Text,
                Foreground = new SolidColorBrush(ToColor(element.ForegroundColor)),
                FontStyle = FontStyles.Italic,
                Opacity = 0.85,
            };

            return border;
        }

        /// <summary>
        /// Draws a grid layout group, including a preview sample for dynamic running-app grids.
        /// </summary>
        private static FrameworkElement CreateGridLayoutGroupVisual(AppElementDefinition element, AppResolvedLayout layout)
        {
            var border = new System.Windows.Controls.Border
            {
                Width = layout.Width,
                Height = layout.Height,
                Background = new SolidColorBrush(ToColor(element.BackgroundColor)),
                BorderBrush = new SolidColorBrush(ToColor(element.BorderColor)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8),
            };

            if (string.Equals(element.Text, "RUNNING_APPS", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(element.Text, "RUNNING_APP_TITLES", StringComparison.OrdinalIgnoreCase))
            {
                border.Child = new System.Windows.Controls.Border
                {
                    Width = Math.Max(48, element.MinValue > 0 ? element.MinValue : 132),
                    Height = Math.Max(24, element.MaxValue > 0 ? element.MaxValue : 32),
                    Background = new SolidColorBrush(ToColor(element.BackgroundColor)),
                    BorderBrush = new SolidColorBrush(ToColor(element.BorderColor)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Child = new System.Windows.Controls.TextBlock
                    {
                        Text = "WinOS Boot App",
                        Foreground = new SolidColorBrush(ToColor(element.ForegroundColor)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextAlignment = TextAlignment.Center,
                    },
                };
                return border;
            }

            border.Child = new System.Windows.Controls.TextBlock
            {
                Text = string.IsNullOrWhiteSpace(element.Text) ? "Grid Layout Group" : element.Text,
                Foreground = new SolidColorBrush(ToColor(element.ForegroundColor)),
                FontStyle = FontStyles.Italic,
                Opacity = 0.85,
            };

            return border;
        }

        private static FrameworkElement CreateLabelVisual(AppElementDefinition element, AppResolvedLayout layout)
        {
            return new System.Windows.Controls.Border
            {
                Width = layout.Width,
                Height = layout.Height,
                Background = Brushes.Transparent,
                Child = new System.Windows.Controls.TextBlock
                {
                    Text = element.Text,
                    Foreground = new SolidColorBrush(ToColor(element.ForegroundColor)),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                },
            };
        }

        private static FrameworkElement CreateButtonVisual(AppElementDefinition element, AppResolvedLayout layout)
        {
            return new System.Windows.Controls.Border
            {
                Width = layout.Width,
                Height = layout.Height,
                Background = new SolidColorBrush(ToColor(element.BackgroundColor)),
                BorderBrush = new SolidColorBrush(ToColor(element.BorderColor)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Child = new System.Windows.Controls.TextBlock
                {
                    Text = element.Text,
                    Foreground = new SolidColorBrush(ToColor(element.ForegroundColor)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                },
            };
        }

        private static FrameworkElement CreateSliderVisual(AppElementDefinition element, AppResolvedLayout layout)
        {
            var denominator = Math.Max(1, element.MaxValue - element.MinValue);
            var normalized = Math.Clamp((double)(element.Value - element.MinValue) / denominator, 0.0, 1.0);

            var track = new System.Windows.Controls.Border
            {
                Width = layout.Width,
                Height = layout.Height,
                Background = new SolidColorBrush(ToColor(element.BackgroundColor)),
                BorderBrush = new SolidColorBrush(ToColor(element.BorderColor)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
            };

            var fill = new System.Windows.Controls.Border
            {
                Width = Math.Max(18, layout.Width * normalized),
                Height = Math.Max(8, layout.Height - 10),
                Margin = new Thickness(5),
                Background = new SolidColorBrush(ToColor(element.ForegroundColor)),
                CornerRadius = new CornerRadius(10),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };

            track.Child = fill;
            return track;
        }

        private static FrameworkElement CreateTextFieldVisual(AppElementDefinition element, AppResolvedLayout layout)
        {
            return new System.Windows.Controls.Border
            {
                Width = layout.Width,
                Height = layout.Height,
                Background = new SolidColorBrush(ToColor(element.BackgroundColor)),
                BorderBrush = new SolidColorBrush(ToColor(element.BorderColor)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Child = new System.Windows.Controls.TextBlock
                {
                    Text = element.Text,
                    Foreground = new SolidColorBrush(ToColor(element.ForegroundColor)),
                    Margin = new Thickness(10, 7, 10, 7),
                    TextWrapping = TextWrapping.NoWrap,
                },
            };
        }

        private static FrameworkElement CreateToggleVisual(AppElementDefinition element, AppResolvedLayout layout)
        {
            var enabled = element.Value != 0;
            var outer = new System.Windows.Controls.Border
            {
                Width = layout.Width,
                Height = layout.Height,
                Background = new SolidColorBrush(ToColor(element.BackgroundColor)),
                BorderBrush = new SolidColorBrush(ToColor(element.BorderColor)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 6, 8, 6),
            };

            var row = new System.Windows.Controls.Grid();
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(42) });
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var track = new System.Windows.Controls.Border
            {
                Width = 34,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(ToColor(enabled ? element.ForegroundColor : AdjustPreviewColor(element.BorderColor, -30))),
                VerticalAlignment = VerticalAlignment.Center,
            };

            var knob = new Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = Brushes.White,
                HorizontalAlignment = enabled ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(3, 0, 3, 0),
            };
            track.Child = knob;

            var label = new System.Windows.Controls.TextBlock
            {
                Text = string.IsNullOrWhiteSpace(element.Text) ? "Toggle" : element.Text,
                Foreground = new SolidColorBrush(ToColor(element.ForegroundColor)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
            };

            System.Windows.Controls.Grid.SetColumn(track, 0);
            System.Windows.Controls.Grid.SetColumn(label, 1);
            row.Children.Add(track);
            row.Children.Add(label);
            outer.Child = row;
            return outer;
        }

        private static FrameworkElement CreateDropdownVisual(AppElementDefinition element, AppResolvedLayout layout)
        {
            var selectedText = GetDropdownSelectedText(element);
            var outer = new System.Windows.Controls.Border
            {
                Width = layout.Width,
                Height = layout.Height,
                Background = new SolidColorBrush(ToColor(element.BackgroundColor)),
                BorderBrush = new SolidColorBrush(ToColor(element.BorderColor)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6, 10, 6),
            };

            var row = new System.Windows.Controls.Grid();
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });

            var label = new System.Windows.Controls.TextBlock
            {
                Text = selectedText,
                Foreground = new SolidColorBrush(ToColor(element.ForegroundColor)),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            var arrow = new System.Windows.Controls.TextBlock
            {
                Text = "v",
                Foreground = new SolidColorBrush(ToColor(element.ForegroundColor)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                FontWeight = FontWeights.Bold,
            };

            System.Windows.Controls.Grid.SetColumn(label, 0);
            System.Windows.Controls.Grid.SetColumn(arrow, 1);
            row.Children.Add(label);
            row.Children.Add(arrow);
            outer.Child = row;
            return outer;
        }

        private FrameworkElement CreateImageVisual(AppElementDefinition element, AppResolvedLayout layout)
        {
            var container = new System.Windows.Controls.Border
            {
                Width = layout.Width,
                Height = layout.Height,
                Background = new SolidColorBrush(ToColor(element.BackgroundColor)),
                BorderBrush = new SolidColorBrush(ToColor(element.BorderColor)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
            };

            var resolvedPath = AssetReferenceResolver.ResolveAssetPath(element.AssetPath, _solutionRoot);
            if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
            {
                container.Child = new System.Windows.Controls.TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(element.AssetPath) ? "Missing asset" : $"Missing asset\n{element.AssetPath}",
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                };
                return container;
            }

            var image = new System.Windows.Controls.Image
            {
                Stretch = Stretch.UniformToFill,
            };

            if (string.Equals(System.IO.Path.GetExtension(resolvedPath), ".gif", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryAttachGifPreview(image, resolvedPath))
                {
                    image.Source = new BitmapImage(new Uri(resolvedPath));
                }
            }
            else
            {
                image.Source = new BitmapImage(new Uri(resolvedPath));
            }

            container.Child = image;
            return container;
        }

        private void StopPreviewAnimations()
        {
            foreach (var timer in _previewTimers)
            {
                timer.Stop();
            }

            _previewTimers.Clear();
        }

        private bool TryAttachGifPreview(System.Windows.Controls.Image image, string assetPath)
        {
            try
            {
                using var stream = new FileStream(assetPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count == 0)
                {
                    return false;
                }

                var frames = new List<BitmapSource>(decoder.Frames.Count);
                var delays = new List<TimeSpan>(decoder.Frames.Count);

                foreach (var frame in decoder.Frames)
                {
                    frame.Freeze();
                    frames.Add(frame);
                    delays.Add(ReadGifDelay(frame));
                }

                image.Source = frames[0];
                if (frames.Count == 1)
                {
                    return true;
                }

                var frameIndex = 0;
                var timer = new DispatcherTimer
                {
                    Interval = delays[0],
                };
                timer.Tick += (_, _) =>
                {
                    frameIndex = (frameIndex + 1) % frames.Count;
                    image.Source = frames[frameIndex];
                    timer.Interval = delays[frameIndex];
                };
                _previewTimers.Add(timer);
                timer.Start();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static TimeSpan ReadGifDelay(BitmapFrame frame)
        {
            try
            {
                if (frame.Metadata is BitmapMetadata metadata)
                {
                    var delayValue = metadata.GetQuery("/grctlext/Delay");
                    if (delayValue is ushort shortDelay && shortDelay > 0)
                    {
                        return TimeSpan.FromMilliseconds(shortDelay * 10);
                    }

                    if (delayValue is byte byteDelay && byteDelay > 0)
                    {
                        return TimeSpan.FromMilliseconds(byteDelay * 10);
                    }
                }
            }
            catch
            {
            }

            return TimeSpan.FromMilliseconds(100);
        }

        private static Color ToColor(uint value)
        {
            return Color.FromRgb(
                (byte)((value >> 16) & 0xFF),
                (byte)((value >> 8) & 0xFF),
                (byte)(value & 0xFF));
        }

        private static uint AdjustPreviewColor(uint color, int delta)
        {
            var red = Math.Clamp(((int)((color >> 16) & 0xFF)) + delta, 0, 255);
            var green = Math.Clamp(((int)((color >> 8) & 0xFF)) + delta, 0, 255);
            var blue = Math.Clamp(((int)(color & 0xFF)) + delta, 0, 255);
            return ((uint)red << 16) | ((uint)green << 8) | (uint)blue;
        }

        private static string[] GetDropdownOptions(AppElementDefinition element)
        {
            var raw = element.Text ?? string.Empty;
            var options = raw.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            return options.Length > 0 ? options : new[] { "Option" };
        }

        private static string GetDropdownSelectedText(AppElementDefinition element)
        {
            var options = GetDropdownOptions(element);
            var index = Math.Clamp(element.Value, 0, options.Length - 1);
            return options[index];
        }
    }
}
