using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OwlWidgets.ViewModels;

namespace OwlWidgets.Views
{
    /// <summary>
    /// The view of <see cref="AddWidgetViewModel"/>. Keeps the keyboard on the selected type:
    /// when the dialog opens and when the dialog returns to the first step.
    /// </summary>
    internal partial class AddWidgetDialog : System.Windows.Controls.UserControl
    {
        private AddWidgetViewModel? _viewModel;

        public AddWidgetDialog()
        {
            InitializeComponent();
            Loaded += (_, _) => FocusSelected();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_viewModel != null)
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = e.NewValue as AddWidgetViewModel;
            if (_viewModel != null)
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AddWidgetViewModel.SettingsStep) && _viewModel?.SettingsStep == false)
                Dispatcher.BeginInvoke(FocusSelected);
        }

        private void FocusSelected()
        {
            if (PackageList.ItemContainerGenerator.ContainerFromIndex(PackageList.SelectedIndex) is ListBoxItem item)
                item.Focus();
        }

        /// <summary>Only a double click on a type goes on, not one on the scroll bar or the empty space.</summary>
        private void PackageList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source
                && ItemsControl.ContainerFromElement(PackageList, source) is ListBoxItem
                && _viewModel?.PrimaryCommand.CanExecute(null) == true)
                _viewModel.PrimaryCommand.Execute(null);
        }
    }
}
