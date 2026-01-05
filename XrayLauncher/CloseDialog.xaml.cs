using System.Windows;

namespace XrayLauncher
{
    public partial class CloseDialog : Window
    {
        public enum CloseAction { Close, Tray, Cancel }
        
        private CloseAction _result;
        
        public CloseAction Result 
        { 
            get { return _result; } 
            private set { _result = value; }
        }
        
        public CloseDialog()
        {
            _result = CloseAction.Cancel;
            InitializeComponent();
        }
        
        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Result = CloseAction.Close;
            DialogResult = true;
            Close();
        }
        
        private void TrayBtn_Click(object sender, RoutedEventArgs e)
        {
            Result = CloseAction.Tray;
            DialogResult = true;
            Close();
        }
        
        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            Result = CloseAction.Cancel;
            DialogResult = false;
            Close();
        }
    }
}
