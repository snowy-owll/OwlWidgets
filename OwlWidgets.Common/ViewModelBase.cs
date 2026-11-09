using System.ComponentModel;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace OwlWidgets.Common
{
    public class ViewModelBase : INotifyPropertyChanged
    {
        private readonly Dictionary<string, object?> _propertyValues = [];

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected virtual T? GetValue<T>(Expression<Func<T>> property)
        {
            var propertyName = GetPropertyName(property);
            return _propertyValues.TryGetValue(propertyName, out var value)
                ? (T?)value
                : default;
        }

        protected virtual void SetValue<T>(Expression<Func<T>> property, T value)
        {
            var propertyName = GetPropertyName(property);
            var currentValue = GetValue(property);

            if (EqualityComparer<T>.Default.Equals(currentValue, value))
                return;

            _propertyValues[propertyName] = value;
            OnPropertyChanged(propertyName);
        }

        private static string GetPropertyName<T>(Expression<Func<T>> property)
        {
            var memberExpression = (MemberExpression)property.Body;
            return memberExpression.Member.Name;
        }
    }
}
