using System;
using System.Windows;
using System.Windows.Controls;

namespace TrayApp.Behaviors
{
    public static class ScrollViewerBehavior
    {
        public static readonly DependencyProperty MonitorProperty = DependencyProperty.RegisterAttached(
            "Monitor", typeof(bool), typeof(ScrollViewerBehavior), new PropertyMetadata(false, OnMonitorChanged));

        public static void SetMonitor(DependencyObject d, bool value) => d.SetValue(MonitorProperty, value);
        public static bool GetMonitor(DependencyObject d) => (bool)d.GetValue(MonitorProperty);

        public static readonly DependencyProperty IsNearBottomProperty = DependencyProperty.RegisterAttached(
            "IsNearBottom", typeof(bool), typeof(ScrollViewerBehavior), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static void SetIsNearBottom(DependencyObject d, bool value) => d.SetValue(IsNearBottomProperty, value);
        public static bool GetIsNearBottom(DependencyObject d) => (bool)d.GetValue(IsNearBottomProperty);

        public static readonly DependencyProperty ScrollToBottomTriggerProperty = DependencyProperty.RegisterAttached(
            "ScrollToBottomTrigger", typeof(int), typeof(ScrollViewerBehavior), new PropertyMetadata(0, OnScrollToBottomTriggerChanged));

        public static void SetScrollToBottomTrigger(DependencyObject d, int value) => d.SetValue(ScrollToBottomTriggerProperty, value);
        public static int GetScrollToBottomTrigger(DependencyObject d) => (int)d.GetValue(ScrollToBottomTriggerProperty);

        private static void OnMonitorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer sv)
            {
                if ((bool)e.NewValue)
                {
                    sv.ScrollChanged += Sv_ScrollChanged;
                    // initialize
                    UpdateIsNearBottom(sv);
                }
                else
                {
                    sv.ScrollChanged -= Sv_ScrollChanged;
                }
            }
        }

        private static void Sv_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                UpdateIsNearBottom(sv);
            }
        }

        private static void UpdateIsNearBottom(ScrollViewer sv)
        {
            var threshold = 40.0; // pixels
            var isNear = sv.VerticalOffset + sv.ViewportHeight >= sv.ExtentHeight - threshold;
            SetIsNearBottom(sv, isNear);
        }

        private static void OnScrollToBottomTriggerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer sv)
            {
                // scroll to bottom when trigger increments
                sv.Dispatcher.Invoke(() => sv.ScrollToEnd());
            }
        }
    }
}
