namespace YASN.WindowLayout
{
    /// <summary>
    /// Calculates cross-platform quick move and resize geometry.
    /// </summary>
    public static class QuickWindowLayout
    {
        /// <summary>
        /// Calculates a new rectangle for a quick move target.
        /// </summary>
        /// <param name="current">The current window rectangle.</param>
        /// <param name="workingArea">The screen working area.</param>
        /// <param name="target">The target screen region.</param>
        /// <returns>The calculated window rectangle.</returns>
        public static WindowRect Move(WindowRect current, WindowRect workingArea, QuickMoveTarget target)
        {
            return target switch
            {
                QuickMoveTarget.LeftHalf => new WindowRect(workingArea.Left, workingArea.Top, workingArea.Width / 2, workingArea.Height),
                QuickMoveTarget.RightHalf => new WindowRect(workingArea.Left + workingArea.Width / 2, workingArea.Top, workingArea.Width / 2, workingArea.Height),
                QuickMoveTarget.TopHalf => new WindowRect(workingArea.Left, workingArea.Top, workingArea.Width, workingArea.Height / 2),
                QuickMoveTarget.BottomHalf => new WindowRect(workingArea.Left, workingArea.Top + workingArea.Height / 2, workingArea.Width, workingArea.Height / 2),
                QuickMoveTarget.Center => Center(current.Width, current.Height, workingArea),
                QuickMoveTarget.Full => workingArea,
                _ => current
            };
        }

        /// <summary>
        /// Calculates a resized rectangle around the current center while staying inside the working area.
        /// </summary>
        /// <param name="current">The current window rectangle.</param>
        /// <param name="workingArea">The screen working area.</param>
        /// <param name="requestedWidth">The requested width.</param>
        /// <param name="requestedHeight">The requested height.</param>
        /// <param name="minWidth">The minimum width.</param>
        /// <param name="minHeight">The minimum height.</param>
        /// <returns>The calculated resized rectangle.</returns>
        public static WindowRect Resize(
            WindowRect current,
            WindowRect workingArea,
            double requestedWidth,
            double requestedHeight,
            double minWidth,
            double minHeight)
        {
            double width = Math.Min(Math.Max(requestedWidth, minWidth), workingArea.Width);
            double height = Math.Min(Math.Max(requestedHeight, minHeight), workingArea.Height);
            double centerX = current.Left + current.Width / 2;
            double centerY = current.Top + current.Height / 2;
            double left = Clamp(centerX - width / 2, workingArea.Left, workingArea.Left + workingArea.Width - width);
            double top = Clamp(centerY - height / 2, workingArea.Top, workingArea.Top + workingArea.Height - height);

            return new WindowRect(left, top, width, height);
        }

        private static WindowRect Center(double width, double height, WindowRect workingArea)
        {
            double clampedWidth = Math.Min(width, workingArea.Width);
            double clampedHeight = Math.Min(height, workingArea.Height);
            double left = workingArea.Left + (workingArea.Width - clampedWidth) / 2;
            double top = workingArea.Top + (workingArea.Height - clampedHeight) / 2;

            return new WindowRect(left, top, clampedWidth, clampedHeight);
        }

        private static double Clamp(double value, double min, double max)
        {
            if (max < min)
            {
                return min;
            }

            return Math.Min(Math.Max(value, min), max);
        }
    }
}
