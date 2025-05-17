​using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using NetMQ;
using NetMQ.Sockets;
using Newtonsoft.Json; // 我們將使用 Newtonsoft.Json 來處理 JSON
using TradingManagementServices; // 引入 SystemStatusService

namespace TradingManagement
{
    // 用於市場數據的訊息結構 (範例)
    public class MarketData
    {
        public string Symbol { get; set; } = string.Empty;
        public double Bid { get; set; }
        public double Ask { get; set; }
        public DateTime Timestamp { get; set; }
    }

    // 用於命令的訊息結構 (範例)
    public class Command
    {
        public string CommandName { get; set; } = string.Empty;
        public dynamic? Parameters { get; set; }
    }

    // 用於狀態報告的訊息結構 (範例)
    public class StatusReport
    {
        public string StrategyId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }

    // --- 設定模型 ---
    public class ZeroMqSettings
    {
        public string MarketDataAddress { get; set; } = string.Empty;
        public string CommandAddress { get; set; } = string.Empty;
        public string StatusReportAddress { get; set; } = string.Empty;
    }

    // --- 核心功能模組介面 (為依賴注入準備) ---
    public interface IFundingManager { /* ... 方法簽名 ... */ }
    public interface IEvaluationSystem { /* ... 方法簽名 ... */ }
    public interface IStrategyManager
    {
        void ProcessMarketUpdate(MarketData data);
        void UpdateStrategyStatus(StatusReport report);
        // ... 其他方法簽名 ...
    }
    public interface IRiskManager { /* ... 方法簽名 ... */ }
    public interface IPerformanceManager
    {
        void UpdatePerformance(MarketData data);
        // ... 其他方法簽名 ...
    }

    // --- 核心功能模組實作 (稍後移至獨立檔案) ---
    // 暫時保留為空實作或記錄日誌，以便程式運行
    public class FundingManager : IFundingManager { private readonly ILogger<FundingManager> _logger; public FundingManager(ILogger<FundingManager> logger) => _logger = logger; /* ... 實作 ... */ }
    public class EvaluationSystem : IEvaluationSystem { private readonly ILogger<EvaluationSystem> _logger; public EvaluationSystem(ILogger<EvaluationSystem> logger) => _logger = logger; /* ... 實作 ... */ }
    public class StrategyManager : IStrategyManager
    {
        private readonly ILogger<StrategyManager> _logger;
        public StrategyManager(ILogger<StrategyManager> logger) => _logger = logger;
        public void ProcessMarketUpdate(MarketData data) => _logger.LogInformation("[策略管理] 處理市場更新 for {Symbol}", data.Symbol); // 替換 Console.WriteLine
        public void UpdateStrategyStatus(StatusReport report) => _logger.LogInformation("[策略管理] 更新策略狀態: {StrategyId} - {Status}", report.StrategyId, report.Status); // 替換 Console.WriteLine
    }
    public class RiskManager : IRiskManager { private readonly ILogger<RiskManager> _logger; public RiskManager(ILogger<RiskManager> logger) => _logger = logger; /* ... 實作 ... */ }
    public class PerformanceManager : IPerformanceManager
    {
        private readonly ILogger<PerformanceManager> _logger;
        public PerformanceManager(ILogger<PerformanceManager> logger) => _logger = logger;
        public void UpdatePerformance(MarketData data) => _logger.LogInformation("[績效統計] 更新績效數據 for {Symbol}", data.Symbol); // 替換 Console.WriteLine
    }

    // --- 主要工作服務 ---
    public class TradingWorker : BackgroundService
    {
        private readonly ILogger<TradingWorker> _logger;
        private readonly ZeroMqSettings _settings;
        private readonly IStrategyManager _strategyManager;
        private readonly IPerformanceManager _performanceManager;
        private readonly ISystemStatusService _statusService;
        private NetMQPoller? _poller;
        private SubscriberSocket? _marketDataSubscriber;
        private RequestSocket? _commandRequester;
        private PullSocket? _statusReportReceiver;

        public TradingWorker(ILogger<TradingWorker> logger,
                             ZeroMqSettings settings,
                             IStrategyManager strategyManager,
                             IPerformanceManager performanceManager,
                             ISystemStatusService statusService)
        {
            _logger = logger;
            _settings = settings ?? throw new ArgumentNullException(nameof(settings), "ZeroMqSettings cannot be null.");
            
            _logger.LogInformation("ZeroMqSettings received via DI: MarketDataAddress={MarketDataAddress}, CommandAddress={CommandAddress}, StatusReportAddress={StatusReportAddress}", 
                                 _settings.MarketDataAddress, _settings.CommandAddress, _settings.StatusReportAddress);
            
            if (string.IsNullOrEmpty(_settings.MarketDataAddress) || 
                string.IsNullOrEmpty(_settings.CommandAddress) || 
                string.IsNullOrEmpty(_settings.StatusReportAddress))
            {
                _logger.LogError("One or more ZeroMQ addresses are missing in ZeroMqSettings.");
                throw new InvalidOperationException("One or more ZeroMQ addresses are missing in ZeroMqSettings. Please check appsettings.json.");
            }

            _strategyManager = strategyManager;
            _performanceManager = performanceManager;
            _statusService = statusService;
        }

        private void InitializeSockets()
        {
            _logger.LogInformation("Initializing Sockets...");
            try
            {
                _marketDataSubscriber = new SubscriberSocket();
                _logger.LogInformation("_marketDataSubscriber created.");
                _commandRequester = new RequestSocket();
                _logger.LogInformation("_commandRequester created.");
                _statusReportReceiver = new PullSocket();
                _logger.LogInformation("_statusReportReceiver created.");

                // 在將 Sockets 添加到 Poller 之前，確保它們不是 null
                if (_marketDataSubscriber == null || _commandRequester == null || _statusReportReceiver == null)
                {
                    _logger.LogError("One or more sockets failed to initialize before adding to poller.");
                    // 這種情況下 _poller 可能會保持為 null，並在後續檢查中被捕獲
                    return; 
                }

                _poller = new NetMQPoller { _marketDataSubscriber, _statusReportReceiver }; 
                _logger.LogInformation("_poller created. Is _poller null? {IsPollerNull}", _poller == null);
                
                // 確保 Poller 成功建立
                if (_poller == null)
                {
                    _logger.LogError("NetMQPoller failed to initialize.");
                    return; // 避免後續對 null _poller 的操作
                }

                _marketDataSubscriber.ReceiveReady += HandleMarketData;
                _statusReportReceiver.ReceiveReady += HandleStatusReport;
                _logger.LogInformation("Socket ReceiveReady events subscribed.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during InitializeSockets.");
                // 確保在異常情況下，依賴於這些 sockets 的後續代碼不會執行
                // 可以考慮重新拋出或設定一個標誌指示初始化失敗
                _poller = null; // 確保 poller 為 null，以觸發後續的檢查
                throw; // 重新拋出，讓 BackgroundService 知道啟動失敗
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("C# ZeroMQ Connector (Worker) ExecuteAsync starting...");
            _logger.LogInformation("Initial Poller state in ExecuteAsync: Is _poller null? {IsPollerNull}", _poller == null);

            try
            {
                InitializeSockets();
                _logger.LogInformation("InitializeSockets completed. Current Poller state: Is _poller null? {IsPollerNull}", _poller == null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize sockets in ExecuteAsync. Worker cannot start.");
                _statusService.ZeroMqMarketDataStatus = "Error: Init failed";
                _statusService.ZeroMqCommandStatus = "Error: Init failed";
                _statusService.ZeroMqStatusReportStatus = "Error: Init failed";
                return; // 初始化失敗，直接返回
            }


            if (_marketDataSubscriber == null || _commandRequester == null || _statusReportReceiver == null || _poller == null)
            {
                _logger.LogError("Sockets or Poller is null after InitializeSockets. Worker stopping. MarketDataSub: {MDS}, CmdReq: {CR}, StatusRec: {SR}, Poller: {P}", 
                                 _marketDataSubscriber == null, _commandRequester == null, _statusReportReceiver == null, _poller == null);
                _statusService.ZeroMqMarketDataStatus = "Error: Sockets null";
                _statusService.ZeroMqCommandStatus = "Error: Sockets null";
                _statusService.ZeroMqStatusReportStatus = "Error: Sockets null";
                return;
            }

            _logger.LogInformation("All sockets and poller seem initialized. Proceeding to connect.");

            try
            {
                _logger.LogInformation("Connecting MarketDataSubscriber to {Address}", _settings.MarketDataAddress);
                _marketDataSubscriber.Connect(_settings.MarketDataAddress);
                _marketDataSubscriber.Subscribe("");
                _logger.LogInformation("MarketDataSubscriber connected and subscribed.");
                _statusService.ZeroMqMarketDataStatus = $"Connected to {_settings.MarketDataAddress}";

                _logger.LogInformation("Connecting CommandRequester to {Address}", _settings.CommandAddress);
                _commandRequester.Connect(_settings.CommandAddress);
                _logger.LogInformation("CommandRequester connected.");
                _statusService.ZeroMqCommandStatus = $"Connected to {_settings.CommandAddress}";

                _logger.LogInformation("Connecting StatusReportReceiver to {Address}", _settings.StatusReportAddress);
                _statusReportReceiver.Connect(_settings.StatusReportAddress);
                _logger.LogInformation("StatusReportReceiver connected.");
                _statusService.ZeroMqStatusReportStatus = $"Connected to {_settings.StatusReportAddress}";

                _ = SendTestCommandAsync();

                stoppingToken.Register(() =>
                {
                    _logger.LogInformation("Stop signal received. Stopping Poller...");
                    _poller?.Stop(); 
                });

                _logger.LogInformation("Starting Poller.Run in Task.Run...");
                await Task.Run(() => 
                {
                    try
                    {
                        _logger.LogInformation("Poller Task.Run: Entering _poller.Run()");
                        _poller?.Run(); 
                        _logger.LogInformation("Poller Task.Run: _poller.Run() exited.");
                    }
                    catch (System.Runtime.InteropServices.SEHException sehEx)
                    {
                        _logger.LogWarning(sehEx, "Poller Task.Run: SEHException during Poller.Run, possibly after Stop.");
                    }
                    catch (NetMQ.TerminatingException termEx)
                    {
                        _logger.LogInformation(termEx, "Poller Task.Run: Poller terminated as expected.");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Poller Task.Run: Exception in Poller Task.Run");
                    }
                }, stoppingToken);

                _logger.LogInformation("Poller Task.Run completed or cancelled.");

            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("ExecuteAsync received OperationCanceledException. Worker stopping.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in ExecuteAsync after socket initialization.");
                _statusService.ZeroMqMarketDataStatus = "Error: Runtime";
                _statusService.ZeroMqCommandStatus = "Error: Runtime";
                _statusService.ZeroMqStatusReportStatus = "Error: Runtime";
            }
            finally
            {
                _logger.LogInformation("ExecuteAsync finally block. Cleaning up resources...");
                
                _marketDataSubscriber?.Disconnect(_settings.MarketDataAddress);
                _marketDataSubscriber?.Close();
                _marketDataSubscriber?.Dispose();
                _statusService.ZeroMqMarketDataStatus = "Disconnected";

                _commandRequester?.Disconnect(_settings.CommandAddress);
                _commandRequester?.Close();
                _commandRequester?.Dispose();
                _statusService.ZeroMqCommandStatus = "Disconnected";

                _statusReportReceiver?.Disconnect(_settings.StatusReportAddress);
                _statusReportReceiver?.Close();
                _statusReportReceiver?.Dispose();
                _statusService.ZeroMqStatusReportStatus = "Disconnected";

                 _poller?.Dispose(); 
                _logger.LogInformation("Worker cleanup complete.");
            }
        }

        private void HandleMarketData(object? sender, NetMQSocketEventArgs e)
        {
            try
            {
                if (e.Socket == null || !e.Socket.HasIn) return;

                string messageTopic = e.Socket.ReceiveFrameString();
                string messageJson = e.Socket.ReceiveFrameString();
                MarketData? data = JsonConvert.DeserializeObject<MarketData>(messageJson);
                if (data != null)
                {
                    _logger.LogInformation("市場數據: {Symbol} Bid: {Bid} Ask: {Ask} Time: {Timestamp}", data.Symbol ?? "N/A", data.Bid, data.Ask, data.Timestamp);
                    _strategyManager.ProcessMarketUpdate(data);
                    _performanceManager.UpdatePerformance(data);
                    _statusService.UpdateLastMarketData(data);
                }
                else
                {
                    _logger.LogWarning("收到的市場數據無法反序列化: {Json}", messageJson);
                }
            }
            catch (NetMQException ex) when (ex.ErrorCode != ErrorCode.ContextTerminated)
            {
                _logger.LogError(ex, "市場數據接收錯誤");
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError(jsonEx, "市場數據JSON反序列化錯誤");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "處理市場數據時發生未知錯誤");
            }
        }

        private void HandleStatusReport(object? sender, NetMQSocketEventArgs e)
        {
            try
            {
                if (e.Socket == null || !e.Socket.HasIn) return;

                string messageJson = e.Socket.ReceiveFrameString();
                StatusReport? report = JsonConvert.DeserializeObject<StatusReport>(messageJson);
                if (report != null)
                {
                    _logger.LogInformation("狀態報告: Strategy: {StrategyId} Status: {Status} Msg: {Message} Time: {Timestamp}",
                        report.StrategyId ?? "N/A", report.Status ?? "N/A", report.Message ?? "N/A", report.Timestamp);
                    _strategyManager.UpdateStrategyStatus(report);
                    _statusService.UpdateLastStatusReport(report);
                }
                else
                {
                    _logger.LogWarning("收到的狀態報告無法反序列化: {Json}", messageJson);
                }
            }
            catch (NetMQException ex) when (ex.ErrorCode != ErrorCode.ContextTerminated)
            {
                _logger.LogError(ex, "狀態報告接收錯誤");
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError(jsonEx, "狀態報告JSON反序列化錯誤");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "處理狀態報告時發生未知錯誤");
            }
        }

        private async Task SendTestCommandAsync()
        {
            if (_commandRequester == null)
            {
                _logger.LogError("Command Requester 未初始化，無法發送測試命令。");
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None); // 使用 CancellationToken.None 如果不希望此延遲被 worker 的 stoppingToken 取消

            var testCommand = new Command
            {
                CommandName = "GET_ACCOUNT_BALANCE",
                Parameters = null
            };
            string commandJson = JsonConvert.SerializeObject(testCommand);
            _logger.LogInformation("發送命令: {CommandJson}", commandJson);

            string? responseJson = null;
            bool received = false;
            try
            {
                // 檢查 _commandRequester 是否已被 dispose
                if (_commandRequester.IsDisposed) 
                {
                    _logger.LogWarning("Command Requester 已釋放，無法發送或接收命令。");
                    return;
                }

                lock (_commandRequester) // 確保線程安全訪問
                {
                    if (!_commandRequester.IsDisposed) // 再次檢查，因為 lock 不是經時的
                    {
                         _commandRequester.SendFrame(commandJson);
                         received = _commandRequester.TryReceiveFrameString(TimeSpan.FromSeconds(5), out responseJson);
                    } 
                    else
                    {
                         _logger.LogWarning("Command Requester 在鎖定後發現已釋放。");
                         return;
                    }
                }

                if (received)
                {
                    _logger.LogInformation("收到回應: {ResponseJson}", responseJson ?? "NULL");
                }
                else
                {
                    // 再次檢查 IsDisposed，避免在已釋放的 socket上記錄超時
                    if (!_commandRequester.IsDisposed)
                    {
                        _logger.LogWarning("命令回應超時。");
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                _logger.LogWarning("嘗試在已釋放的 Command Requester 上操作。");
            }
            // 添加對 TerminatingException 的處理，以防 Socket 在等待回應時終止
            catch (NetMQ.TerminatingException termEx)
            {
                _logger.LogWarning(termEx, "Command Requester 在嘗試發送/接收時終止。");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "發送或接收命令時出錯");
            }
        }
    }

    // --- 程式進入點 (現在由 App.xaml.cs 處理) ---
    public class Program
    {
        // 將 Main 方法重新命名，以避免多個進入點的錯誤 CS0017
        public static void OriginalProgramMain(string[] args) 
        {
            // WPF 應用程式的進入點現在是 App.xaml.cs
            // TradingWorker 等服務由 App.xaml.cs 中的通用主機管理。
            // 此方法不再是應用程式的直接進入點。
        }

        // CreateHostBuilder 方法已不再需要，因為配置已移至 App.xaml.cs
        /*
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    // 服務配置現在在 App.xaml.cs 中
                });
        */
    }
}