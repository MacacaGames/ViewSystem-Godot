# ViewSystem Godot 使用手冊

適用目前工作目錄的 **0.7.0-dev（未發佈）**，更新日期：2026-11-02。安裝需求與版本取得見 [README](README.md)。已發佈的 `v0.6.0` 不包含本手冊全部 Editor 功能；使用前先核對實際 checkout，而非只看主專案的版本名稱。

這份文件同時供使用者與 agent 操作。前半部依日常工作排列，後半部說明 runtime、資料與維護規則；操作差異與限制直接寫在對應流程內。UI 名稱可能隨 Godot 語言設定翻譯，程式中的名稱以原始 API 為準。

| 要做的事 | 入口 |
|---|---|
| 建第一個頁面 | [快速開始](#快速開始) |
| 選頁面、管理元件 | [兩層導覽與新增](#兩層導覽與新增) |
| 編輯一般 Control 或頁面局部版面 | [兩種 Rect Transform](#兩種-rect-transform) |
| 改文字、顏色、Model、事件 | [Override](#override)、[Event](#event) |
| 拖場景、匯入 instance 差異 | [Reference 與差異匯入](#reference-與差異匯入) |
| 寫遊戲接線與動態 UI | [Runtime 與 Model](#runtime-與-model)、[Pool 與動態元件](#pool-與動態元件) |
| 查問題或修改套件 | [常見問題](#常見問題)、[Agent 與維護流程](#agent-與維護流程) |

## 核心概念

| 名稱 | 意義 |
|---|---|
| `ViewCatalog` | `.tres` 文件；包含全部定義、全域設定、預覽資料、Action 宣告。 |
| Page／`ViewKind.Page` | 主要頁面；一個 host 同時只有一個目前 Page。 |
| Overlay／`ViewKind.Overlay` | 疊在 Page 上的頁面；保留底頁，可有多個，支援 Modal。預設清單及 Page 篩選都包含它。 |
| State／`ViewKind.State` | 可跨頁保留的共用 UI。Page／Overlay 與 State 的 `State` 群組字串相同且非空時建立關聯；不是 Godot 的 Animator state。 |
| Catalog Element／`ViewKind.Element` | 可動態建立、借還的模板；由第一層種類篩選找到。 |
| ViewElement／ViewPageItem／placement | `ViewElementPlacement`：某一個 Page／State／Overlay 使用某個 Scene 的紀錄，保存在 `ViewEntry.Elements`。與 Catalog Element 定義不同。 |
| Source Scene | 共用 `.tscn`／`.scn`，根節點必須是 Control。共用外觀在原生 2D Editor 修改。 |
| `ViewHost` | 掛在實際場景中的 Control；指定 Catalog 後負責組裝、導航、Model、生命週期與回收。 |

Catalog 的 Entry ID 須唯一，名稱與查找採大小寫區分。placement 的 `Id` 用於穩定識別；`DisplayName` 是清單名稱，`NodeName` 是組裝後節點名稱。一般改名只改 `DisplayName`，不要順手重建 ID 或改寫來源 GUID／fileID。

## 快速開始

1. 使用 Godot **.NET** 開啟宿主專案，完成 C# build，在 **Project Settings → Plugins** 啟用 **Macaca ViewSystem**。
2. 頂部選 **ViewSystem**，按 **套件範例**先查看內建 `Examples/Catalog.tres`。它是簡單範例；要修改時先複製到宿主專案。
3. 準備 `Welcome.tscn`：建立 Control 根節點、設為 Full Rect，作為 Page 的來源場景。
4. 準備可重用的 `WelcomePanel.tscn`，例如：

   ```text
   WelcomePanel (Control)
   ├── Title (Label)
   └── Continue (Button)
   ```

   給根節點可見尺寸，例如400×160；將 Title 與 Continue 排開，按鈕 Text 設為 Continue。

5. 按 **新增 Catalog**，將 `.tres` 存在宿主，例如 `res://UI/Views.tres`；產品資料不要存進 addon。
6. 在左側 **＋ 新增 → Page**，選 `Welcome.tscn`，將 ID 設為 `Welcome`。
7. 雙擊 `Welcome` 進入元件層，按 **＋ Element** 選 `WelcomePanel.tscn`。先選中新增元件，再改名稱、Parent 或版面。
8. 在該元件的 **覆寫 → ＋ Override**，雙擊 `Title` 節點，再雙擊 `text` 屬性，輸入固定文字；Enter／離焦後更新預覽。
9. 按 ViewSystem 工具列的 **儲存**，保存 Catalog。來源 Scene 的修改則在原生 Editor 保存，兩者是不同文件。
10. 在遊戲場景新增 `ViewHost` Control、指定剛才的 Catalog、設定尺寸或 Full Rect。固定值頁面可設 `InitialPage = Welcome` 自動開啟；需要 Model 時則由應用程式準備資料後呼叫 `await host.ShowPageAsync("Welcome", models)`。

若希望開工具就載入自己的文件，可設定 Project Settings 字串 `macaca_viewsystem/catalog`。這是可選設定，套件沒有遊戲專用預設路徑，也不要求建立 Autoload。

## 兩層導覽與新增

第一層單擊 Page／State／Overlay，只顯示本體設定，例如 Scene、State 群組、動畫、Safe Area、導航與幀率。**此層不顯示 placement 的版面、Override 或 Event 表單。**

雙擊項目進入第二層，只列該項目擁有的 placements。單擊元件後，右側顯示該 `ViewElementPlacement` 的設定；尚未選元件時右側保持空白。上方頁名只表示位置；按 **← 返回** 或清單中的 Escape 回第一層，恢復原先搜尋／篩選／捲動。

- 第一層 **＋ 新增**先選 Page／State／Overlay／Element，再選來源 Control Scene；Graph 的新增使用相同流程。
- 第二層提供新增、移除、排序、複製、場景選取。`ParentPath` 相對於頁面根節點，`.` 表示根；選擇器只列目前可用的 Control。
- 一個元件若要放在另一個 placement 下面，先讓父 placement 排在前面；不可引用自己、自己的子樹或尚未組裝的後續 placement。
- State 關聯可編輯 `State` 群組，或在中間關係圖連接／斷開；不要用 Scene 路徑或顯示名稱代替群組。
- 新增後會顯露並選中項目，Undo／Redo 還原編輯位置。搜尋後刪除保留搜尋，只選可見鄰項或留在空態。

右鍵提供重新命名、複製、移除、開啟來源。清單／Graph 焦點下 F2 改名、Ctrl／Cmd+D 複製、Delete／Backspace 移除；文件區 Ctrl／Cmd+Z、Shift+Z 撤銷／重做。文字框保留自身文字編輯行為。Unique placement 不能在同一頁複製。

## 兩種 Rect Transform

| 操作位置 | 修改與保存的資料 | Undo 歸屬 |
|---|---|---|
| 原生 **2D Editor → Control Inspector → Rect Transform** | 該場景 Control 的原生屬性，保存到 `.tscn`／`.scn`。修改來源 Scene 會影響其使用者；在 instance 編輯則是原生場景差異。 | 場景歷史 |
| **ViewSystem → 選中 placement → 版面** | 該次放置的 `LayoutData`／breakpoint，保存到 Catalog。 | Catalog 歷史 |

### 一般 Control

外掛啟用後，選取正在編輯場景中的單一 Control、Button、Label 或其他 Control 子類，即可使用 Rect Transform；不需 Catalog、ViewPageItem 或額外掛 Script。

- 固定軸：Pos X／Pos Y、Width／Height。伸展軸：Left／Top／Right／Bottom。
- 縮圖開啟 **16 種 Anchor preset**。單擊保持矩形；Shift 同時改 Pivot，Alt 同時對齊位置，兩者可合用。
- Anchors 可展開 Min／Max 與拖曳圖；另有正規化 Pivot、Scale、Rotation，Advanced 內有 Z Order。
- Copy／Paste 複製本工具的矩形資料；不複製其他屬性或 script。
- Y 與 Z 角度沿用 Unity Inspector 的方向；底層仍是 Godot offsets、ratio pivot 與弧度。直接用程式操作原生 Control 時，仍遵守 Godot 座標。
- 未編輯時採短數值顯示，點入使用完整值；顯示取整不改寫保存精度。舊像素 Pivot 會與 ratio 合併顯示；編輯尺寸／Pivot 時才轉成該 Control 的 ratio 表示。
- 文字 Enter／離焦提交，拖曳中預覽、釋放後一筆 Undo，Esc 取消。2D 畫布或其他原生工具的幾何修改會回映到欄位。

父 Container 管理的 anchor／位置／大小會停用，改用原生 minimum size、Container sizing 等欄位；按 **選取父 Container** 可回到排版來源。多選、Node2D、遠端及 detached／ViewSystem 預覽物件不注入此介面，沿用原生流程。這不是 Unity 3D RectTransform 相容層。

### Placement 版面

預設可沿用子場景的幾何。需要這一頁獨立設定時按 **獨立版面**；要交回來源幾何，按 **沿用子場景**。Container 直接管理的子項不建立競爭的 placement Layout。

其 Rect Transform 使用相同方向與 preset 操作；Pick 擷取預覽中的矩形，Copy／Paste 只轉移版面。Advanced 的 Flags 決定要覆寫哪些幾何欄位，未勾選的欄位保留來源值。

Catalog 可定義 breakpoint 名稱；在預覽工具列選名稱後，擷取／移除該 placement 的斷點版面。runtime 使用 `SetBreakPoint(name)`、`RemoveBreakPoint(name)`、`ClearBreakPoint()`。多個名稱同時啟用時，placement 中第一個符合的 authored breakpoint 勝出，並非依螢幕尺寸自動猜測。

## Override

1. 在第二層選中元件，按 **＋ Override**，或目標分組旁的 **＋**。
2. 在目標 Tree 雙擊節點，再在屬性 Tree 雙擊屬性。`.` 表示此元件根節點；例如 `Title` 是元件內的 Label。
3. 新項目會被選中並聚焦值欄位；依型別顯示文字、數值、range、enum／flags、色彩、向量、Resource、陣列等原生 UI。
4. 編輯結束即驗證及更新預覽，正常流程不需要再按套用。Tree 同列顯示值，屬性選擇器的藍色圓點表示已有 Override。

雙擊已存在的相同 target／property 只定位既有 binding，不重複新增。父分組只負責瀏覽，不改寫已選 binding 的 Target；選錯可返回／取消，回到先前表單與草稿。

Property 使用 Godot 屬性路徑，例如 `text`、`modulate`，不是任意 C# 私有欄位名稱。自訂 Godot C# 屬性用 `[Export]` 提供序列化與編輯 metadata。Resource 子屬性可用 `material:blend_mode`；套用時會複製沿途 Resource，避免改到共用資產。Vector2／Color 等以完整值覆寫。

### 固定值與 Raw Value

預設是型別欄位。只有明確開啟 **Raw Value** 才設定 Model 表達式，例如：

```text
{PageModel.string["title"]}
{SharedOnly.int["coins"]}
```

這些是 **typed model** 查找；相對應資料需以 `Add<T>(value, key)` 提供。前者採 PageFirst 查找，後者只查 Shared；`PageOnly` 可禁止回退。型別名歧義時使用完整型別名。

在普通文字欄位輸入同樣的 `{...}` 仍是固定文字；切換 Raw 顯示本身不會改資料。既有 Model 綁定保留 Raw，必須先明確改成固定值，才可切回原生欄位。舊 `ModelKey`／`Scope` 仍支援，但不要將它與 `ModelExpression` 同時設定。

預覽的 `ViewModelData.Values` 是 key／Variant fixture，不能據此推論任意 typed Model 已可在 Editor 建構。typed binding 應由實際 runtime 注入驗證；缺少 Model、錯誤表達式或型別不符會回報，不以空字串掩蓋。

Override 的 Copy All／Paste All 深拷貝該組規則；Import／Export 使用 `ViewOverrideSet.tres`，與 Rect Transform 的 Copy／Paste 不同。

## Event

1. 在元件的 **事件 → ＋ Event**，雙擊目標節點，再雙擊 Signal，例如 Button 的 `pressed`。
2. 新 Event 是停用草稿；依序選 **接收對象 → 方法**，選到有效設定後才啟用。
3. 編輯結束自動提交，Tree 顯示 `signal → action`。雙擊既有項目定位原資料。

| 接收方式 | 設定與執行 |
|---|---|
| Catalog Actions | `Catalog.Actions` 宣告 ID、HandlerScript、HandlerMethod，Editor 核對 script method metadata；runtime 由宿主的 `ActionRequested`／`ActionWithPayloadRequested` 派送。宣告本身不會自動呼叫 script。 |
| C# 本地方法 | 方法標記 `[ViewSystemEvent]` 並明確註冊 AOT schema。選單顯示簽名，未註冊／不支援的項目停用。 |
| GDScript 方法 | 選取可用接收節點的方法；目前支援零參數，或與 Signal 參數數量／型別相容的簽名。不任意推測 optional／vararg 對應。 |

C# 方法回傳 `void`，可不帶參數，或接受一個相容的 Node／Control origin，或 `ViewActionEvent`。不要建立同名多載讓 Editor 猜選。

**進階**才顯示 Action／Handler 字串及 ForwardArguments。Signal 支援 0–8 個參數；需要 payload 時用支援的接收方式並開啟轉送。若 Catalog 沒宣告 Actions，選單只列已有 binding 的 ID；新 ID 要先宣告，或明確使用進階輸入，不會憑空產生業務命令。

以下 script 可掛在 `WelcomePanel` 根節點，使方法可供選擇。static 註冊讓 Editor 建構節點時也有 schema；應用程式亦可在建立 views 前集中註冊。

```csharp
// file: WelcomePanel.cs
using Godot;
using MacacaGames.ViewSystem;

[Tool]
public partial class WelcomePanel : Control, IViewAttributeReceiver
{
    static WelcomePanel() => ViewAttributeBindings.Register<WelcomePanel>();

    [ViewSystemEvent("Navigation")]
    private void OnContinue(Control origin)
    {
        GD.Print($"Continue from {origin.Name}");
    }
}
```

來源 Scene 既有的 signal 連線會保留。相同業務行為不要同時用來源連線、Attribute button event 與 Catalog Event 重複綁定。

## Reference 與差異匯入

**新增 placement**：將 FileSystem／Inspector 的 PackedScene，或本地 Scene dock 的 instance 根 Control，拖到第一層的 Page／State／Overlay、Graph 的對應節點，或第二層元件清單。

**合併到既有 placement**：先選中該元件，再把同來源 instance 拖到右側元件設定的共用 instance reference 入口。也可在 Scene dock 選 instance，再按該入口的 **使用場景選取**。左側同名按鈕用於新增元件，右側用於合併，作用範圍由位置決定。

- PackedScene 建立 placement；instance 則先比較直接來源，列出 Override／Event／Layout 差異。
- 用全選／全不選及逐列勾選決定匯入範圍，確認鈕顯示本次數量。取消不改資料，確認整批是一筆 Undo。
- 比較 inherited Scene 的有效預設，不把 variant 本來的設定再次當成 instance 差異。
- 既有元件只接受同來源合併；來源不同請新增 placement 或明確更換來源，不靜默替換。
- 結構、script、外部 receiver、bound callable 等不當成一般可套用屬性；依報告回到原生 Scene 工具處理。

可重用變體用 Godot inherited Scene；只有某頁需要的值差異用 placement Override。這兩種用途不用強行合併成 Unity Prefab Variant。

## 編輯、預覽與儲存

文字 Enter／離焦、選項選定、數值拖曳結束會更新預覽並建立一筆 Undo；Esc 還原本次表單。草稿按 owner 與 binding 身分分開保存，返回原物件可繼續。Save 先驗證全部待處理草稿，通過後原子套用，失敗不先寫入前面幾筆。

ViewSystem 的 **儲存**保存 Catalog；原生場景的保存／Ctrl／Cmd+S 處理 Scene。一般 Control Rect Transform 使用場景歷史，ViewSystem 元件設定使用 Catalog 歷史。不要依某個視窗的星號推論另一份文件已保存。

預覽尺寸提供12組固定解析度。選單的 **＋ 自訂尺寸…**建立可重選的具名 preset，支援編輯、移除、去重及旋轉；寬／高範圍64–4096。自訂清單與選取存於本機專案 Editor metadata，跨 Editor 冷開／DLL 重載保留，不寫入 Catalog，也不代表可共享的專案資產。

預覽等比 fit；專注預覽／展開工作區只調整編輯空間，不改正式輸出解析度。可選 Overlay、breakpoint、Safe Area 模擬；一般元件修改保留有效的 Overlay 預覽。

預覽不派送業務 Action，也不執行 runtime lifecycle／show hooks。只有實作 `IViewPreviewParticipant` 的節點收到預覽 Inject；Scene 的 `[Tool]`／`@tool` 仍按 Godot 規則執行，需保持 editor-safe。

## Runtime 與 Model

在 Godot 主執行緒使用已掛入場景的 ViewHost。需要先注入 Model 時，將 InitialPage 留空，避免 host 在 `_Ready` 自動開頁早於資料準備。指定 Catalog 後，await 導航及離場，並由呼叫端處理例外／取消。以下 ID 與 typed binding 須事先在 Catalog 建立；`Confirm` 為 Overlay。

```csharp
// file: ViewSystemGuideExamples.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using MacacaGames.ViewSystem;

public static class ViewSystemGuideExamples
{
    public static async Task<ViewInstance> OpenDemoAsync(
        ViewHost host, CancellationToken token = default)
    {
        host.SharedModels = new ViewModels().Add<int>(3000, "coins");
        await host.ShowPageAsync("Welcome",
            new ViewModels().Add<string>("Hello", "title"), token);
        return await host.ShowOverlayAsync("Confirm",
            new ViewModels().Add<string>("Continue?", "prompt"), token);
    }

    public static Task CloseDialogAsync(ViewHost host, ViewInstance dialog,
        CancellationToken token = default) => host.DismissOverlayAsync(dialog, token);

    public static Guid AddTemporaryTint(ViewInstance view) =>
        view.AddRuntimeOverride(new ViewPropertyOverride {
            Target = new NodePath("."), Property = "modulate",
            Kind = ViewValueKind.Color, Color = Colors.LightBlue
        });

    public static async Task UseRowAsync(ViewHost host, Control mountedParent,
        CancellationToken token = default)
    {
        // Catalog must contain an Element entry with ID "Row".
        using var pool = host.CreateRequestedPool("Row");
        var row = await pool.RequestAsync(mountedParent,
            new ViewModels().Add<string>("Item", "title"), token);
        try { GD.Print(row.Name); }
        finally { await pool.ReturnAsync(row); }
    }
}
```

關閉 Overlay 使用回傳的 instance；不必刪除底頁。臨時 Override 的 Guid handle 可用 `view.RemoveRuntimeOverride(handle)` 移除單筆，恢復前一層；`ResetRuntimeOverrides()` 清除臨時層，`ApplyOverrides()` 仍可使用。

| 資料入口 | 對應用途 |
|---|---|
| `ViewModels.Set(key, Variant)` | key／Variant 值，供 `context.Value(key)` 或舊 `ModelKey` binding 使用。 |
| `ViewModels.Add<T>(model, key)` | typed Model，供 `context.Model<T>(key)`、Attribute 注入及 Raw typed expression 使用。不能與 Set 混為同一儲存區。 |
| `ViewModelData.Values` | 可序列化的 key／Variant 預覽 fixture。runtime 只有明確開啟 `UsePreviewModels` 才使用。 |
| `SharedModels` | 跨頁共享資料；`SetSharedModels(...)` 更新後會 RefreshAll；直接改 SharedModels 後依需要呼叫 Refresh。 |

節點可實作 `IViewModelReceiver.Inject(ViewContext)`，用 `context.Value`／`context.Model<T>` 讀資料。Scope 有 PageFirst、PageOnly、SharedFirst、SharedOnly；對應 Attribute 建構參數使用 `InjectScope` enum，Model API 使用 `ViewInjectScope`。

Attribute 模式以 `IViewAttributeReceiver`、`[ViewElementInject]`、`[OverrideProperty]`、`[OverrideButtonEvent]` 設定。建立 views 前呼叫 `ViewAttributeBindings.Register<T>()`；有 Attribute 的基底類別另行註冊，保留 AOT／trimming 需要的成員。注入不可寫 readonly field；有注入的 property 必須可寫。這是明確註冊的反射流程，不需要產生器，也不等於已驗證每個行動 AOT 平台。

常用查詢／更新包括 `CurrentPage`、`Overlays`、`States`、`IsFullPageLive`／`IsOverlayLive`／`IsStateLive`、`GetCurrentPageElement`、`GetPageElement`、`GetStateElement`、`RefreshPageModels`、`RefreshFullPage`／`RefreshOverlayPage`／`RefreshAll`。需要相容的 fluent 流程可用 `FullPageChanger().SetPage(id).SetPageModel(...).Show()`，Overlay 使用 `OverlayPageChanger()`；Scene 內按鈕也可加入 `ViewNavigationButton`，從原生 Inspector 選目的地。

## 導航、動畫與生命週期

預設 `Catalog.NavigationMode = UnityTiming`：準備成功後 commit，再依 Page 的 WithPrevious／AfterPrevious／Custom 決定入場與退場的重疊／等待。commit 後失敗以 `ViewNavigationCommittedException` 回報，不能保證恢復已退場的舊頁。需要候選入場成功才 commit 時，明確選 `Transactional`；完整合約見 [NAVIGATION](NAVIGATION.md)。

裸 async 呼叫與 fluent 預設不同：裸 API 保留 queue／rebuild 語意；fluent 預設重用相同 ID、使用點擊保護與 unscaled 時間。可用 `ViewNavigationOptions`／fluent 選擇模式、重播、忽略動畫、排序、自訂已掛入的子 root、callback 等。OnChanged 在 commit，OnComplete 在入場與 AfterReady 成功後。

同步 `ActivatePage`／`ActivateOverlay` 是立即啟動入口，仍須觀察回傳的 Ready；不要把呼叫返回當作全部動畫完成。`DrainTransitionsAsync()` 等候已觀察的轉場，`ShutdownAsync()` 取消並釋放所有權。hook 不要在持有同 host 導航流程時遞迴 await 該 host 的導航或 shutdown。

使用原生 AnimationPlayer 時設定 `AnimationPlayerPath`、ShowAnimation、HideAnimation，所選動畫須存在且不能循環。ShowTransition／HideTransition 另支援 ActiveSwitch、Fade、Tween 或自訂 `IViewTransitionParticipant`；AnimationTree 由專案 adapter 接手，不能讓兩套驅動同時寫相同動畫。placement 可設定 DelayIn／DelayOut、transfer duration／ease、ManualOnly 與平台 ExcludeFeatures。

組裝與綁定順序為：元件／sibling → placement Layout → Inject → 固定 Override → Model binding → 掛入 SceneTree／`_Ready`。保留的 State 會重綁 context 並 Refresh；目前 Page 與所有 Overlay 都參與 State 引用計數。

`IViewElementLifeCycle` 提供 OnBeforeShow、OnStartShow、OnShown、OnBeforeLeave、OnStartLeave、OnHidden、RefreshView 等階段。Godot `_Ready` 只執行一次，不能用來代表每次借用。非同步 show hook 使用 `IViewPageShowHook`，可搭配 order／timeout／phase callback；自訂動畫及 hook 必須遵守傳入的 CancellationToken，並清理自己的外部副作用。

## Pool 與動態元件

`ViewElementPolicy` 以 PackedScene identity 共用 pool，同 Scene 在一個 host 需用一致政策。Recovery 為 KeepForever、KeepN、DestroyOnRecovery；預熱不掛 SceneTree、不提早呼叫 `_Ready`，使用中的 Unique 不會預熱出第二個實例。

Unique 借出同一實例，關閉後依 LIFO 還給仍有效的 owner，沒有 owner 才回 pool。借用轉移時會重設版面／Override、切換事件 owner；不要保留已交給其他 owner 的 Control，或自行 QueueFree 仍由 pool 管理的實例。

巢狀 singleton 以最近的 Control 作為獨立 unit；可在該 Control 下加入 `ViewSingleton` 或自訂 `IViewElementSingleton` Node。`CreateNestedSingletonPool(control)` 可獨立借還；一般手動局部顯示可用 ViewElementGroup／相關 show API。

Catalog Element 的 `CreateRequestedPool(id, owner, mode)` 可包含 template 的 Source Scene 與 `Elements`，使用相同組裝及事件流程。parent 必須已掛入 SceneTree，歸還交回同一 requested pool。

| requested mode | 用途 |
|---|---|
| ReturnToGlobalPool | 歸還到 host 全域池。 |
| DestroyWithOwner | owner-local 管理與銷毀；不接受 Unique／singleton，含巢狀 unit。 |
| UseChildPolicy | 依子元件政策回收。 |

`RequestAsync`／`ReturnAsync`／`PrewarmAsync` 負責非同步借還；`ViewElementLifetime` 是永久實例 lifetime，普通回收不取消它。每次顯示另有 token／ownership；回收清理用 `IViewElementPoolLifeCycle.OnBeforeReturnToPool`，永久釋放可登記 OnDispose。return／取消的清理可能彙報多個錯誤，不能忽略為「一定已安全歸還」。

## Safe Area、Focus 與多螢幕

Catalog 的 SafeArea 可全域啟用，頁面可用 `UseGlobalSafeArea` 或自己的設定，選擇四邊與 flip。runtime 依 host 所屬原生視窗／螢幕及 viewport 變化更新；可用 `DisplayMetricsProvider` 接平台資料。離屏 SubViewport 沒有唯一實體螢幕，需 adapter 或明確使用 `UseSimulatedSafeArea`。Editor 模擬不取代手機／多螢幕硬體驗收。

啟用 NavigationEnabled 後可指定 FirstSelected、FocusLinks、StateNavigation；Modal Overlay 管理底層 focus，關閉後恢復仍有效的焦點。Overlay 排序不只改 ZIndex，也同步原生輸入的 sibling 順序。

只有 `IsPrimaryHost = true` 的目前 Page／State 決定全域 `Engine.MaxFps`，Overlay 不介入；shutdown 還原接管前值。避免把多個 host 都當成全域幀率控制者。

## 工具與 Unity 資產

工具列 **⋯** 提供 Catalog 全域設定、ViewHost Scene 產生、Overlay 排序、ID bake、匯入、Verifier、Pool Advisor 等入口。

- Verifier 可聚合問題並定位，移除失效規則仍可 Undo。
- `[ViewId]` 提供 catalog-aware ID 選單；ID bake 檢查識別字衝突，不覆寫手寫 C#。
- Pool Advisor 是 dry-run 建議，不自動改 policy；runtime Capture 從 debugger 取得主 host 快照或匯入 JSON。至少兩個完整 baseline → open → returned 週期才足以判斷重複使用，不輸出 Model 值。
- `ScenePath` 可替代直接 PackedScene，`IViewSceneProvider` 可接自訂載入；預設使用 ResourceLoader threaded cache。

Unity 匯出／Godot 匯入、支援映射與 adapter 範例見 [UNITY-CONVERSION](UNITY-CONVERSION.md)。Unity Prefab、Animator、UnityEvent、3D RectTransform、shader 與任意 MonoBehaviour 不能直接執行。匯入到空目錄，檢查 conversion-report，再接業務行為；報告 Converted／Complete 不等於所有 Unity 功能或平台相容。

## 常見問題

| 現象 | 檢查與處理 |
|---|---|
| 看不到新的 Editor／Rect Transform | 核對 addon checkout 與版本、宿主 C# build、Plugin 啟用及錯誤輸出。一般 Rect Inspector 只處理編輯中場景的單一 Control。 |
| 改原始碼後畫面仍是舊版 | 先保護並保存正在編輯的文件，關閉日常 Editor，再 build 正確的 Godot 宿主後重開。只更新檔案或建置 Server 不會換掉已載入 DLL。 |
| 進入 Page 後右側空白 | 尚未選 placement；點第二層元件。要改本體則返回第一層。 |
| 建好的 Element 找不到 | 確認是 Catalog Element 還是 placement；前者用種類篩選，後者在所屬 Page／State 的第二層。 |
| 欄位不能改／版面被重排 | 檢查父 Container、原生最小尺寸、read-only 狀態，以及 placement 是否仍沿用子場景。 |
| Tree 點父分組後值沒變 | 分組只瀏覽；雙擊實際屬性或 binding，或用分支＋新增。 |
| Event 方法是停用的 | 檢查 Signal、接收節點、相容簽名與 C# AOT 註冊；尚未選完的 Event 仍是停用草稿。 |
| Model 缺失或文字顯示 `{...}` | 分清原生固定文字與 Raw，並核對 Add 的型別／key／scope；Set 的 Variant key 不等於 typed Add。 |
| 差異窗沒有預期內容 | 檢查是否拖 instance 根 Control、直接來源是否相同，以及差異是否屬於可匯入範圍；也可用場景選取入口。 |
| 預覽沒播放業務事件 | 預覽刻意不派送 Action／runtime lifecycle；啟動遊戲驗證實際行為。 |
| 自訂預覽 preset 別人看不到 | 目前是本機專案 Editor metadata，不在 Catalog 中。 |

不要把刪除整個 `.godot` 當成預設更新步驟；它包含本機 Editor metadata，自訂 preset 等可能遺失。不要覆寫使用者未保存內容來處理舊 DLL。

## Agent 與維護流程

### 修改前確認

1. 先讀本手冊與所在 repository 的 AGENTS.md，確認這份 checkout 的版本、`git status`、submodule gitlink／HEAD 與未提交差異；不要用穩定 tag 的行為描述覆蓋目前開發碼。
2. 分清使用者要改的是 source Scene、instance、placement、Catalog 全域設定、Model 或 runtime。參照上面的保存／Undo 表決定資料歸屬。
3. 在宿主與 addon 之間維持界線：套件放通用 runtime／Editor；產品 Scene、Catalog、服務路由、業務 Action／Model 放宿主。
4. 一般改名保留穩定 ID、NodeName 與來源身分；現有本體 Override／Event 雖不在日常表單顯示，資料與 runtime API 仍相容，不要自動刪除。
5. Editor 還開著時，不直接換掉它使用中的 DLL 或原生編輯 helper。先保護使用者的未保存工作；需建置時確認宿主 target／SDK／lockfile，不為了桌面測試覆寫其他平台的鎖定依賴。

新增 Resource 欄位時檢查 `ViewCatalog.RetainAuthoredGraph()` 的 wrapper 保留與重載交接；不要以全域永久快取代替文件生命週期。Editor UI 遵守既有 method-name callable／解除連線／釋放流程，參照 [native callable helper](Editor/native_callable.gd)，避免讓舊 C# UI delegate 留過 DLL unload。

### 操作與驗收

優先使用引擎／套件公開入口與 Resource API；手改 `.tres`／`.tscn` 時保留 script、資源型別、ID 及引用，並用 Godot 載入驗證。不要把 placement 當作新 Scene，或用來源資產的修改代替局部 Override。

對影響 Editor 的修改，驗證選取範圍、無效輸入、取消、Undo／Redo、來源不被改寫、保存冷開；生命週期相關修改還要測實際 DLL reload 與清理。日常專案用 QA 副本驗收，前後核對正式資產；單純程式呼叫、payload 測試與實際滑鼠／鍵盤操作分開記錄。若桌面工具沒送出有效 drag，不將它寫成拖曳已通過。

本套件可獨立執行 smoke，以下命令的工作目錄為 **submodule 根目錄**：

```sh
python3 Tools/verify.py --godot /path/to/Godot --dotnet /path/to/dotnet --output /path/to/new-evidence
```

需 Python3.9+、Godot4.7.2 .NET、SDK8；GUI 階段需桌面。離線可加 `--nuget-source /path/to/pinned/nupkgs`。每次使用新 output 目錄，檢查 exit code、JSON assertions、ERROR／SCRIPT ERROR／leak，不能只看某一行 PASS。此 smoke 涵蓋乾淨宿主建置、runtime10項與 GUI 範例，不是全部功能或平台資格驗證。

在 Macaca Bootstrap 宿主內，較完整的入口是 `Tests/ViewSystem/run.py`、`Tests/ViewSystem/authoring_reload.py`、`Tests/ViewSystem/control_rect.py`；這些檔案不隨獨立 addon 交付。宿主的 `Docs/ViewSystemParity/` 與 `Validation/ViewSystemParity/` 保存擴充驗證，獨立 consumer 不應硬依賴它們。Control Rect 的保存驗收使用有渲染的 Editor，headless dummy renderer 的縮圖不代表功能失敗或成功。

### 原始碼查找

下列連結均在 submodule 內，可供 agent 查核實際行為；變更 API 時同步修訂手冊。

| 範圍 | 來源 |
|---|---|
| 定義與序列化 | [ViewCatalog](ViewCatalog.cs)、[ViewEntry](ViewEntry.cs)、[placement](ViewElementPlacement.cs)、[layout](ViewElementLayout.cs) |
| 導覽與狀態 | [ViewHost](ViewHost.cs)、[UnityTiming](ViewHost.UnityTiming.cs)、[options](ViewNavigationOptions.cs)、[合約](NAVIGATION.md) |
| Model／Attribute | [context](ViewContext.cs)、[expression](ViewModelExpression.cs)、[attributes](ViewAttributeBindings.cs) |
| Override／Event 實際套用 | [composer／instance](ViewComposer.cs)、[rule](ViewPropertyOverride.cs)、[binding](ViewEventBinding.cs)、[method picker](Editor/ViewSystemEditor.EventMethods.cs) |
| 工作區與文件歷史 | [Editor](Editor/ViewSystemEditor.cs)、[selection](Editor/ViewSystemEditor.Selection.cs)、[drafts](Editor/ViewSystemEditor.Drafts.cs)、[interactions](Editor/ViewSystemEditor.Interactions.cs) |
| 導覽／差異 | [sidebar](Editor/ViewSystemEditor.Navigation.cs)、[references](Editor/ViewSystemEditor.References.cs)、[diff](Editor/ViewReferenceDiff.cs) |
| 一般 Control Rect | [Inspector](Editor/ControlRect/inspector.gd)、[UI](Editor/ControlRect/rect_property.gd)、[geometry](Editor/ControlRect/rect_geometry.gd) |
| Placement Rect／預覽 | [Rect Inspector](Editor/ViewSystemEditor.RectInspector.cs)、[preset](Editor/ViewSystemEditor.PreviewPresets.cs)、[parity](Editor/ViewSystemEditor.Parity.cs) |
| Pool／requested | [policy](ViewElementPolicy.cs)、[pool](ViewElementPool.cs)、[requested](ViewRequestedPool.cs)、[host entrypoints](ViewHost.Requested.cs) |
| Safe Area／display | [settings](ViewSafeAreaSettings.cs)、[metrics](ViewDisplayMetrics.cs) |
| 驗證與打包 | [verify](Tools/verify.py)、[package](Tools/package.py) |

### 更新與交付

安裝與 submodule 選版見 README。修改手冊不需要重建日常 Editor；功能修改依變更範圍驗證，更新 CHANGELOG／版本後再依交付要求提交與發佈，不能把 `0.7.0-dev` 文件當成已發佈 tag。

打包命令：

```sh
python3 Tools/package.py --output /path/to/artifacts
```

產物包含 `addons/macaca_viewsystem/` 與 SHA256 manifest；應確認本手冊有打包、manifest 相符、重複打包相同。套件自己的程式採 MIT，授權及移植來源見 [LICENSE](LICENSE)、[NOTICE](NOTICE.md)。不要提交生成的 build/cache，或把日常 QA 資料混入套件。
