// MainWindow.xaml.cs - TEMPORARY MODIFICATION FOR TESTING
using System.Windows;

namespace RedditVideoMaker.UI
{
    public partial class MainWindow : Window
    {
        // Your existing constructor for DI
        public MainWindow(MainViewModel? viewModel) // Make viewModel nullable
        {
            InitializeComponent();
            if (viewModel != null)
            {
                DataContext = viewModel;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("MainWindow (DI constructor): Created with a NULL viewModel for minimal startup test or if MainViewModel is optional.");
            }
        }

        // Parameterless constructor TEMPORARILY added for minimal App.xaml.cs test
        // Remove this or ensure it's properly handled if kept after testing.
        public MainWindow()
        {
            InitializeComponent();
            System.Diagnostics.Debug.WriteLine("MainWindow (parameterless constructor): Created for minimal startup test.");
            // You might want to set a title or some basic content here if not using a ViewModel for this test
            this.Title = "MainWindow - Parameterless Test";
        }
    }
}