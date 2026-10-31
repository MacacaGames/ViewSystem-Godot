# ViewSystem for Godot

Godot .NET 的 Page／State／Overlay 組合與視覺化編輯工具。Editor 與 runtime 共用 composer，採原生 Control、Container、Resource 與 EditorUndoRedoManager。

只依賴 **Godot 4.7.2 .NET**（桌面測試 SDK **8.0.427**）。不需要 Unity、GameSystem、ServerShared、FreeSql 或 MagicOnion。其他 Godot 版本尚未驗證。

## 安裝：Git submodule

在 Godot 專案根目錄執行：

```sh
git submodule add git@github.com:MacacaGames/ViewSystem-Godot.git addons/macaca_viewsystem
git -C addons/macaca_viewsystem checkout --detach v0.6.0
git add .gitmodules addons/macaca_viewsystem
```

若 Git 根目錄在 Godot 專案上一層，路徑要加上該專案資料夾。repository 根目錄就是 addon 內容，不要再嵌套一層 `addons/macaca_viewsystem`。請保留安裝名稱，場景與 Resource 使用這個固定路徑。

建置宿主 C# 專案後，到 **Project Settings → Plugins** 啟用 **Macaca ViewSystem**。預設 Godot C# 專案會自動包含 addon 的 C#；自訂 Compile Include 時加入 `addons/macaca_viewsystem/**/*.cs`。沒有額外 ProjectReference 或 NuGet 依賴。

新 checkout：

```sh
git submodule update --init --recursive
```

升級時先確認 addon 工作目錄乾淨，再 fetch 並 checkout 指定 tag，測試宿主後提交主專案的 gitlink。主專案固定的是 commit；tag 是選版入口，不會自動跟隨最新版本。開發 addon 時先在其中建立 branch、提交並 push，再更新主專案 gitlink。

## 安裝：ZIP

```sh
python3 Tools/package.py --output /path/to/output
```

輸出的 ZIP 內有 `addons/macaca_viewsystem/` 與 SHA256 manifest，可直接解到 Godot 專案根目錄。GitHub 自動產生的 Source code ZIP 是 repository 根目錄內容，須自行放到 `addons/macaca_viewsystem/`；兩者目錄結構不同。

## 可選：GodotEnv

第三方 [GodotEnv](https://github.com/chickensoft-games/GodotEnv) 可透過 `addons.json` 指定 Git tag：

```json
{
  "addons": {
    "macaca_viewsystem": {
      "url": "git@github.com:MacacaGames/ViewSystem-Godot.git",
      "checkout": "v0.6.0",
      "subfolder": "/"
    }
  }
}
```

執行 `godotenv addons install`。這是替代安裝方式；同一目錄只能選一個管理者，不要讓 GodotEnv 覆寫 submodule。此 repository 未以 GodotEnv 實測安裝；上例依其官方文件提供。GodotEnv 不是本 plugin 的依賴。

## 開始使用

開啟頂部 **ViewSystem → 新增 Catalog**，儲存 `.tres`，或按 **套件範例**。可選 Project Settings 字串 `macaca_viewsystem/catalog` 設定專案 catalog，沒有產品預設入口。

產品 Scene、Catalog、Model 與業務 Action 放在宿主專案，勿放進 addon。新增 `ViewHost` 並指定 Catalog，使用下方 C# API。

## Editor 工作方式

左側 **ListView** 以 State → Page 排列；Page 顯示所屬 State 字串，沒有 State 者明確標示。篩選／搜尋後仍保留群組標籤；中間關係圖可編輯群組連線。

右側是**單一捲動 Inspector**，不再切換五個分頁。頂部一直顯示 Page／State 與目前 Root／Element；元件清單置頂，下方版面、覆寫與事件均屬該對象。選 Root 才顯示頁面設定；每區可收合，來源 GUID／fileID 可按「Source」展開。工作區儲存 catalog，來源 Scene 在原生 2D Editor 儲存共用外觀。

新加入的 Element 若沿用子場景幾何，可按「獨立版面」擷取目前矩形，直接逐項編輯；「沿用子場景」交回原生幾何，兩者均支援 Undo。父 Container 管理時不允許再建立競爭版面。

Rect Transform 的縮圖按鈕開啟16種預設；固定軸顯示 Pos X/Y、Width/Height，伸展軸顯示 Left/Right 或 Top/Bottom。預設切換保持目前矩形；數值直接更新現有預覽並支援 Undo。Anchors 與 Advanced 收合，保留 Pivot、Scale、Rotation及排序。畫面使用 Unity Inspector 的向上 Y 與角度，底層仍儲存 Godot offsets／弧度。Shift/Alt預設修飾語義尚未提供。

預覽依可用寬高等比 fit，顯示實際縮放比例；「專注預覽」可隱藏本工具導覽與Inspector，再按一次恢復；外層 toolbar 自動換行，三欄可拖動分隔線，也可按「展開工作區」隱藏原生側欄。保持輸入解析度，不將縮放當成修改正式 Canvas 尺寸。

本介面借鑑 Unity RectTransform／ViewSystem 的工作方式，由 Godot Control／Resource／EditorUndoRedoManager 實作；沒有複製 Unity Editor 程式碼。

## 資料與 runtime

| 資料 | 用途 |
| --- | --- |
| `ViewCatalog` | 全部定義、Shared 預覽資料與可選 Action 宣告 |
| `ViewEntry` | ID、Kind、Scene、State 群組、排序、元件、Overrides、Events、預覽資料、動畫 |
| `ViewElementPlacement` | 穩定 ID、顯示名稱、NodeName、共用 Scene、ParentPath／SiblingIndex、此次放置的 Layout／Overrides／Events、GUID／fileID／原 ViewPageItem 身分 |
| `ViewElementLayout` | 放置根節點的 anchors／offsets／pivot／scale／rotation／ZIndex；Container 管理的項目留空 |
| `ViewPropertyOverride` | 相對目標節點、Godot 屬性、固定值或 Model key、查找 scope |
| `ViewEventBinding` | 相對目標節點、signal、Action ID |
| `ViewModelData` | 可序列化的預覽 key／Variant 資料 |

新增一個 `ViewHost` Control，指定 Catalog，然後：

```csharp
host.SharedModels = new ViewModels().Set("balance", "3,200");
host.ActionRequested += (action, origin) => HandleAction(action);
await host.ShowPageAsync("Welcome",
    new ViewModels().Set("title", "Hello").Set("actionLabel", "Continue"));

var dialog = await host.ShowOverlayAsync("Confirm",
    new ViewModels().Set("prompt", "Ready to continue?"));
await host.DismissOverlayAsync(dialog);
```

runtime 預設要求呼叫端提供 Model。只有明確開啟 `UsePreviewModels` 才使用 fixture，
repository 的完整 AuthoringDemo 啟用此選項。Page 切換時，相同 State 群組保留實例並重新綁定；Overlay 保留底下的 Page。
方法需在 Godot 主執行緒呼叫並 await，失敗以例外回報。State 接收目前 Page 的 context，
與它自己的獨立預覽 fixture 不同。

`ViewModels.Add<T>(model, key)` 可加入 typed model；節點實作 `IViewModelReceiver.Inject`
後使用 `context.Model<T>()` 取得。`PageFirst`、`PageOnly`、`SharedFirst`、`SharedOnly`
支援具名值與 typed model。這是明確介面注入，目前沒有 attribute reflection 或產生器。

套用順序：組裝元件及 sibling 順序 → placement Layout → Inject → 固定 Overrides → Model bindings → 掛入場景樹／`_Ready`。
目標不存在、屬性型別不符、缺 Model 或錯誤 signal 都會回報。Resource 子屬性例如
`material:blend_mode` 會先複製沿途的 Resource；Vector2、Color 請覆寫完整值。
事件第一版只支援零參數 signal，例如 `pressed`。Action 的業務含義由應用程式派送。

## Lifecycle、非同步 hook 與取消

節點可實作 `IViewElementLifeCycle`：

```text
OnBeforeShow → OnStartShow → ShowAnimation → OnShown
OnBeforeLeave → OnStartLeave → HideAnimation → OnHidden → 釋放
RefreshView：保留的 State 在換頁後重新綁定
```

`ShowPageAsync`／`ShowOverlayAsync` 不論是否設定 `ProjectTransitions` 都使用同一交易：準備、新State注入、入場及AfterReady成功後才commit；commit前取消／失敗保留舊頁，清理候選所有權並反序執行OnAborted。commit後的State刷新／舊頁清理錯誤用ViewNavigationCommittedException明確回報，不回復舊頁。

同步ActivatePage／ActivateOverlay先完成組裝與State入場準備再commit，保留入場／退場重疊；其Ready失敗發生於commit之後，不回復舊頁。完整取消、hook、cleanup及互斥合約見 [NAVIGATION.md](NAVIGATION.md)。

Show／Hide 動畫使用定義上的 `AnimationPlayerPath`、`ShowAnimation`、`HideAnimation`。
動畫須存在且不能循環；取消時停止等待並清理候選實例。

Editor 預覽不派送業務 Action、不呼叫 runtime lifecycle／show hooks。
只有明確實作 `IViewPreviewParticipant` 的節點會收到預覽 Inject。
Scene 裡的 Godot `[Tool]`／`@tool` 腳本仍會依引擎規則執行，必須自行保持 editor-safe。

## 儲存與相容範圍

Placement 的 `LayoutData` 使用原生 Resource metadata schema1，透過 `ViewElementLayout` 取得型別化存取。工作區負責組合與 placement layout；共享外觀直接在 Godot 2D Editor 編輯來源子場景。Container 與原生 Control 的最小尺寸規則仍有效。

支援 Page／State、Overlay、動態 Element、typed Model、Overrides、零參數 signal、生命週期與動畫。Unity MacacaViewSystem API 並非全部移植：任意 breakpoint／flag mask、safe-area 模擬、整個 item 的事件／覆寫剪貼簿、pooling、任意參數 signal、attribute injection 與 Shift／Alt preset 額外語義仍未提供。Transform Pick／Copy／Paste 不改來源身分、Overrides 或 Events。

Godot 資源 wrapper／GC 問題參考 [godotengine/godot#112067](https://github.com/godotengine/godot/issues/112067)。Catalog 在文件生命週期內保留 wrappers；熱重載以短暫 native references 交接，沒有全域永久快取。非互動 `--import`／`--quit`／`--export-*` 不建立預覽工作區。

## 驗證與維護

repository 本身不是遊戲專案；驗證工具建立可拋棄的 Godot .NET 宿主，不讀產品存檔：

```sh
python3 Tools/verify.py --godot /path/to/Godot --dotnet /path/to/dotnet --output /path/to/evidence
```

需要 Python 3.9+、Godot 4.7.2 .NET 與 SDK8。預設從 NuGet 還原；離線時可傳 `--nuget-source /path/to/pinned/nupkgs`。工具驗證乾淨宿主建置、命名空間隔離、實際 runtime 與 GUI Editor 範例、輸出 screenshot 與 JSON；GUI階段需要可用桌面。較完整產品整合／熱重載測試由消費端維護，不冒充這個 smoke 的覆蓋範圍。

修改之後重新執行驗證、更新版本／CHANGELOG，再建立不重用的 tag。原始移植來源與公告見 [NOTICE.md](NOTICE.md)、授權見 [LICENSE](LICENSE)。


## Authoring pickers（0.6）

Target、Property、Signal、Action旁的「…」提供可搜尋選單，「↗」定位預覽節點，或在檔案系統與Inspector顯示應用程式的派送宣告。Property從實際選取節點及Resource子路徑取得型別，選取後填入對應Value kind。仍可直接輸入路徑／值。帶參數signal顯示但停用，payload binding仍未提供。

Element的Parent只列本項目前已存在的Control，排除自身子樹及後續placement，支援Undo／Redo及存重開。Container擁有排版時必須先交回原生layout，不能同時套用placement Layout。定位框使用Node2D，不參與Container排版。

Catalog.Actions可選擇宣告ViewActionDefinition（Id、HandlerScript、HandlerMethod）；Editor以實際Script方法metadata核對並提供來源定位。宣告後，Events必須使用已宣告ID。沒有宣告時，Action選單取自既有bindings並保留文字輸入。這是宿主提供的派送對照，runtime仍由ActionRequested交給應用程式處理；plugin不依賴任何遊戲類別、不建立service locator。應用程式須確保其dispatcher處理每個已宣告ID。
