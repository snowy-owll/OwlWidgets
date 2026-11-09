using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using OwlWidgets.Hosting;
using OwlWidgets.Settings;
using OwlWidgets.ViewModels;

namespace OwlWidgets
{
    internal partial class MainWindow : Window
    {
        private const int DwmwaUseImmersiveDarkMode = 20;

        private readonly MainViewModel _viewModel;
        private readonly ThemeService _theme;

        public MainWindow(MainViewModel viewModel, ThemeService theme)
        {
            _viewModel = viewModel;
            _theme = theme;
            InitializeComponent();
            DataContext = viewModel;

            SourceInitialized += (_, _) => ApplyTitleBarTheme();
            _theme.Changed += (_, _) => ApplyTitleBarTheme();

            Closing += (_, e) =>
            {
                e.Cancel = true;
                Visibility = Visibility.Hidden;
            };
        }

        /// <summary>The widgets screen on this instance, on its own settings page when it has one.</summary>
        internal void ShowSettings(InstanceSession session) => _viewModel.ShowInstanceSettings(session);

        /// <summary>
        /// The caption is drawn by Windows, so it follows the base theme through DWM, not through resources.
        /// </summary>
        private void ApplyTitleBarTheme()
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
                return;
            var dark = _theme.IsDark ? 1 : 0;
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
