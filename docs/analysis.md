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

### 2.5 送鍵方式（第二輪分析）

易語言呼叫 DLL 命令的方式是 `MOV EAX, 編號` 再 `CALL 0x409b72`，編號就是 2.2 命令表的順序（0=`MapVirtualKeyA`、1=`keybd_event`、6=`PostMessageA`、8=`ShowWindowAsync`、9=`ShowWindow`、0x0E=`SetForegroundWindow`、0x0F=`SendMessageA`、0x13=`GetForegroundWindow`、0x1B=`SetWindowTextA`）。用位元組樣式 `B8 xx 00 00 00 E8` 搜尋 `.text` 段得到：

| 函式 | 用到的 DLL 命令 | 功能 |
|---|---|---|
| `FUN_004030a9`、`FUN_004033e2` | `SendMessageA`（各 9 處） | 鍵盤訊息：模式 1=`WM_CHAR`、2=SYSKEY 按下＋放開、3=`WM_KEYDOWN`、4=`WM_KEYUP`、5/6=`WM_SYSKEYDOWN/UP`、其他=按下＋放開；可選擇用 `EnumChildWindows` 一併送給子視窗 |
| `FUN_004019bd`、`FUN_004020d5` | `PostMessageA`（各 9 處） | 同上，PostMessage 版本（`004020d5` 是子視窗回呼） |
| `FUN_00401274` | `keybd_event`（8 處） | 前景實體按鍵模擬 |
| `FUN_00403712` | `PostMessageA` | 滑鼠訊息 0x200～0x20A |
| `FUN_00402405` | `ShowWindowAsync`／`ShowWindow` | 顯示／隱藏視窗 |
| `FUN_00409614` | `GetForegroundWindow`、`SetWindowTextA` | 修改視窗標題 |

**技能施放 `FUN_00402f45(按鍵, 延遲, 是否點滑鼠)`**（由技能迴圈 `FUN_004025a9` 呼叫，後者是透過執行緒間接啟動的）：

```
FUN_004030a9(hwnd, vk, 3)                    ; SendMessage WM_KEYDOWN
延遲 30ms (0x1e)
FUN_004030a9(hwnd, vk, 4)                    ; SendMessage WM_KEYUP
if 是否點滑鼠 == 1:
    x = 視窗寬 / 2.0, y = 視窗高 / 2.0       ; 常數 0x478499 = 2.0，寬高來自 GetClientRect (FUN_00404d46)
    FUN_00403712(hwnd, x, y, 左鍵, 按下)      ; PostMessage
    延遲 30ms
    FUN_00403712(hwnd, x, y, 左鍵, 放開)
延遲「延遲時間」
```

**Ctrl+T `FUN_004011c5`**：`keybd_event(Ctrl 按下)` → 50ms → `PostMessage(hwnd, 'T', KEYDOWN)` → 50ms → `PostMessage(hwnd, 'T', KEYUP)` → `keybd_event(Ctrl 放開)`。由「開始」`FUN_004078c4` 呼叫；「停止」`FUN_00407a38` 會在旗標 `DAT_004a7198 == 1` 時呼叫。停止時另外會停掉 4 個執行緒（`DAT_004a7190/71f0/71f4/71ec`），並用 `ShowWindow(hwnd, 1)` 還原遊戲視窗。

**結論：技能鍵和滑鼠是背景送出，只有開始時的 Ctrl+T 用到前景的 `keybd_event`。** `SetForegroundWindow`／`AttachThreadInput` 雖然有匯入，但技能流程沒有用到。

## 3. CPU 使用率過高的原因

`FUN_00407442` 每次找視窗都要對最多 6,000,000 個可能的視窗代碼逐一呼叫 Win32 API，而且是經由易語言的 DLL 命令分派器間接呼叫，額外開銷更大。多開時要找的視窗變多，掃描就會頻繁觸發，CPU 長時間被佔滿。正確的做法是用 `EnumWindows`，只會列出實際存在的視窗（通常只有幾百個）。

## 4. 尚未確認／未知

- **`.\1.exe`**：`FUN_004041bf`、`FUN_004096a7` 把這個路徑傳給 `FUN_0040450c`（看起來在做位元組比對或檔案處理）。旁邊緊接著一份內嵌的 96×96 ICO（`0x4784fa` 起，易語言位元組集，長度 0x70BE），被設為某個控制項的圖片。沒有找到寫出或執行 `1.exe` 的直接證據，但也無法排除。
- **網路**：`WS2_32` 匯入屬於易語言執行環境，沒看到程式邏輯實際使用。
- **共享記憶體**（`OpenFileMapping`／`MapViewOfFile`）：屬於模組函式組合，沒找到具體用途。
- ~~「鼠標」「延遲時間」的確切語意~~：已在 2.5 確認。
- ~~Ctrl+T 在 ECO 裡的用途~~：使用者確認是**切換拍照模式（隱藏 UI）**。這說明了原版的設計：開始時隱藏 UI，點視窗中央才不會點到介面；停止流程用旗標 `DAT_004a7198` 判斷要不要再送一次 Ctrl+T，把 UI 切回來。
- **技能迴圈 `FUN_004025a9` 如何排定各技能的「技能時間」**：它是透過執行緒間接呼叫的，這次沒有追進去。
- 資料區有一段沒被引用的 GBK 粗話字串（`0x4784eb`），可能是未使用的常數。

## 6. ECO 客戶端封包格式（`eco.exe` 逆向）

另外分析了遊戲本體 `eco.exe`（SHA-256 `02d709ed…bbe9abad`）的資料封包，用來取得副職業技能表。

### 6.1 `.hed` / `.dat`

- 路徑表在 `0xcdeaf8`（`data\effect\effect.hed`、`data\xls\table.hed`…），由 `FUN_00554af0` 傳給封包模組 `FUN_009b2af0`。
- `.hed` 每筆 12 bytes：`u32 位移`、`u32 儲存大小 | 0x80000000（壓縮旗標）`、`u32 原始大小`。
- 讀取：`FUN_009b1e10` → `FUN_009b2690`（檔名 → 編號）→ `FUN_009b1b30` → `FUN_009b0800`；壓縮旗標成立時呼叫解壓 `FUN_009d5ef0`。
- **第 0 筆是檔名索引**：`u32 檔案數` + 以 `\0` 分隔的檔名，依序對應第 1..N 筆（`table.hed` 有 183 個 CSV）。

### 6.2 壓縮格式（`FUN_009d5ef0`）

改良版 LZW，位元串流 MSB 先：

| 函式 | 作用 |
|---|---|
| `FUN_009d5e50` | 初始化：下一個碼 = 0x100，碼長 = 1，門檻 = 2，字典上限 0x2000，NIL = 0x2000 |
| `FUN_009d5da0` | 讀 n 位元 |
| `FUN_009d5ef0` | 主迴圈：每次先檢查 `門檻 ≤ 下一個碼 − 0x100` 時碼長 +1、門檻 ×2；讀 1 位元旗標，**0 → 讀 8 位元字元**，**1 → 讀「碼長」位元 + 0x100** 當字典碼；沿父節點取出字串並把走過的節點移到 LRU 最新端 |
| `FUN_009d6150` | 新增字典：從「前一個碼」開始，沿著「這次輸出的字串」逐字往下走，缺的節點就新建（前一段長度 + 本段累計超過 100 時停止）；字典滿了就淘汰 LRU 最舊的節點 |
| `FUN_009d5cd0` / `9d5c80` / `9d5c30` | LRU 插入 / 移除、從兄弟串列移除 |

C# 移植在 `src/EcoArchive.cs`；`table.hed` 184 筆全部可解（解出大小與 `.hed` 記錄一致）。

### 6.3 副職業表

- `dualjob_info.csv`：12 個副職業**體系**（1 劍士系 … 9 礦工系 … 12 商人系），各含 4 個職業 ID。
- `dualjob_skill_learn.csv`：`體系ID, 技能ID, 技能名, 所屬職業ID, Lv1..Lv10 需要的副職業等級`（0 = 該等級學不到），共 933 列。
- 小雅依此只列出副職業學得到的技能，並限制等級上限。

## 5. 風險評估

沒有看到自我複製、開機自動啟動、竊取帳密或連線回傳的跡象。不過它是來源不明、沒有數位簽章的易語言程式，防毒軟體常會把這類程式標為可疑。
