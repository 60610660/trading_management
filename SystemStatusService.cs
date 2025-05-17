using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows; // Added for Application.Current.Dispatcher
using TradingManagement; // 引入包含 MarketData 和 StatusReport 的命名空間

namespace TradingManagementServices
{
    public interface ISystemStatusService : INotifyPropertyChanged
    {
        string ZeroMqMarketDataStatus { get; set; }
        string ZeroMqCommandStatus { get; set; }
        string ZeroMqStatusReportStatus { get; set; }
        MarketData? LastMarketData { get; }
        ObservableCollection<MarketData> RecentMarketData { get; }
        StatusReport? LastStatusReport { get; }
        ObservableCollection<StatusReport> RecentStatusReports { get; }
        void UpdateLastMarketData(MarketData data);
        void UpdateLastStatusReport(StatusReport report);
    }

    public class SystemStatusService : ISystemStatusService
    {
        private string _zeroMqMarketDataStatus = "Initializing";
        public string ZeroMqMarketDataStatus
        {
            get => _zeroMqMarketDataStatus;
            set { _zeroMqMarketDataStatus = value; OnPropertyChanged(); }
        }

        private string _zeroMqCommandStatus = "Initializing";
        public string ZeroMqCommandStatus
        {
            get => _zeroMqCommandStatus;
            set { _zeroMqCommandStatus = value; OnPropertyChanged(); }
        }

        private string _zeroMqStatusReportStatus = "Initializing";
        public string ZeroMqStatusReportStatus
        {
            get => _zeroMqStatusReportStatus;
            set { _zeroMqStatusReportStatus = value; OnPropertyChanged(); }
        }

        private MarketData? _lastMarketData;
        public MarketData? LastMarketData
        {
            get => _lastMarketData;
            private set { _lastMarketData = value; OnPropertyChanged(); }
        }

        public ObservableCollection<MarketData> RecentMarketData { get; } = new ObservableCollection<MarketData>();

        private StatusReport? _lastStatusReport;
        public StatusReport? LastStatusReport
        {
            get => _lastStatusReport;
            private set { _lastStatusReport = value; OnPropertyChanged(); }
        }
        public ObservableCollection<StatusReport> RecentStatusReports { get; } = new ObservableCollection<StatusReport>();

        private const int MaxRecentItems = 10;

        public void UpdateLastMarketData(MarketData data)
        {
            LastMarketData = data; // This can be set from any thread if OnPropertyChanged handles thread switching, or if bound to a property that is updated on UI thread

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (RecentMarketData.Count >= MaxRecentItems)
                {
                    RecentMarketData.RemoveAt(0); // 移除最舊的
                }
                RecentMarketData.Add(data); // 添加最新的
            });
        }

        public void UpdateLastStatusReport(StatusReport report)
        {
            LastStatusReport = report; // Similar to LastMarketData

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (RecentStatusReports.Count >= MaxRecentItems)
                {
                    RecentStatusReports.RemoveAt(0); // 移除最舊的
                }
                RecentStatusReports.Add(report); // 添加最新的
            });
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            // Ensure PropertyChanged is invoked on the UI thread as well, 
            // especially if properties like LastMarketData or LastStatusReport are bound directly
            // and their setters are called from background threads.
            if (Application.Current.Dispatcher.CheckAccess())
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
            else
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
                });
            }
        }
    }
}