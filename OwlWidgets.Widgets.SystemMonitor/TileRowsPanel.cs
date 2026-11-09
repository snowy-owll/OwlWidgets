using System.Windows;
using System.Windows.Controls;

namespace OwlWidgets.Widgets.SystemMonitor
{
    /// <summary>
    /// Children in rows of <see cref="Columns"/> equal columns, each row as high as its highest child and every child
    /// of the row stretched to it. Unlike <see cref="System.Windows.Controls.Primitives.UniformGrid"/>, a tall tile
    /// makes its own row taller, not every row of the panel.
    /// </summary>
    public class TileRowsPanel : Panel
    {
        public static readonly DependencyProperty ColumnsProperty =
            DependencyProperty.Register(nameof(Columns), typeof(int), typeof(TileRowsPanel),
                new FrameworkPropertyMetadata(2, FrameworkPropertyMetadataOptions.AffectsMeasure), v => (int)v > 0);

        public int Columns
        {
            get => (int)GetValue(ColumnsProperty);
            set => SetValue(ColumnsProperty, value);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var width = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : availableSize.Width / Columns;
            double height = 0;
            double widest = 0;
            foreach (var row in Rows())
            {
                double rowHeight = 0;
                foreach (var child in row)
                {
                    child.Measure(new Size(width, double.PositiveInfinity));
                    rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
                    widest = Math.Max(widest, child.DesiredSize.Width);
                }

                height += rowHeight;
            }

            return new Size(double.IsInfinity(width) ? widest * Columns : availableSize.Width, height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var width = finalSize.Width / Columns;
            double top = 0;
            foreach (var row in Rows())
            {
                var rowHeight = row.Max(child => child.DesiredSize.Height);
                for (var i = 0; i < row.Count; i++)
                    row[i].Arrange(new Rect(i * width, top, width, rowHeight));
                top += rowHeight;
            }

            return finalSize;
        }

        private IEnumerable<List<UIElement>> Rows() =>
            InternalChildren.Cast<UIElement>().Chunk(Columns).Select(row => row.ToList());
    }
}
