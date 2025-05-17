using System.ComponentModel;
using System.Runtime.CompilerServices;
using TradingManagementServices; // For ISystemStatusService

namespace TradingManagement
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public ISystemStatusService StatusService { get; }

        public MainViewModel(ISystemStatusService statusService) // ISystemStatusService 通過依賴注入傳入
        {
            StatusService = statusService;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}