using System.Windows;

namespace XrayLauncher
{
    public partial class AlreadyRunningDialog : Window
    {
        public AlreadyRunningDialog()
        {
            InitializeComponent();
        }
        
        private void OK_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
