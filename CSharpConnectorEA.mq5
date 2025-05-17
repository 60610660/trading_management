//+------------------------------------------------------------------+
//|                                          CSharpConnectorEA.mq5 |
//|                        Copyright 2023, Your Name/Company |
//|                                              http://your.url |
//+------------------------------------------------------------------+
#property copyright "Copyright 2023, Your Name/Company"
#property link      "http://your.url"
#property version   "1.00"
#property strict    // 建議使用嚴格模式

#include <Zmq/Zmq.mqh> // 引入 ZeroMQ 函式庫

// --- 添加缺失的 ZeroMQ 錯誤碼定義 ---
#define ZMQ_EAGAIN   11 // Non-blocking mode was requested and the message cannot be sent at the moment.
// #define ZMQ_ETERM    156384765 // Context was terminated (這個似乎是NetMQ特有的，標準ZMQ中為EFSM)

//--- EA 輸入參數 (如果需要)
// input int ExampleParameter=10;

//--- 全域變數
long zmq_context = NULL; // ZeroMQ 上下文 (改為 long)
long publisher   = NULL; // 市場數據發布 (PUB) (改為 long)
long responder   = NULL; // 命令回應 (REP) (改為 long)
long pusher      = NULL; // 狀態報告推送 (PUSH) (改為 long)

// --- 修改綁定地址為 127.0.0.1 ---
string marketDataAddress   = "tcp://127.0.0.1:5556"; // 改為本地回環地址
string commandAddress      = "tcp://127.0.0.1:5557"; // 改為本地回環地址
string statusReportAddress = "tcp://127.0.0.1:5558"; // 改為本地回環地址

//+------------------------------------------------------------------+
//| Expert initialization function                                   |
//+------------------------------------------------------------------+
int OnInit()
{
   Print("CSharpConnectorEA: 初始化中...");

   // 1. 初始化 ZeroMQ 上下文
   zmq_context = libzmq::zmq_ctx_new(); // 添加 libzmq::
   if(zmq_context == NULL)
   {
      Print("CSharpConnectorEA: 無法建立 ZeroMQ 上下文");
      return(INIT_FAILED);
   }
   Print("CSharpConnectorEA: ZeroMQ 上下文已建立");

   // 2. 建立並綁定 Sockets
   // 市場數據發布 (PUB)
   publisher = libzmq::zmq_socket(zmq_context, ZMQ_PUB); // 添加 libzmq::
   char marketDataAddressChar[]; StringToCharArray(marketDataAddress, marketDataAddressChar);
   if(publisher == NULL || libzmq::zmq_bind(publisher, marketDataAddressChar) != 0)
   {
      // --- 增強錯誤報告 ---
      int err = libzmq::zmq_errno();
      string errMsg = Zmq::errorMessage(err); // 使用 Zmq.mqh 提供的函數
      PrintFormat("CSharpConnectorEA: 無法建立或綁定 PUB Socket 到 %s. Error: %d (%s)",
                  marketDataAddress, err, errMsg);
      DeinitializeSockets(); // 清理已建立的 Socket
      return(INIT_FAILED);
   }
   PrintFormat("CSharpConnectorEA: PUB Socket 已綁定到 %s", marketDataAddress);

   // 命令回應 (REP)
   responder = libzmq::zmq_socket(zmq_context, ZMQ_REP); // 添加 libzmq::
   char commandAddressChar[]; StringToCharArray(commandAddress, commandAddressChar);
   if(responder == NULL || libzmq::zmq_bind(responder, commandAddressChar) != 0)
   {
      // --- 增強錯誤報告 ---
      int err = libzmq::zmq_errno();
      string errMsg = Zmq::errorMessage(err);
      PrintFormat("CSharpConnectorEA: 無法建立或綁定 REP Socket 到 %s. Error: %d (%s)",
                  commandAddress, err, errMsg);
      DeinitializeSockets();
      return(INIT_FAILED);
   }
   PrintFormat("CSharpConnectorEA: REP Socket 已綁定到 %s", commandAddress);

   // 狀態報告推送 (PUSH)
   pusher = libzmq::zmq_socket(zmq_context, ZMQ_PUSH); // 添加 libzmq::
   char statusReportAddressChar[]; StringToCharArray(statusReportAddress, statusReportAddressChar);
   if(pusher == NULL || libzmq::zmq_bind(pusher, statusReportAddressChar) != 0)
   {
      // --- 增強錯誤報告 ---
      int err = libzmq::zmq_errno();
      string errMsg = Zmq::errorMessage(err);
      PrintFormat("CSharpConnectorEA: 無法建立或綁定 PUSH Socket 到 %s. Error: %d (%s)",
                  statusReportAddress, err, errMsg);
      DeinitializeSockets();
      return(INIT_FAILED);
   }
   PrintFormat("CSharpConnectorEA: PUSH Socket 已綁定到 %s", statusReportAddress);

   Print("CSharpConnectorEA: 初始化成功");
   //--- 初始成功
   return(INIT_SUCCEEDED);
}

//+------------------------------------------------------------------+
//| Expert deinitialization function                                 |
//+------------------------------------------------------------------+
void OnDeinit(const int reason)
{
   PrintFormat("CSharpConnectorEA: 反初始化中... Reason: %d", reason);
   DeinitializeSockets();
   Print("CSharpConnectorEA: 反初始化完成");
}

//+------------------------------------------------------------------+
//| 清理 Socket 和 Context                                            |
//+------------------------------------------------------------------+
void DeinitializeSockets()
{
   if(publisher != NULL)
   {
      libzmq::zmq_close(publisher); // 添加 libzmq::
      publisher = NULL;
      Print("CSharpConnectorEA: PUB Socket 已關閉");
   }
   if(responder != NULL)
   {
      libzmq::zmq_close(responder); // 添加 libzmq::
      responder = NULL;
      Print("CSharpConnectorEA: REP Socket 已關閉");
   }
   if(pusher != NULL)
   {
      libzmq::zmq_close(pusher); // 添加 libzmq::
      pusher = NULL;
      Print("CSharpConnectorEA: PUSH Socket 已關閉");
   }
   if(zmq_context != NULL)
   {
      libzmq::zmq_ctx_term(zmq_context); // 添加 libzmq:: 並改為 zmq_ctx_term
      zmq_context = NULL;
      Print("CSharpConnectorEA: ZeroMQ 上下文已銷毀");
   }
}

//+------------------------------------------------------------------+
//| Expert tick function                                             |
//+------------------------------------------------------------------+
void OnTick()
{
   // 檢查必要的 Socket 是否存在
   if(publisher == NULL || responder == NULL) return;

   // 1. 發布市場數據
   PublishMarketData();

   // 2. 檢查並處理命令 (非阻塞)
   HandleCommand();
}

//+------------------------------------------------------------------+
//| 發布市場數據                                                     |
//+------------------------------------------------------------------+
void PublishMarketData()
{
   MqlTick tick;
   if(!SymbolInfoTick(_Symbol, tick))
   {
      PrintFormat("CSharpConnectorEA: 無法獲取 %s 的 Tick 數據", _Symbol);
      return;
   }

   // *** 重要: 手動建立 JSON 字串 (極度不建議用於生產環境) ***
   string json = StringFormat("{\"Symbol\":\"%s\",\"Bid\":%.5f,\"Ask\":%.5f,\"Timestamp\":\"%s\"}",
                              _Symbol,
                              tick.bid,
                              tick.ask,
                              TimeToString(tick.time, TIME_DATE | TIME_SECONDS));

   // 將 string 轉換為 uchar[]
   uchar json_data[];
   int json_len = StringToCharArray(json, json_data) - 1; // -1 移除結尾的 null 字元
   if(json_len < 0) json_len = 0;

   // *** 確定使用：直接使用 zmq_send 發送 uchar[] ***
   if(libzmq::zmq_send(publisher, json_data, json_len, 0) == -1) // ZMQ_DONTWAIT 可能更合適
   {
       PrintFormat("CSharpConnectorEA: 發送市場數據失敗 (zmq_send). Error: %d", libzmq::zmq_errno());
   }
   // 移除所有舊的 zmq_msg_* 發送代碼

   // PrintFormat("CSharpConnectorEA: 已發布市場數據: %s", json);
}

//+------------------------------------------------------------------+
//| 處理來自 C# 的命令                                               |
//+------------------------------------------------------------------+
void HandleCommand()
{
   // --- 重構：使用 zmq_recv 直接接收到 buffer，移除 CopyMemory ---
   uchar buffer[4096]; // 增加緩衝區大小以防萬一
   int recv_bytes = libzmq::zmq_recv(responder, buffer, sizeof(buffer), ZMQ_DONTWAIT);

   if(recv_bytes > 0)
   {
      // 將接收到的數據轉換為 string
      uchar actual_data[];
      ArrayResize(actual_data, recv_bytes);
      ArrayCopy(actual_data, buffer, 0, 0, recv_bytes);
      string request_json = CharArrayToString(actual_data);

      // 移除舊的 zmq_msg_* 相關代碼
      // zmq_msg_t request_msg;
      // libzmq::zmq_msg_init(request_msg);
      // ... (舊的接收和 CopyMemory 代碼)
      // libzmq::zmq_msg_close(request_msg);

      PrintFormat("CSharpConnectorEA: 收到命令: %s", request_json);

      // *** 重要: 手動解析 JSON (極度不建議) ***
      // 這裡僅做最簡單的處理，假設命令是 "GET_ACCOUNT_BALANCE"
      string command_name = "";
      // 非常基礎的解析 - 實際應用需要正規的 JSON 解析器
      if(StringFind(request_json, "\"CommandName\":\"GET_ACCOUNT_BALANCE\"") >= 0)
      {
         command_name = "GET_ACCOUNT_BALANCE";
      }

      string response_json = "";
      // 處理命令
      if(command_name == "GET_ACCOUNT_BALANCE")
      {
         double balance = AccountInfoDouble(ACCOUNT_BALANCE);
         // *** 重要: 手動建立 JSON 回應 ***
         response_json = StringFormat("{\"Status\":\"OK\",\"Balance\":%.2f}", balance);
      }
      else
      {
         // *** 重要: 手動建立 JSON 錯誤回應 ***
         response_json = StringFormat("{\"Status\":\"Error\",\"Message\":\"Unknown command: %s\"}", request_json);
         PrintFormat("CSharpConnectorEA: 未知命令: %s", request_json);
      }

      // 將 string 轉換為 uchar[]
      uchar response_data[];
      int response_len = StringToCharArray(response_json, response_data) - 1; // -1 移除結尾的 null 字元
      if(response_len < 0) response_len = 0;

      // *** 確定使用：直接使用 zmq_send 發送 uchar[] ***
      if(libzmq::zmq_send(responder, response_data, response_len, 0) == -1)
      {
          PrintFormat("CSharpConnectorEA: 發送回應失敗 (zmq_send). Error: %d", libzmq::zmq_errno());
      }
      else
      {
         PrintFormat("CSharpConnectorEA: 已發送回應: %s", response_json);
      }
     // 移除所有舊的 zmq_msg_* 發送代碼
   }
   // else if(recv_bytes == -1 && libzmq::zmq_errno() != ZMQ_EAGAIN) // 舊的 EAGAIN 嘗試
   else if(recv_bytes == -1) // 保持移除 EAGAIN 檢查
   {
      int err = libzmq::zmq_errno();
      // --- 過濾 EAGAIN 錯誤 ---
      if (err != ZMQ_EAGAIN) // 使用定義的常數
      {
         string errMsg = Zmq::errorMessage(err);
         PrintFormat("CSharpConnectorEA: 接收命令錯誤. Error: %d (%s)", err, errMsg);
      }
      // 如果是 EAGAIN，則不打印日誌，因為這是非阻塞模式下的正常情況
   }
   // 如果 recv_bytes == 0，通常不會發生，但也無需處理
}

//+------------------------------------------------------------------+
//| 發送狀態報告給 C# (範例)                                         |
//+------------------------------------------------------------------+
void SendStatusReport(string strategyId, string status, string message)
{
   if(pusher == NULL) return;

   // *** 重要: 手動建立 JSON 字串 ***
   string json = StringFormat("{\"StrategyId\":\"%s\",\"Status\":\"%s\",\"Message\":\"%s\",\"Timestamp\":\"%s\"}",
                              strategyId,
                              status,
                              message,
                              TimeToString(TimeCurrent(), TIME_DATE | TIME_SECONDS));

   // 將 string 轉換為 uchar[]
   uchar json_data_status[];
   int json_len_status = StringToCharArray(json, json_data_status) - 1;
   if(json_len_status < 0) json_len_status = 0;

   // *** 確定使用：直接使用 zmq_send 發送 uchar[] ***
   if(libzmq::zmq_send(pusher, json_data_status, json_len_status, 0) == -1) // ZMQ_DONTWAIT 可能更合適
   {
       PrintFormat("CSharpConnectorEA: 發送狀態報告失敗 (zmq_send). Error: %d", libzmq::zmq_errno());
   }
   // 移除所有舊的 zmq_msg_* 發送代碼

   // PrintFormat("CSharpConnectorEA: 已推送狀態報告: %s", json);
}

//+------------------------------------------------------------------+
//| Expert Comment function                                          |
//+------------------------------------------------------------------+
void OnChartEvent(const int id,
                  const long& lparam,
                  const double& dparam,
                  const string& sparam)
{
//--- 可在此處添加按鈕或其他圖表事件，用於手動觸發 SendStatusReport 或其他操作
   /*
   if(id == CHARTEVENT_OBJECT_CLICK)
   {
      if(sparam == "MyStatusButton")
      {
         SendStatusReport("MyStrategy01", "ManualTrigger", "Button clicked by user");
      }
   }
   */
}
//+------------------------------------------------------------------+