using System.Windows;
using System.Windows.Controls;
using OwlWidgets.Widgets.Weather.ViewModels;

namespace OwlWidgets.Widgets.Weather
{
    /// <summary>
    /// Interaction logic for Content.xaml
    /// </summary>
    public partial class Content : UserControl
    {
        public Content()
        {
            InitializeComponent();
        }

        private void OnAttributionClick(object sender, RoutedEventArgs e) =>
            (DataContext as ContentViewModel)?.OpenAttribution();
    }
}
