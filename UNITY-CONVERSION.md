# Unity → Godot 資產轉換

以 Unity Editor 讀取資產，再產生 Godot 原生資源。來源資料、GUID 和有正負號的完整 64-bit fileID 都保留；輸出目錄必須為空，不覆寫既有 Godot 成品。不是 Unity 執行環境或任意 C# 原始碼翻譯器。

## 匯出

將 `Tools/Unity~/ViewSystemUnityExporter.cs.txt` 複製至來源專案 `Assets/Editor/ViewSystemUnityExporter.cs`。選取資產／資料夾後執行 `Tools > ViewSystem > Export Unity assets`，選擇空資料夾。依賴與內嵌物件會一起匯出。

批次執行：

```sh
unity run /path/to/UnityProject --editor-version 6000.3.10f1 --timeout 180 -- -nographics -executeMethod MacacaGames.ViewSystemMigration.ViewSystemUnityExporter.Export -viewExportRoot Assets/UI -viewExportOutput /tmp/UnityBundle -viewSampleRate 60
```

不依賴 Newtonsoft 或專案中的 ViewSystem namespace。一般 prefab／scene、controller、clip、SerializedProperty 由 Unity 公開 Editor API 讀取。匯出器保留原始曲線 keys／tangents／weights，並由 Unity 的 `AnimationCurve.Evaluate` 按指定取樣率烘焙；Godot 播放取樣間為線性內插，並非連續曲線逐點完全相同。取樣率可設1–240 Hz。遺失腳本／載入失敗會列入報告。

## 匯入

Godot ViewSystem 的「⋯ > 匯入 Unity」選擇 bundle 的 `manifest.json`。輸出至新的 `res://UnityImports/Import_…`，查看 `conversion-report.json`。

或在已建置的 Godot .NET **編輯環境**中：

```sh
godot --headless --path /path/to/GodotProject res://addons/macaca_viewsystem/Tools/UnityImport.tscn -- --unity-manifest /tmp/UnityBundle/manifest.json --unity-output res://UnityImports/UI
```

CLI exit code：0＝沒有待處理項目；2＝已產生可檢視成品但有 adapter 項目；1＝匯入失敗。應先檢查報告再使用成品；這個工具不在 exported runtime 執行。原始 bundle、資產快照與 hash 會保留在輸出下的 `_source/` 及 `*.source.tres`。來源 C# 以文字保存，避免混入 Godot 編譯。

## 已提供的對應

| 來源 | Godot 成品 |
|---|---|
| Prefab／Scene | PackedScene；保留階層、名稱、active、2D anchors／offsets／pivot／scale／rotation 或 Node3D transform |
| Text／TextMeshProUGUI 基本內容 | Label、文字、字級、顏色；一般 Font 可轉 FontFile |
| Image／RawImage | TextureRect；Sprite 子矩形轉 AtlasTexture；圖檔轉 ImageTexture |
| Button／CanvasGroup | Button enabled、alpha、基本 raycast；UnityEvent 需明確換成 Action／signal |
| PNG／JPG／WebP／BMP／TGA、WAV／OGG／MP3、TTF／OTF | 原生 Texture／AudioStream／Font Resource；各引擎可解碼格式仍依平台 |
| ScriptableObject 資料 | UnitySerializedResource.Fields；保留 public 和 private SerializeField 的型別、路徑與資產參照；複合欄位以 Unity serialized property paths 表達 |
| AnimationClip | Animation；alpha、顏色、Sprite reference、scale、Control Z 旋轉、RectTransform anchored position、Node3D position／Euler binding |
| 基本 Animator | AnimationPlayer＋UnityAnimatorAdapter；float／int／bool／trigger、條件、Any State、default state、exit time、duration／offset、speed parameter；另產生可編輯的 AnimationTree state graph |

`AnimationTree` 預設不啟用，由 adapter 驅動 `AnimationPlayer`。要以原生 graph 接手時，停用 adapter 再啟用 tree，補上原生條件／parameters。不要同時讓兩者寫同一動畫。

報告的 `Converted` 只計原生轉換成品；`Assets` 同時涵蓋轉換資產與保留的來源快照。`Complete` 代表本次匯入未發現待處理項目，不代表任意 Unity 腳本及所有平台已相容。

## 擴充與需要人工對應的情況

以新的 `UnityAssetConverter` instance 註冊 mapping 後呼叫 `Import(manifest, freshOutput)`：

```csharp
var converter = new UnityAssetConverter();
converter.Components["MyGame.CustomPanel"] = (source, node) => {
    // Read source.properties / source.json and configure a native Godot node.
};
converter.Assets["MyGame.UiTheme"] = (source, resources) => new Theme();
converter.Bindings["MyGame.CustomPanel:progress"] = (curve, node) =>
    ("value", 1d, 0d); // native property path, scale, offset
var result = converter.Import(manifestPath, "res://Imports/UiV2");
```

Assets adapter 可把材質、專用 ScriptableObject 或其他來源型別改成應用程式自己的 Resource。Component adapter 接收所屬 Node，可新增原生子節點；Bindings adapter 顯式處理自訂動畫 property。JSON 中保留完整來源，不會把未辨識欄位悄悄當作已轉換。

以下仍會逐項報告，不會自動猜測：BlendTree／多 layers／AvatarMask／additive blending、nested machine 的 entry／exit routing、transition interruption、mirroring／motion time、StateMachineBehaviour、AnimationEvent、AnimatorOverrideController、Humanoid／root motion／骨架曲線、RectTransform sizeDelta／3D UI／Camera Canvas、Unity layout groups／mask／shader／材質、自訂 MonoBehaviour 與 UnityEvent 方法副作用。這些大多可在 Godot 重建，並非都屬於引擎無法實現。

| 情境 | 可選方案 |
|---|---|
| BlendTree／layer／骨架 | 以 AnimationTree 原生重建；或加入該類型的專案 converter adapter |
| AnimationEvent／StateMachineBehaviour／UnityEvent | 換成 signal／ViewSystem Action／Godot C# Node；或自行提供方法對照 |
| Unity 材質／特殊 Image／Mask | 使用原生 Theme、NinePatchRect、shader adapter；或預先烘焙圖像 |
| World Space／Camera Canvas | 使用 SubViewport＋Node3D 材質；或專用 3D UI adapter |

這些選項延續已選定的「Godot 原生流程優先」。保留可檢視的 source 與報告，讓各項專案對應能獨立進行。
