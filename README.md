# ViewSystem for Godot

Godot .NET 的 Page／State／Overlay 組合與視覺化編輯工具。提供可重用元件、Override／Event、Model、動畫、pool，以及一般 Control 的 Unity 風格 Rect Transform Inspector。

**目前 Godot 版的主要操作文件：[USAGE.md](USAGE.md)**。同一份手冊供人類使用者與 agent 使用，包含第一次建立頁面、Editor 操作、runtime 範例、資料規則、常見問題、原始碼入口與驗證流程。

| 文件 | 內容 |
|---|---|
| [USAGE.md](USAGE.md) | 日常使用及 agent 維護的主要入口 |
| [NAVIGATION.md](NAVIGATION.md) | 導航、取消、commit 與清理的詳細合約 |
| [UNITY-CONVERSION.md](UNITY-CONVERSION.md) | Unity 資產匯出／轉換與人工 adapter 範圍 |
| [CHANGELOG.md](CHANGELOG.md) | 版本變更 |
| [NOTICE.md](NOTICE.md)、[LICENSE](LICENSE) | 移植來源與授權 |

此 checkout 為 **0.7.0-dev（未發佈）**。下列安裝指令中的 `v0.6.0` 是既有穩定版，不含目前工作目錄的全部 Editor 功能；不能用 tag 安裝指令推論已取得開發版功能。

桌面驗證環境為 **Godot4.7.2 .NET、SDK8.0.427**。套件不依賴 Unity、GameSystem、ServerShared、FreeSql、MagicOnion 或產品 Autoload；其他 Godot 版本與平台仍需各自驗證。

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

## 啟用後開始使用

建置 Godot 宿主、啟用 **Macaca ViewSystem** 後，頂部選 **ViewSystem → 套件範例**，或依 [快速開始](USAGE.md#快速開始) 建自己的 Catalog。產品 Scene、Catalog、Model 與業務 Action 留在宿主專案。

一般 Control 不需要 Catalog：在原生 2D Editor 選單一 Control，即可使用右側 Rect Transform。[一般 Control 與 placement 的保存／Undo 差異](USAGE.md#兩種-rect-transform) 請先確認。

## 驗證與封裝

以下命令在 submodule 根目錄執行。需要 Python3.9+；驗證另需 Godot .NET、SDK8 與 GUI 桌面，使用全新的 evidence 目錄。

```sh
python3 Tools/verify.py --godot /path/to/Godot --dotnet /path/to/dotnet --output /path/to/new-evidence
python3 Tools/package.py --output /path/to/artifacts
```

離線驗證可加 `--nuget-source /path/to/pinned/nupkgs`。smoke 範圍與完整驗收、原始碼查找及更新規則見 [Agent 與維護流程](USAGE.md#agent-與維護流程)。ZIP 包含本手冊與 SHA256 manifest，安裝及使用不依賴 Bootstrap 的文件目錄。
