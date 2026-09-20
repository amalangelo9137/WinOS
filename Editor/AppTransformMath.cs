using System;

namespace WinOS_Editor
{
    // Centralized layout/transform resolution keeps the editor preview and export
    // model aligned with the kernel's element placement rules.
    public readonly record struct AppResolvedLayout(
        int X,
        int Y,
        int Width,
        int Height,
        int PivotXPercent,
        int PivotYPercent,
        int RotationDegrees,
        int OpacityPercent);

    public static class AppTransformMath
    {
        /// <summary>
        /// Resolves an element's authored bounds into preview/runtime canvas coordinates.
        /// </summary>
        public static AppResolvedLayout ResolveElementLayout(AppDocument document, AppElementDefinition element)
        {
            if (TryResolveGridChildLayout(document, element, out var gridLayout))
            {
                return gridLayout;
            }

            if (element.ParentId >= 0)
            {
                var parent = document.FindElement(element.ParentId);
                if (parent != null)
                {
                    var parentLayout = ResolveElementLayout(document, parent);
                    return BuildLayoutFromParent(parentLayout, element);
                }
            }

            var rootWidth = document.CanvasWidth;
            var rootHeight = document.CanvasHeight;

            if (element.Type == AppElementType.RootWindow)
            {
                return new AppResolvedLayout(
                    0,
                    0,
                    rootWidth,
                    rootHeight,
                    Math.Clamp(element.PivotXPercent, 0, 100),
                    Math.Clamp(element.PivotYPercent, 0, 100),
                    element.RotationDegrees,
                    Math.Clamp(element.OpacityPercent, 0, 100));
            }

            return BuildLayout(
                ResolveLayoutValue(element.X, rootWidth, false),
                ResolveLayoutValue(element.Y, rootHeight, false),
                ResolveLayoutValue(element.Width, rootWidth, true),
                ResolveLayoutValue(element.Height, rootHeight, true),
                element,
                100);
        }

        /// <summary>
        /// Resolves a child element against its parent's anchors before applying transform properties.
        /// </summary>
        private static AppResolvedLayout BuildLayoutFromParent(AppResolvedLayout parentLayout, AppElementDefinition element)
        {
            var anchorLeft = parentLayout.X + ((parentLayout.Width * element.AnchorMinXPercent) / 100);
            var anchorRight = parentLayout.X + ((parentLayout.Width * element.AnchorMaxXPercent) / 100);
            var anchorTop = parentLayout.Y + ((parentLayout.Height * element.AnchorMinYPercent) / 100);
            var anchorBottom = parentLayout.Y + ((parentLayout.Height * element.AnchorMaxYPercent) / 100);

            var resolvedLeft = anchorLeft + ResolveLayoutValue(element.X, parentLayout.Width, false);
            var resolvedTop = anchorTop + ResolveLayoutValue(element.Y, parentLayout.Height, false);
            var resolvedWidth = element.AnchorMinXPercent == element.AnchorMaxXPercent
                ? ResolveLayoutValue(element.Width, parentLayout.Width, true)
                : Math.Max(1, (anchorRight - ResolveLayoutValue(element.Width, parentLayout.Width, false)) - resolvedLeft);
            var resolvedHeight = element.AnchorMinYPercent == element.AnchorMaxYPercent
                ? ResolveLayoutValue(element.Height, parentLayout.Height, true)
                : Math.Max(1, (anchorBottom - ResolveLayoutValue(element.Height, parentLayout.Height, false)) - resolvedTop);

            if (UsesPivotedAnchor(element))
            {
                resolvedLeft -= (resolvedWidth * Math.Clamp(element.PivotXPercent, 0, 100)) / 100;
                resolvedTop -= (resolvedHeight * Math.Clamp(element.PivotYPercent, 0, 100)) / 100;
            }

            return BuildLayout(
                resolvedLeft,
                resolvedTop,
                resolvedWidth,
                resolvedHeight,
                element,
                parentLayout.OpacityPercent);
        }

        /// <summary>
        /// Applies translation, scale, pivot, rotation metadata, and inherited opacity.
        /// </summary>
        private static AppResolvedLayout BuildLayout(int x, int y, int width, int height, AppElementDefinition element, int parentOpacityPercent)
        {
            x += ResolveLayoutValue(element.TranslateX, width, false);
            y += ResolveLayoutValue(element.TranslateY, height, false);

            var pivotX = Math.Clamp(element.PivotXPercent, 0, 100);
            var pivotY = Math.Clamp(element.PivotYPercent, 0, 100);
            var scaledWidth = Math.Max(1, (width * Math.Max(1, element.ScaleXPercent)) / 100);
            var scaledHeight = Math.Max(1, (height * Math.Max(1, element.ScaleYPercent)) / 100);

            x -= ((scaledWidth - width) * pivotX) / 100;
            y -= ((scaledHeight - height) * pivotY) / 100;

            var opacity = Math.Clamp((parentOpacityPercent * Math.Clamp(element.OpacityPercent, 0, 100)) / 100, 0, 100);

            return new AppResolvedLayout(
                x,
                y,
                scaledWidth,
                scaledHeight,
                pivotX,
                pivotY,
                element.RotationDegrees,
                opacity);
        }

        /// <summary>
        /// Converts an absolute or percent encoded layout value into pixels.
        /// </summary>
        public static int ResolveLayoutValue(int rawValue, int reference, bool clampToPositive)
        {
            var resolved = LayoutValueCodec.IsPercent(rawValue)
                ? (reference * (LayoutValueCodec.PercentBase - rawValue)) / 100
                : rawValue;

            return clampToPositive ? Math.Max(1, resolved) : resolved;
        }

        /// <summary>
        /// Checks whether an anchor preset should treat X/Y as the pivot position instead of top-left.
        /// </summary>
        private static bool UsesPivotedAnchor(AppElementDefinition element)
        {
            if (element.AnchorMinXPercent != element.AnchorMaxXPercent ||
                element.AnchorMinYPercent != element.AnchorMaxYPercent)
            {
                return false;
            }

            return element.AnchorMinXPercent != 0 || element.AnchorMinYPercent != 0;
        }

        /// <summary>
        /// Overrides child placement when the parent is a GridLayoutGroup.
        /// </summary>
        private static bool TryResolveGridChildLayout(AppDocument document, AppElementDefinition element, out AppResolvedLayout layout)
        {
            layout = default;
            if (element.ParentId < 0)
            {
                return false;
            }

            var parent = document.FindElement(element.ParentId);
            if (parent == null || parent.Type != AppElementType.GridLayoutGroup)
            {
                return false;
            }

            var parentLayout = ResolveElementLayout(document, parent);
            var siblings = document.GetOrderedElements()
                .Where(item => item.ParentId == parent.Id && item.Type != AppElementType.RootWindow && item.IsVisible)
                .ToList();
            var childIndex = siblings.FindIndex(item => item.Id == element.Id);
            if (childIndex < 0)
            {
                return false;
            }

            var cellWidth = ResolveLayoutValue(element.Width, parentLayout.Width, true);
            var cellHeight = ResolveLayoutValue(element.Height, parentLayout.Height, true);
            var spacingX = Math.Max(0, parent.GridSpacingX);
            var spacingY = Math.Max(0, parent.GridSpacingY);
            var columns = parent.GridColumns;
            if (columns <= 0)
            {
                columns = Math.Max(1, (parentLayout.Width + spacingX) / Math.Max(1, cellWidth + spacingX));
            }

            var column = childIndex % columns;
            var row = childIndex / columns;

            layout = BuildLayout(
                parentLayout.X + (column * (cellWidth + spacingX)),
                parentLayout.Y + (row * (cellHeight + spacingY)),
                cellWidth,
                cellHeight,
                element,
                parentLayout.OpacityPercent);
            return true;
        }
    }
}
