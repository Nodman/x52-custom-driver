using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
// WinForms is referenced too (tray icon): pick the WPF types explicitly
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;

namespace X52.CustomDriver.App
{
    /// <summary>
    /// A small dark dialog with a message and several big choice buttons, each with a short
    /// explanation underneath. Returns the index of the chosen option, or -1 when closed.
    /// </summary>
    public class ChoiceDialog : Window
    {
        public record Choice(string Label, string Description, bool IsDefault = false);

        private int _result = -1;

        private ChoiceDialog(string title, string message, IReadOnlyList<Choice> choices)
        {
            Title = title;
            Width = 520;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0B));
            Foreground = System.Windows.Media.Brushes.White;
            FontFamily = new FontFamily("Segoe UI");

            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 16)
            });

            for (int i = 0; i < choices.Count; i++)
            {
                int index = i;
                var c = choices[i];
                var content = new StackPanel();
                content.Children.Add(new TextBlock { Text = c.Label, FontWeight = FontWeights.Bold, FontSize = 13 });
                if (!string.IsNullOrEmpty(c.Description))
                    content.Children.Add(new TextBlock
                    {
                        Text = c.Description,
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 11,
                        FontWeight = FontWeights.Normal,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                        Margin = new Thickness(0, 3, 0, 0)
                    });

                var accent = c.IsDefault ? Color.FromRgb(0x00, 0xD2, 0xFF) : Color.FromRgb(0x55, 0x55, 0x55);
                var button = new System.Windows.Controls.Button
                {
                    Content = content,
                    HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left,
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 0, 0, 8),
                    Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                    Foreground = c.IsDefault ? new SolidColorBrush(accent) : System.Windows.Media.Brushes.White,
                    BorderBrush = new SolidColorBrush(accent),
                    BorderThickness = new Thickness(c.IsDefault ? 2 : 1),
                    IsDefault = c.IsDefault
                };
                button.Click += (s, e) => { _result = index; DialogResult = true; };
                panel.Children.Add(button);
                if (c.IsDefault) Loaded += (s, e) => button.Focus();
            }

            Content = panel;
        }

        public static int Ask(Window? owner, string title, string message, IReadOnlyList<Choice> choices)
        {
            var dlg = new ChoiceDialog(title, message, choices);
            if (owner != null && owner.IsVisible) dlg.Owner = owner;
            else dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dlg.ShowDialog();
            return dlg._result;
        }
    }
}
