using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using TrayApp.ViewModels;

namespace TrayApp.Views
{
    public partial class MainWindow : Window
    {
        private const double MinCollapsedWidth = 420;

        private MainWindowViewModel? _viewModel;
        private bool _isApplyingWindowSize;
        private bool _initialSizingApplied;
        private double _collapsedWidth;

        public MainWindow()
        {
            InitializeComponent();

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            DataContextChanged += OnDataContextChanged;
            SizeChanged += OnSizeChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_initialSizingApplied || WindowState != WindowState.Normal)
                return;

            _collapsedWidth = Math.Max(GetWindowWidthResource(), MinCollapsedWidth);
            ApplyHistoryPaneSizing(_viewModel?.IsHistoryPaneExpanded == true, forceCollapsedWidth: true);
            _initialSizingApplied = true;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null)
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is MainWindowViewModel oldVm)
                oldVm.PropertyChanged -= OnViewModelPropertyChanged;

            _viewModel = e.NewValue as MainWindowViewModel;
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;

                if (IsLoaded)
                    ApplyHistoryPaneSizing(_viewModel.IsHistoryPaneExpanded, forceCollapsedWidth: false);
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsHistoryPaneExpanded))
            {
                Dispatcher.Invoke(() =>
                {
                    ApplyHistoryPaneSizing(_viewModel?.IsHistoryPaneExpanded == true, forceCollapsedWidth: false);
                });
                return;
            }

            if (e.PropertyName == nameof(MainWindowViewModel.IsLoading) && _viewModel?.IsLoading == false)
            {
                Dispatcher.InvokeAsync(() => InputBox?.Focus(), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_isApplyingWindowSize || WindowState != WindowState.Normal)
                return;

            var sidebarWidth = GetSidebarWidthResource();
            if (_viewModel?.IsHistoryPaneExpanded == true)
                _collapsedWidth = Math.Max(Width - sidebarWidth, MinCollapsedWidth);
            else
                _collapsedWidth = Math.Max(Width, MinCollapsedWidth);
        }

        private void ApplyHistoryPaneSizing(bool expanded, bool forceCollapsedWidth)
        {
            if (WindowState != WindowState.Normal)
                return;

            var sidebarWidth = GetSidebarWidthResource();
            if (forceCollapsedWidth || _collapsedWidth <= 0)
                _collapsedWidth = Math.Max(GetWindowWidthResource(), MinCollapsedWidth);

            var rightEdge = Left + Width;
            var targetWidth = expanded ? _collapsedWidth + sidebarWidth : _collapsedWidth;

            if (Math.Abs(targetWidth - Width) < 0.5)
                return;

            _isApplyingWindowSize = true;
            try
            {
                Width = targetWidth;
                Left = rightEdge - targetWidth;
            }
            finally
            {
                _isApplyingWindowSize = false;
            }
        }

        public void ActivateAndFocusInput()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            Focus();

            Dispatcher.InvokeAsync(() =>
            {
                InputBox?.Focus();
                InputBox?.Select(InputBox.Text?.Length ?? 0, 0);
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private double GetSidebarWidthResource()
        {
            if (TryFindResource("Size.Sidebar.Width") is double width)
                return width;

            return 220;
        }

        private double GetWindowWidthResource()
        {
            if (TryFindResource("Size.Window.Main.Width") is double width)
                return width;

            return 500;
        }

        private void DragRegion_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
                return;

            try
            {
                DragMove();
            }
            catch
            {
                // no-op: DragMove can throw if mouse state changes mid-gesture
            }
        }
    }
}
