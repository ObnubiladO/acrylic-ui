using System.Windows;
using System.Windows.Controls;

namespace AcrylicUI.Views
{
    /// <summary>
    /// The message box of the application. It does what the message box of Windows does, with the same parameters and
    /// the same results, but it is drawn by the application so that it follows its light or dark appearance.
    /// </summary>
    public partial class MessageDialog : Window
    {
        private MessageBoxResult result;

        private MessageDialog(string text, string title, MessageBoxButton buttons, MessageBoxImage image, MessageBoxResult defaultResult)
        {
            InitializeComponent();

            ThemeManager.Attach(this);

            Title = title;

            MessageText.Text = text;

            switch (image)
            {
                case MessageBoxImage.Error: Sign.Text = "\uEA39"; Tones.Apply(Sign, Tones.Red); break;
                case MessageBoxImage.Warning: Sign.Text = "\uE7BA"; Tones.Apply(Sign, Tones.Amber); break;
                case MessageBoxImage.Question: Sign.Text = "\uE9CE"; Tones.Apply(Sign, Tones.Accent); break;
                case MessageBoxImage.Information: Sign.Text = "\uE946"; Tones.Apply(Sign, Tones.Accent); break;
                default: Sign.Visibility = Visibility.Collapsed; break;
            }

            // What closing the window with its X button or with the ESC key means.

            switch (buttons)
            {
                case MessageBoxButton.OKCancel:
                    result = MessageBoxResult.Cancel;
                    AddButton("OK", MessageBoxResult.OK, defaultResult, false);
                    AddButton("Cancel", MessageBoxResult.Cancel, defaultResult, true);
                    break;

                case MessageBoxButton.YesNo:
                    result = MessageBoxResult.No;
                    AddButton("_Yes", MessageBoxResult.Yes, defaultResult, false);
                    AddButton("_No", MessageBoxResult.No, defaultResult, true);
                    break;

                case MessageBoxButton.YesNoCancel:
                    result = MessageBoxResult.Cancel;
                    AddButton("_Yes", MessageBoxResult.Yes, defaultResult, false);
                    AddButton("_No", MessageBoxResult.No, defaultResult, false);
                    AddButton("Cancel", MessageBoxResult.Cancel, defaultResult, true);
                    break;

                default:
                    result = MessageBoxResult.OK;
                    AddButton("OK", MessageBoxResult.OK, MessageBoxResult.OK, true);
                    break;
            }
        }

        private void AddButton(string caption, MessageBoxResult value, MessageBoxResult defaultResult, bool isCancel)
        {
            bool isDefault = (value == defaultResult) || (defaultResult == MessageBoxResult.None && Buttons.Children.Count == 0);

            var button = new Button { Content = caption, MinWidth = 88, Margin = new Thickness(Buttons.Children.Count == 0 ? 0 : 8, 0, 0, 0), IsDefault = isDefault, IsCancel = isCancel };

            if (isDefault)
            {
                button.Style = (Style)FindResource("PrimaryButton");

                Loaded += (sender, e) => button.Focus();
            }

            button.Click += (sender, e) => { result = value; Close(); };

            Buttons.Children.Add(button);
        }

        public static MessageBoxResult Show(Window owner, string text, string title, MessageBoxButton buttons, MessageBoxImage image, MessageBoxResult defaultResult = MessageBoxResult.None)
        {
            var dialog = new MessageDialog(text, title, buttons, image, defaultResult);

            if (owner != null && owner.IsLoaded)
            {
                dialog.Owner = owner;
            }
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                dialog.ShowInTaskbar = true;
            }

            dialog.ShowDialog();

            return dialog.result;
        }
    }
}
