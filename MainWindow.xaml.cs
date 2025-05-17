using System.Windows;
using System.ComponentModel; // Required for CancelEventArgs

namespace TradingManagement
{
    public partial class MainWindow : Window
    {
        public MainWindow(MainViewModel viewModel) // ViewModel 通過依賴注入傳入
        {
            InitializeComponent();
            DataContext = viewModel; // 設定 DataContext
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            // 為了確保點擊關閉按鈕的行為與 Exit_Click 按鈕完全一致，
            // 我們也調用 Application.Current.Shutdown()。
            // WPF 的 Application.Shutdown() 方法在被多次調用時通常是安全的。
            // 如果應用程式已經在關閉過程中，後續的調用通常會被忽略。
            
            // 如果擔心重複調用 Shutdown()，可以先檢查應用程式是否正在關閉。
            // 但通常直接調用是可行的，並且能確保觸發 App.OnExit 中的清理邏輯。
            Application.Current.Shutdown();
            
            // 如果在 Shutdown() 之前需要執行一些異步操作，並且希望等待它們完成，
            // 則可能需要更複雜的處理，例如設置 e.Cancel = true，
            // 然後在異步操作完成後手動調用 this.Close() 或 Application.Current.Shutdown()。
            // 但對於簡單的「與離開按鈕行為一致」的需求，直接 Shutdown() 即可。
        }
    }
}