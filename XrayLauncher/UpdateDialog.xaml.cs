using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Windows;

namespace XrayLauncher
{
    public partial class UpdateDialog : Window
    {
        private string _downloadUrl;
        private string _tempPath;
        
        public string OldVersion 
        { 
            set { OldVersionText.Text = value; } 
        }
        
        public string NewVersion 
        { 
            set { NewVersionText.Text = value; } 
        }
        
        public string Changelog 
        { 
            set { ChangelogText.Text = value; } 
        }
        
        public string DownloadUrl 
        { 
            set { _downloadUrl = value; } 
        }
        
        public UpdateDialog()
        {
            InitializeComponent();
        }
        
        private void Update_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_downloadUrl))
            {
                MessageBox.Show("URL загрузки не указан", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            
            UpdateBtn.IsEnabled = false;
            UpdateBtn.Content = "Загрузка...";
            DownloadProgress.Visibility = Visibility.Visible;
            
            _tempPath = Path.Combine(Path.GetTempPath(), "LX_VPN_Update.exe");
            
            using (var client = new WebClient())
            {
                client.DownloadProgressChanged += Client_DownloadProgressChanged;
                client.DownloadFileCompleted += Client_DownloadFileCompleted;
                client.DownloadFileAsync(new Uri(_downloadUrl), _tempPath);
            }
        }
        
        private void Client_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            Dispatcher.Invoke(new Action(delegate
            {
                DownloadProgress.Value = e.ProgressPercentage;
                double mbReceived = e.BytesReceived / 1024.0 / 1024.0;
                double mbTotal = e.TotalBytesToReceive / 1024.0 / 1024.0;
                DownloadStatus.Text = mbReceived.ToString("F1") + " / " + mbTotal.ToString("F1") + " MB";
            }));
        }
        
        private void Client_DownloadFileCompleted(object sender, AsyncCompletedEventArgs e)
        {
            Dispatcher.Invoke(new Action(delegate
            {
                if (e.Error != null)
                {
                    MessageBox.Show("Ошибка загрузки: " + e.Error.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    UpdateBtn.IsEnabled = true;
                    UpdateBtn.Content = "⬇ Обновить";
                    return;
                }
                
                // Launch the new version and close current
                try
                {
                    DownloadStatus.Text = "Установка...";
                    
                    // Copy to app directory
                    string appPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    string appDir = Path.GetDirectoryName(appPath);
                    string updateBat = Path.Combine(Path.GetTempPath(), "lx_vpn_update.bat");
                    
                    // Create batch script that waits, replaces exe, and starts new version
                    string batContent = "@echo off\r\n" +
                        "timeout /t 2 /nobreak > nul\r\n" +
                        "copy /Y \"" + _tempPath + "\" \"" + appPath + "\"\r\n" +
                        "start \"\" \"" + appPath + "\"\r\n" +
                        "del \"" + _tempPath + "\"\r\n" +
                        "del \"%~f0\"";
                    
                    File.WriteAllText(updateBat, batContent);
                    
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = updateBat;
                    psi.WindowStyle = ProcessWindowStyle.Hidden;
                    psi.CreateNoWindow = true;
                    Process.Start(psi);
                    
                    // Close application
                    Application.Current.Shutdown();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Ошибка установки: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }));
        }
        
        private void Later_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
