# 交易管理系統

本專案是一個整合 C# 與 MQL5 (MetaTrader 5) 的交易管理系統。該系統使用 ZeroMQ (NetMQ) 進行跨行程通訊，實現 C# 應用程式與 MetaTrader 5 交易平台之間的資料交換和指令傳遞。

## 系統架構

系統由兩個主要部分組成：

1. **C# 交易管理應用程式**：負責資金管理、評價系統、策略管理、風險控制和績效統計等功能。
2. **MQL5 連接器**：在 MetaTrader 5 平台上執行，負責接收和執行來自 C# 應用程式的交易指令，並將市場資料和交易狀態回傳給 C# 應用程式。

## 通訊架構

系統使用 ZeroMQ 建立了三個主要的通訊通道：

- **MarketData (SUB)**：訂閱來自 MetaTrader 5 的市場資料
- **Command (REQ)**：向 MetaTrader 5 發送交易指令
- **StatusReport (PULL)**：接收來自 MetaTrader 5 的交易狀態報告

## 主要功能

- 資金管理
- 交易策略評價
- 策略管理
- 風險控制
- 績效統計
- 與 MetaTrader 5 的實時通訊

## 技術堆疊

- C# (.NET Core)
- ZeroMQ (NetMQ)
- MQL5
- MetaTrader 5

## 安裝與設定

1. 安裝 MetaTrader 5
2. 安裝 ZeroMQ for MQL5
3. 將 `CSharpConnectorEA.mq5` 複製到 MetaTrader 5 的 Experts 資料夾
4. 編譯並啟動 C# 應用程式
5. 在 MetaTrader 5 中附加 EA 到圖表

## 使用說明

1. 啟動 C# 應用程式
2. 在 MetaTrader 5 中啟動 EA
3. 系統將自動建立連接並開始交換資料

## 開發者

- 60610660