using Microsoft.Extensions.Configuration; // 確保 IConfiguration 和 ConfigurationBuilder 被識別
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging; // 確保 ILogger 被識別
using System;
using System.IO; // For Path.Combine
using System.Windows;
using TradingManagementServices; // For ISystemStatusService

namespace TradingManagement
{
    public partial class App : Application
    {
        private IHost? _host; // 修改：宣告為可為 Null
        // 提前獲取一個靜態的 Logger Factory，以便在 Host 完全建立前記錄日誌
        private static ILoggerFactory _earlyLoggerFactory = LoggerFactory.Create(builder => builder.AddDebug().AddConsole());
        private static ILogger _earlyLogger = _earlyLoggerFactory.CreateLogger("AppEarlyLog");

        public App()
        {
            _earlyLogger.LogInformation("App constructor started.");
            try
            {
                _host = Host.CreateDefaultBuilder() // 預設會載入 appsettings.json
                    .ConfigureAppConfiguration((hostingContext, config) =>
                    {
                        // 清除預設來源 (如果需要完全控制，但通常 CreateDefaultBuilder 已包含 appsettings.json)
                        // config.Sources.Clear(); 

                        _earlyLogger.LogInformation("Configuring App Configuration...");
                        config.SetBasePath(AppContext.BaseDirectory) // 確保基礎路徑正確
                              .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                              .AddJsonFile($"appsettings.{hostingContext.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true)
                              .AddEnvironmentVariables();
                        _earlyLogger.LogInformation("appsettings.json explicitly added to configuration sources.");
                    })
                    .ConfigureLogging(logging =>
                    {
                        logging.ClearProviders();
                        logging.AddConsole();
                        logging.AddDebug(); 
                        // 可以在此處添加其他日誌提供者，例如 Serilog
                    })
                    .ConfigureServices((context, services) =>
                    {
                        _earlyLogger.LogInformation("Configuring services...");
                        // 嘗試讀取設定並記錄
                        var zeroMqSettings = context.Configuration.GetSection("ZeroMqSettings").Get<ZeroMqSettings>();
                        if (zeroMqSettings == null)
                        {
                            _earlyLogger.LogError("ZeroMqSettings section is NULL after GetSection.Get<ZeroMqSettings>() in App.ConfigureServices.");
                        }
                        else
                        {
                            _earlyLogger.LogInformation("ZeroMqSettings in App.ConfigureServices: MarketDataAddress={MarketData}, CommandAddress={Command}, StatusReportAddress={StatusReport}", 
                                zeroMqSettings.MarketDataAddress, zeroMqSettings.CommandAddress, zeroMqSettings.StatusReportAddress);
                        }
                        ConfigureServices(context.Configuration, services); // 傳遣 IConfiguration
                    })
                    .Build();
                _earlyLogger.LogInformation("Host built successfully.");
            }
            catch (Exception ex)
            {
                _earlyLogger.LogError(ex, "Exception during App constructor (Host building).");
                MessageBox.Show($"應用程式初始化失敗: {ex.Message}", "嚴重錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                // 可能需要更優雅地關閉應用程式
                if (Application.Current != null)
                {
                    Application.Current.Shutdown(-1);
                }
            }
        }

        // 修改 ConfigureServices 以接收 IConfiguration
        private void ConfigureServices(IConfiguration configuration, IServiceCollection services)
        {
            // 註冊 WPF 視窗和 ViewModels
            services.AddSingleton<MainWindow>();
            services.AddSingleton<MainViewModel>();

            // 註冊核心服務
            // services.Configure<ZeroMqSettings>(configuration.GetSection("ZeroMqSettings")); // TradingWorker 建構函式會直接從 IConfiguration 讀取
            // 但為了確保它能正確解析，還是可以保留 services.Configure，或者依賴 TradingWorker 中的邏輯
             var settings = configuration.GetSection("ZeroMqSettings").Get<ZeroMqSettings>();
             if (settings == null)
             {
                 _earlyLogger.LogError("ZeroMqSettings is null when trying to register in ConfigureServices. This will likely cause issues.");
                 // 拋出異常或提供一個預設的回退設定可能會更好，以避免後續的 NullReferenceException
                 // services.AddSingleton(new ZeroMqSettings()); // 提供一個空的實例以避免 DI 失敗，但 Worker 會出錯
             }
             else
             {
                services.AddSingleton(settings); // 直接註冊 ZeroMqSettings 實例
             }

            services.AddSingleton<IFundingManager, FundingManager>();
            services.AddSingleton<IEvaluationSystem, EvaluationSystem>();
            services.AddSingleton<IStrategyManager, StrategyManager>();
            services.AddSingleton<IRiskManager, RiskManager>();
            services.AddSingleton<IPerformanceManager, PerformanceManager>();
            services.AddSingleton<ISystemStatusService, SystemStatusService>();

            // 註冊主要工作服務 (背景服務)
            services.AddHostedService<TradingWorker>();
            _earlyLogger.LogInformation("Core services and TradingWorker registered.");
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            _earlyLogger.LogInformation("App OnStartup started.");
            if (_host == null)
            {
                _earlyLogger.LogError("Host is null in OnStartup. Application cannot start.");
                MessageBox.Show("主機初始化失敗，應用程式無法啟動。", "啟動錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                if (Application.Current != null) Application.Current.Shutdown(-2);
                return;
            }
            try
            {
                await _host.StartAsync();
                _earlyLogger.LogInformation("Host started successfully.");

                var mainWindow = _host.Services.GetRequiredService<MainWindow>();
                _earlyLogger.LogInformation("MainWindow resolved from services.");
                mainWindow.Show();
                _earlyLogger.LogInformation("MainWindow shown.");
            }
            catch (Exception ex)
            {
                _earlyLogger.LogError(ex, "Exception during OnStartup (Host starting or showing MainWindow).");
                MessageBox.Show($"應用程式啟動時發生錯誤: {ex.Message}", "啟動錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                // 嘗試安全關閉
                if (_host != null) 
                {
                    try { await _host.StopAsync(); } catch { /* ignore */ }
                    _host.Dispose();
                }
                if (Application.Current != null) Application.Current.Shutdown(-3);
                return;
            }
            base.OnStartup(e);
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            _earlyLogger.LogInformation("App OnExit started.");
            if (_host != null)
            {
                using (_host)
                {
                    try
                    {
                        await _host.StopAsync(TimeSpan.FromSeconds(5)); 
                         _earlyLogger.LogInformation("Host stopped successfully.");
                    }
                    catch(Exception ex)
                    {
                        _earlyLogger.LogError(ex, "Exception during host stopping.");
                    }
                }
            }
            _earlyLoggerFactory.Dispose(); // 清理靜態 Logger Factory
            base.OnExit(e);
        }
    }
}