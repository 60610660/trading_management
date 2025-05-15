using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using NetMQ;
using NetMQ.Sockets;
using Newtonsoft.Json; // 我們將使用 Newtonsoft.Json 來處理 JSON

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
        private NetMQPoller? _poller;
        private SubscriberSocket? _marketDataSubscriber;
        private RequestSocket? _commandRequester;
        private PullSocket? _statusReportReceiver;

        public TradingWorker(ILogger<TradingWorker> logger,
                             IConfiguration configuration,
                             IStrategyManager strategyManager,
                             IPerformanceManager performanceManager)
        {
            _logger = logger;
            _settings = configuration.GetSection("ZeroMqSettings").Get<ZeroMqSettings>()!;
            _strategyManager = strategyManager;
            _performanceManager = performanceManager;
        }

        private void InitializeSockets()
        {
            _marketDataSubscriber = new SubscriberSocket();
            _commandRequester = new RequestSocket();
            _statusReportReceiver = new PullSocket();

            _poller = new NetMQPoller { _marketDataSubscriber, _statusReportReceiver };

            _marketDataSubscriber.ReceiveReady += HandleMarketData;
            _statusReportReceiver.ReceiveReady += HandleStatusReport;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("C# ZeroMQ Connector (Worker) 啟動中...");

            InitializeSockets();

            if (_marketDataSubscriber == null || _commandRequester == null || _statusReportReceiver == null || _poller == null)
            {
                _logger.LogError("Sockets 或 Poller 初始化失敗。Worker 停止。");
                return;
            }

            try
            {
                _marketDataSubscriber.Connect(_settings.MarketDataAddress);
                _marketDataSubscriber.Subscribe("");
                _logger.LogInformation("已連接到市場數據發布者: {Address}", _settings.MarketDataAddress);

                _commandRequester.Connect(_settings.CommandAddress);
                _logger.LogInformation("已連接到命令處理器: {Address}", _settings.CommandAddress);

                _statusReportReceiver.Connect(_settings.StatusReportAddress);
                _logger.LogInformation("已連接到狀態報告推送器: {Address}", _settings.StatusReportAddress);

                _ = SendTestCommandAsync();

                stoppingToken.Register(() =>
                {
                    _logger.LogInformation("停止信號已接收。停止 Poller...");
                    _poller?.Stop();
                });

                _logger.LogInformation("C# ZeroMQ Connector (Worker) 已啟動。開始輪詢訊息...");

                _poller.Run();

                _logger.LogInformation("Poller 已停止。");

            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Worker 執行期間發生未處理的例外狀況");
            }
            finally
            {
                _logger.LogInformation("Worker 正在清理資源...");
                _poller?.Dispose();
                _marketDataSubscriber?.Disconnect(_settings.MarketDataAddress);
                _marketDataSubscriber?.Close();
                _marketDataSubscriber?.Dispose();
                _commandRequester?.Disconnect(_settings.CommandAddress);
                _commandRequester?.Close();
                _commandRequester?.Dispose();
                _statusReportReceiver?.Disconnect(_settings.StatusReportAddress);
                _statusReportReceiver?.Close();
                _statusReportReceiver?.Dispose();
                _logger.LogInformation("Worker 清理完成。");
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

            await Task.Delay(1000);

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
                lock (_commandRequester)
                {
                    if (!_commandRequester.IsDisposed)
                    {
                        _commandRequester.SendFrame(commandJson);
                        received = _commandRequester.TryReceiveFrameString(TimeSpan.FromSeconds(5), out responseJson);
            }
            else
            {
                        _logger.LogWarning("Command Requester 已釋放，無法發送或接收命令。");
                    }
                }

                if (received)
                {
                    _logger.LogInformation("收到回應: {ResponseJson}", responseJson ?? "NULL");
                }
                else
                {
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "發送或接收命令時出錯");
            }
        }
    }

    // --- 程式進入點 ---
    public class Program
    {
        public static async Task Main(string[] args)
        {
            await CreateHostBuilder(args).Build().RunAsync();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    // 可在此處添加更多設定來源
                })
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                    // 可在此處添加更多日誌提供者，例如 Serilog
                })
                .ConfigureServices((hostContext, services) =>
                {
                    // 註冊設定
                    services.Configure<ZeroMqSettings>(hostContext.Configuration.GetSection("ZeroMqSettings"));

                    // 註冊核心服務 (單例或 Scoped/Transient 取決於具體需求和實作)
                    services.AddSingleton<IFundingManager, FundingManager>();
                    services.AddSingleton<IEvaluationSystem, EvaluationSystem>();
                    services.AddSingleton<IStrategyManager, StrategyManager>();
                    services.AddSingleton<IRiskManager, RiskManager>();
                    services.AddSingleton<IPerformanceManager, PerformanceManager>();

                    // 註冊主要工作服務
                    services.AddHostedService<TradingWorker>();
                });
    }
}

