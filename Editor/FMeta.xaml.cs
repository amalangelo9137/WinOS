using System.Windows;

namespace WinOS_Editor
{
    /// <summary>
    /// Interaction logic for FMeta.xaml
    /// </summary>
    public partial class FMeta : Window
    {
        public FMeta()
        {
            InitializeComponent();
        }
        
        // Props get;
        public string FrameworkName => NameBox.Text.Trim();
        public string FrameworkVersion => VersionBox.Text.Trim();
        public string FrameworkDescription => DescriptionBox.Text.Trim();

        // Props set;
        public void FrameworkNameSet(string content) { NameBox.Text = content; }
        public void FrameworkVersionSet(string content) {  VersionBox.Text = content; }
        public void FrameworkDescriptionSet(string content) { DescriptionBox.Text = content; }

        /// <summary>
        /// Closes the window and sends the data to the Owner window.
        /// </summary>
        /// <param name="sender">sender</param>
        /// <param name="e">eventargs</param>
        private void Done(object sender, RoutedEventArgs e)
        {
            // name is compulsory
            if (string.IsNullOrWhiteSpace(FrameworkName))
            {
                MessageBox.Show(
                    "Enter a name for the framework!",
                    "Missing Field",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                NameBox.Focus();
                return;
            }

            // gets the Owner and converts it to WinOS_Editor.MainWindow
            MainWindow mw = (MainWindow)Owner;

            // sets props
            mw.FrameworkNameSet(FrameworkName);
            mw.FrameworkVersionSet(FrameworkVersion);
            mw.FrameworkDescriptionSet(FrameworkDescription);

            DialogResult = true; // all set.
        }
    }
}