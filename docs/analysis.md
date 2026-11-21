# 原版 `小雅.exe` 逆向分析紀錄

- 分析日期：2026-11-22
- 工具：REA（Ghidra 12.2 後端）＋ PE 匯入表／資源解析，**僅靜態分析，沒有執行原程式**
- 目標：`小雅.exe`，737,280 bytes
- SHA-256：`0f825ce15b3ae629c21798d7bd093740fd4b0efcdbf2b81f4f10d35d020449a5`
- 格式：PE32（x86），易語言 **Super-EC** 靜態編譯（內含 MFC 與 MSVC CRT）

## 1. 這個程式在做什麼

針對線上遊戲 **ECO（艾米爾編年史）** 的自動技能按鍵工具：找出標題含 `eco` 的遊戲視窗，依使用者設定的間隔自動送出 F1～F6 技能鍵與滑鼠點擊。

## 2. 證據

### 2.1 介面與設定字串（GBK，位址 `0x47f5c0` 起）

```
'eco' | 未启动eco | /小雅身体配置.ini
F1~F6 | 设置1 | 真
技能时间1~6 | 设置2
鼠标1~6     | 设置3
延迟时间1~6 | 设置4
游戏编号 | 请选择进程 | 开始自动 | 保存数据 | .tmp | #eco | eco
```

`FUN_00404f7d` 在啟動時讀取 INI，依序還原：6 個勾選框（F1～F6，值為「真」表示勾選）、6 個文字框（技能時間）、6 個勾選框（鼠標）、6 個文字框（延遲時間）。

### 2.2 易語言 DLL 命令表（`0x4767e0`、`0x47f701`）

| 類別 | API |
|---|---|
| 鍵盤模擬 | `keybd_event`, `MapVirtualKeyA` |
| 延遲 | `CreateWaitableTimerA`, `SetWaitableTimer`, `MsgWaitForMultipleObjects`, `CloseHandle` |
| 訊息 | `PostMessageA`, `SendMessageA` |
| 視窗 | `FindWindowA`, `FindWindowExA`, `EnumChildWindows`, `IsWindow`, `GetWindowTextA`, `SetWindowTextA`, `ShowWindow`, `ShowWindowAsync`, `GetForegroundWindow`, `SetForegroundWindow`, `SetActiveWindow`, `GetWindowThreadProcessId`, `GetCurrentThreadId`, `AttachThreadInput` |
| 座標 | `GetCursorPos`, `ClientToScreen`, `GetClientRect` |
| 記憶體 | `OpenFileMappingA`, `MapViewOfFile`, `RtlMoveMemory` |

### 2.3 關鍵函式

| 函式 | 推論功能 |
|---|---|
| `FUN_004041bf` | 初始化：設定比對字串 `'eco'`、讀取設定（呼叫 `FUN_00404f7d`） |
| `FUN_00407442` | **尋找視窗**：以全域計數器 `DAT_004a71f8` 從 1 數到 6,000,000，每個值當作視窗代碼呼叫 DLL 命令（IsWindow／取標題），標題含關鍵字就回傳 |
| `FUN_00409347` | 反覆呼叫 `FUN_00407442`，把所有符合的視窗收集到陣列 `DAT_004a7204`（對應「請選擇進程／遊戲編號」） |
| `FUN_00403712` | 易語言常見的「滑鼠訊息」模組函式：`PostMessage` 送出 0x200～0x20A（移動、左／右／中鍵、雙擊、滾輪）；沒指定視窗時預設送往桌面 `Progman → SHELLDLL_DefView → SysListView32` |
| `FUN_00404f7d` | 從 INI 還原 24 個介面元件的狀態 |

### 2.4 版本資訊

- 別名：「暧床用小雅」
- 版權：「本人属所有生物所拥有-3- by-小雅」
- 備註：「丫的,看什么看吖!!!!!!我叉你!!」

## 3. CPU 使用率過高的原因

`FUN_00407442` 每次找視窗都要對最多 6,000,000 個可能的視窗代碼逐一呼叫 Win32 API，而且是經由易語言的 DLL 命令分派器間接呼叫，額外開銷更大。多開時要找的視窗變多，掃描就會頻繁觸發，CPU 長時間被佔滿。正確的做法是用 `EnumWindows`，只會列出實際存在的視窗（通常只有幾百個）。

## 4. 尚未確認／未知

- **`.\1.exe`**：`FUN_004041bf`、`FUN_004096a7` 把這個路徑傳給 `FUN_0040450c`（看起來在做位元組比對或檔案處理）。旁邊緊接著一份內嵌的 96×96 ICO（`0x4784fa` 起，易語言位元組集，長度 0x70BE），被設為某個控制項的圖片。沒有找到寫出或執行 `1.exe` 的直接證據，但也無法排除。
- **網路**：`WS2_32` 匯入屬於易語言執行環境，沒看到程式邏輯實際使用。
- **共享記憶體**（`OpenFileMapping`／`MapViewOfFile`）：屬於模組函式組合，沒找到具體用途。
- **「鼠標」「延遲時間」的確切語意**：只知道一個是勾選框、一個是數字欄位。
- 資料區有一段沒被引用的 GBK 粗話字串（`0x4784eb`），可能是未使用的常數。

## 5. 風險評估

沒有看到自我複製、開機自動啟動、竊取帳密或連線回傳的跡象。不過它是來源不明、沒有數位簽章的易語言程式，防毒軟體常會把這類程式標為可疑。
