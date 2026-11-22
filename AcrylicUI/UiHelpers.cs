using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using AcrylicUI.Core;

namespace AcrylicUI
{
    /// <summary>
    /// Attached properties used by the control templates of Themes\Styles.xaml.
    /// </summary>
    public static class Look
    {
        public static readonly DependencyProperty GlyphProperty = DependencyProperty.RegisterAttached("Glyph", typeof(string), typeof(Look), new PropertyMetadata(""));

        public static string GetGlyph(DependencyObject element) { return (string)element.GetValue(GlyphProperty); }
        public static void SetGlyph(DependencyObject element, string value) { element.SetValue(GlyphProperty, value); }

        public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached("Placeholder", typeof(string), typeof(Look), new PropertyMetadata(""));

        public static string GetPlaceholder(DependencyObject element) { return (string)element.GetValue(PlaceholderProperty); }
        public static void SetPlaceholder(DependencyObject element, string value) { element.SetValue(PlaceholderProperty, value); }

        public static readonly DependencyProperty HasErrorProperty = DependencyProperty.RegisterAttached("HasError", typeof(bool), typeof(Look), new PropertyMetadata(false));

        public static bool GetHasError(DependencyObject element) { return (bool)element.GetValue(HasErrorProperty); }
        public static void SetHasError(DependencyObject element, bool value) { element.SetValue(HasErrorProperty, value); }
    }

    public sealed class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is bool b && b) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// Hides an element without giving its room away, so that the layout does not jump.
    /// </summary>
    public sealed class BoolToHiddenConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is bool b && b) ? Visibility.Visible : Visibility.Hidden;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class TextToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// Makes one column of a table take whatever width the other columns leave free, so that a table fills its
    /// room without needing a horizontal scroll bar.
    /// </summary>
    public static class TableLayout
    {
        public static void FillColumn(System.Windows.Controls.ListView list, int columnIndex, double minimumWidth)
        {
            SizeChangedEventHandler update = (sender, e) =>
            {
                var view = list.View as System.Windows.Controls.GridView;

                if (view == null || list.ActualWidth <= 0) return;

                double others = 0;

                for (int i = 0; i < view.Columns.Count; i++)
                {
                    if (i != columnIndex) others += view.Columns[i].ActualWidth > 0 ? view.Columns[i].ActualWidth : view.Columns[i].Width;
                }

                view.Columns[columnIndex].Width = Math.Max(minimumWidth, list.ActualWidth - others - SystemParameters.VerticalScrollBarWidth - 8);
            };

            list.SizeChanged += update;
        }
    }

    public abstract class Observable : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void Notify([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected void NotifyAll()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }
    }

    /// <summary>
    /// What the pages of the main window can ask the main window for.
    /// </summary>
    public interface IShell
    {
        Window Window { get; }

        AcrylicOperations Operations { get; }

        EngineState EngineState { get; }

        event EventHandler EngineStateChanged;

        /// <summary>
        /// Runs a possibly long operation in the background while the window shows what is going on. Returns false,
        /// after having told the user, if the operation failed.
        /// </summary>
        Task<bool> RunAsync(string failureTitle, Action<Action<string>> work);

        void SetStatus(string text);

        /// <summary>Called after a file Acrylic reads only on startup has been saved.</summary>
        void NotifyFileSaved(string fileName);
    }

    /// <summary>
    /// A page of the main window that edits one of the Acrylic files.
    /// </summary>
    public interface IEditorPage
    {
        string FileName { get; }

        bool IsDirty { get; }

        event EventHandler DirtyChanged;

        /// <summary>Saves the file. Returns false if the file has not been saved.</summary>
        bool Save();

        void FocusSearch();
    }
}
