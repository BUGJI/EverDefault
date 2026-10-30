using System.Windows;
using System.Windows.Input;
using System.Windows.Shell;

namespace EverDefault.App
{
    /// <summary>A dialog window with the app's own dark-friendly title bar.</summary>
    public class DialogWindow : Window
    {
        public DialogWindow()
        {
            WindowSizing.Clamp(this);

            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 38,
                ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false
            });

            CommandBindings.Add(new CommandBinding(ApplicationCommands.Close, OnCloseCommand));
        }

        private void OnCloseCommand(object sender, ExecutedRoutedEventArgs e)
        {
            Close();
        }
    }
}
