# 开发笔记 — Civ2-clone (macOS)

> 编写口径：本笔记记录在 macOS (Apple Silicon) 上让本仓库跑起来、并修复若干问题的全部变更点，供后续开发查阅。每条均给出 `文件:位置` 与改动要点。
> 环境：macOS (darwin, arm64)、.NET 9 SDK（装在 `~/.dotnet`）、Raylib-CSharp 5.0.0、原版资产为 MGE 黄金版。

---

## 0. 环境与构建

- **.NET SDK**：系统未全局安装 `dotnet`。用官方脚本装到了 `~/.dotnet`（版本 9.0.315），调用时需 `export PATH="$HOME/.dotnet:$PATH"` 或直接用绝对路径 `~/.dotnet/dotnet`。
  - 安装命令：`curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 9.0 --install-dir "$HOME/.dotnet"`
  - 注意：Homebrew 的 `dotnet-sdk` cask 不可用（装的是 .NET 10，且 `.pkg` 安装要 sudo 密码）。
- **构建**：以 `RaylibUI` 为启动项目。

  ```bash
  cd "<repo>" && ~/.dotnet/dotnet build RaylibUI/RaylibUI.csproj -c Debug
  ```

  正常输出约 780 个可空引用警告、0 错误。
- **运行**：

  ```bash
  cd "<repo>/RaylibUI/bin/Debug/net9.0" && ~/.dotnet/dotnet RaylibUI.dll
  ```

  arm64 原生库在 `runtimes/osx-arm64/native/libraylib.dylib`，自动加载。
- **资产路径配置**：改 `Engine/appsettings.json` 的 `SearchPaths` 指向原版资产目录（校验只看目录里有没有 `rules.txt`，大小写不敏感）。该文件 `CopyToOutputDirectory=Always`，会随构建进 bin。用户级配置在 `~/Library/Application Support/AxxCiv/appsettings.json`（优先级更高，游戏内“添加路径”会写这里）。

---

## 1. 战争迷雾 / 视野不更新

- **现象**：单位移动后，所到之处地图视野仍是黑的，视野不展开。
- **根因**：`Engine/src/MapObjects/MapNavigationFunctions.cs` 的 `IsCurrentlyVisible` 有复制粘贴 bug —— 检查“相邻格 `l` 上有没有本方单位”时误用了中心格 `tile.UnitsHere`，应为被遍历的邻格 `l.UnitsHere`。
- **后果链**：移动后 `Game.UpdateTiles` 用 `IsCurrentlyVisible` 过滤要重绘的格子，因为这个 bug，新揭示的邻格全被判为“不可见” → 不重绘、`PlayerKnowledge` 不更新 → 保持黑色。普通单位（无 `TwoSpaceVisibility`）必中。
- **修复**：`tile.UnitsHere` → `l.UnitsHere`（一处）。
- **回归测试**：`Core.Tests/Units/MovementFunctionsTests.cs` 新增 `IsCurrentlyVisible_FriendlyUnitOnAdjacentTile_ReturnsTrue`，已验证“还原 bug 必失败、修复后通过”。

---

## 2. 测试项目编译修复

- **现象**：`Core.Tests` 编译不过，`MockInterface` 未实现接口新增成员 `IUserInterface.GetOptionsLooks(OptionsType?)`。属预先存在问题，与上面无关。
- **修复**：`Core.Tests/Mocks/MockInterface.cs` 补 `GetOptionsLooks` 的 `NotImplementedException` stub。注意该类内含嵌套类 `MockAction` / `MockMainApp`，stub 要加在 `MockInterface` 本体（4 空格缩进段）而非嵌套类里。
- 修复后全套 117 个测试通过。

---

## 3. 声音系统

这一块是本次改动最多的部分。原状态：只有主菜单循环音乐接了线，游戏内事件音效完全没实现，且 macOS 上一切声音都不出。

### 3.1 macOS 音频转换崩溃（根因层）

- **现象**：macOS 上完全没声音，转换输出目录 `CONVERTEDSOUNDS-*` 是空的。
- **根因**：`RaylibUI/Sounds/SoundData.cs` 的 `ConvertAudioPcmU8ToPcmS16Le` 用 NAudio 的 `WaveFormatConversionStream`，它底层是 Windows 专有的 ACM（`Msacm32.dll`），macOS 上抛 `DllNotFoundException`，被 catch 后返回 `false` → `IsConverted=false` → 永不播放。代码里原本就留着 `//TODO: find alternative sound methods for OSX`。
- **关键事实**：原版音效是 8-bit PCM WAV，而 raylib / miniaudio (dr_wav) 本就能直接加载 8-bit WAV，那步转换在 macOS 上既失败又多余。
- **修复**（`SoundData.cs`）：
  - `Convert()`：转换失败时回退为 `IsConverted = File.Exists(PathFull)`。
  - `Play()` / `LoopSound()`：加载时优先用转换文件，没有就直接加载原始 WAV（`var path = File.Exists(PathConv) ? PathConv : PathFull;`）。
  - Windows 行为不变（转换成功仍走转换文件）。

### 3.2 游戏内事件音效（接线）

引擎本就有事件回调，但 UI 侧从没调用音效。已接入的映射：

| 事件 | 音效 | 位置 |
|---|---|---|
| 单位移动（仅玩家自己的） | `MOVPIECE` | `LocalPlayer.UnitMoved` |
| 战斗 | 单位 `AttackSound`（`CombatEventArgs.Sound`，引擎从 `RULES.TXT @SOUNDS` 解析），无则按兵种兜底：陆 `SWORDFGT` / 海 `NAVBTTLE` / 空 `AIRCOMBT` | `LocalPlayer.CombatHappened` |
| 建城 | `BLDCITY` | `BuildCity.Build` |
| 回合结束 | `ENDOTURN` | `EndTurn.Action` |
| 城市动乱 | `CIVDISOR` | `LocalPlayer.CivilDisorder` |
| 我们爱国王日 | `CRWDBUGL` | `LocalPlayer.WeLoveTheKingStarted` |
| 建筑被变卖 | `SELL` | `LocalPlayer.CantMaintain` |
| 我方单位阵亡 | `MEDEXPL` | `LocalPlayer.UnitsLost` |

- 注意：MGE 的 `RULES.TXT` 没有 `@SOUNDS` 段（攻击音写在 EXE 里），所以大多数单位 `AttackSound` 为空，靠上面的兵种兜底。
- 尚未接（避免猜错音效，需要映射表）：城市建完建筑 / 奇观（每种建筑一个音，如 `AQUEDUCT/BARRACKS/CATHEDRL`，WAV 名是缩写）、研究出科技、政府更替、夺取 / 失去城市。

### 3.3 菜单音乐：流式播放在 macOS 不出声

- **现象**：引擎诊断显示 `Music` 流（`PlayMusicStream`）“正在播放并循环”（`GetTimePlayed` 持续推进），但用户听不到；而一次性 `Sound` 能听到。
- **结论**：在该 macOS 环境，流式 `Music` 这条输出通道不出声，一次性 `Sound` 出声。
- **修复**：菜单音乐改用一次性 `Sound` 播放 + 每帧检测 `!IsPlaying` 就重播来实现循环，绕开流式播放。`SoundData` 新增 `IsPlaying` 属性。

### 3.4 音乐管理器重构（循环 + 序列）

- 把音乐播放统一到 `RaylibUI/Sounds/Sound.cs` 的集中式管理器，由 `Main.RunLoop` 每帧调 `Soundman.UpdateLoop()` 驱动，**不再依赖某个屏幕的 Draw**，因此音乐能跨屏幕 / 对话框持续。
- 两种模式：
  - `PlayMusicLoop(name)`：单曲循环（主菜单 `MENULOOP`）。
  - `PlayMusicSequence(names...)`：顺序各播一次然后静音。
- 实际接线：
  - 主菜单：`MainMenu.InterfaceChanged` 调 `PlayMusicLoop("MENULOOP")`（原来的 `_sndMenuLoop` 字段与 `MusicUpdateCall` 调用已移除）。
  - 游戏开始：`Main.StartGame` 调 `PlayMusicSequence("MENUEND", "MENUOK")`（自动停掉菜单音乐，依次播放这两首）。
- `Sound.UpdateLoop`：当前曲播完后，循环模式重播、序列模式取队列下一首、队列空则停。

### 3.5 已知但未做

- 游戏内 Sound / Music 开关选项是摆设：`Options.SoundEffects` / `Options.Music` 存在但代码从不检查（grep 无引用），音效不受开关控制。要让设置界面生效需另接。
- `SoundData` 里旧的流式 `Music` 路径与 `MusicUpdateCall` / `_loopAsSound` 已不再使用（属无害死代码），保留未删。

---

## 4. 地图随机生成（每局都一样）

- **现象**：每次新建游戏地图完全相同。
- **根因**：`Model/Core/GameInitializationConfig.cs` 把地图生成用的随机源写死成固定种子：`public FastRandom Random { get; set; } = new (4564234);`。
- **数据流**：点新游戏 → `Civ2/Dialogs/MainMenu.cs:28` 调 `ClearInitializationConfig()` 清空缓存 → 下次访问 `Initialization.ConfigObject` 新建 config，`Random` 又是固定种子 `4564234` → `MapGenerator` 全程用 `config.Random`（地形、`ResourceSeed`、出生点）→ 每局地图相同。
- **修复**：改成 `new ()`，即 `FastRandom()` → `new Random().Next()`（系统时钟随机种子），每个新 config 都不同。
- 备注：固定种子 `4564234` 多半是开发时为可复现调试留的。若以后要做“指定种子重开同一张图”（原版有 map seed），可在新游戏流程里加一个输入种子的入口。

---

## 5. 已知坑 / 待办

- **iCloud 幽灵副本**：仓库在 iCloud 云盘（`~/Library/Mobile Documents/com~apple~CloudDocs/`）里，每次构建 iCloud 同步冲突会在 `bin/` 生成带 “ 2” 后缀的副本（`Civ2Gold 2.dll` 等）。游戏 `LoadInterfaces` 会加载 bin 里每一个 `*.dll`，导致同一 interface 被实例化两次 → 例如“Select game version”列表出现两个相同的“Civilization II Multiplayer Gold”。
  - 临时清理：`find RaylibUI/bin -name "* 2.*" -delete`。
  - 根治方向（未做）：用 `Directory.Build.props` 把 `bin`/`obj` 输出重定向到 iCloud 之外。
- **`buttons not found!`**：启动日志里的一条提示，某按钮资源没定位到，非致命。
- **从后台 / detached 进程启动 GUI 偶发段错误**：`GLFW: Failed to determine Monitor to center Window`，发生在窗口创建阶段，与游戏逻辑无关；从用户自己的终端正常启动则没问题。
- `CivServer/` 目录为空，多人 / 服务端尚未开始。

---

## 6. 本次改动文件清单

- `Engine/src/MapObjects/MapNavigationFunctions.cs` —— 视野 bug 修复
- `Core.Tests/Units/MovementFunctionsTests.cs` —— 视野回归测试
- `Core.Tests/Mocks/MockInterface.cs` —— 接口 stub
- `RaylibUI/Sounds/SoundData.cs` —— macOS 音频回退、一次性循环、`IsPlaying`
- `RaylibUI/Sounds/Sound.cs` —— 音乐管理器（循环 / 序列 / `UpdateLoop`）
- `RaylibUI/Main.cs` —— 主循环每帧 `Soundman.UpdateLoop()`
- `RaylibUI/Main.LoadGame.cs` —— 游戏开始播 `MENUEND → MENUOK`
- `RaylibUI/Initialization/MainMenu.cs` —— 改用 `PlayMusicLoop`
- `RaylibUI/RunGame/LocalPlayer.cs` —— 各事件音效
- `RaylibUI/RunGame/Commands/EndTurn.cs` —— 回合结束音效
- `RaylibUI/RunGame/Commands/Orders/BuildCity.cs` —— 建城音效
- `Model/Core/GameInitializationConfig.cs` —— 地图随机种子
- `Engine/appsettings.json` —— 资产路径

---

## 7. 奇观世界唯一（第二轮）

- **现象**：同一个奇观可以被反复建造;别人先建成后，自己仍能建;建出来的奇观不生效（其实是重复幻影）。
- **根因**:`BuildingProductionOrder.IsValidBuild` 只检查“这一座城”有没有该改良，对“世界唯一”的奇观是错的;`CompleteProduction` 只处理了 `Effects.Unique`（同文明唯一，如皇宫），没处理 `IsWonder`（全世界唯一）。
- **修复**(`Engine/src/Production/`):
  - `ProductionCabalilities.cs`：新增全局已建奇观集合 `_builtWonders` + `RegisterWonderBuilt`（建成时从所有文明的可造列表移除该奇观）+ `WonderAlreadyBuilt`;`InitializeProductionLists` 从现有城市扫描已建奇观（读档也识别）。
  - `BuildingProductionOrder.cs`:`IsValidBuild` 对已建奇观返回 false;`CompleteProduction` 建成奇观时调 `RegisterWonderBuilt`。
- 连带满足“别人先建成 → 其它城中止建造”：该奇观从列表移除后，正在造它的城 `ProductionValid` 失效 → 回合处理时 `AutoNext` 切换生产。

## 8. 拓荒者建城后仍被供养（第二轮）

- **现象**：拓荒者建城后应消失，但母城仍在供养它、消耗资源。
- **根因**:`Model/Core/Cities/City.cs` 的 `SupportedUnits` 没过滤死亡单位 —— 建城时拓荒者 `Dead=true`，但仍留在 `Owner.Units`，仍按 `HomeCity` 算进 `city.Support`。
- **修复**:`SupportedUnits` 加 `&& !unit.Dead`。

## 9. 弹窗堆叠关不掉（第二轮）

- **现象**：单回合多座城市完成产出时，弹出多个“BUILT”窗叠在一起，点击关不掉、卡死。
- **根因**:`GameScreen` 的 `ShowPopup` 用一对共享字段 `_currentPopupDialog` / `_popupClicked`，但弹窗是 `stack:true` 堆叠;`ClosePopup` 永远关“最后那个”，先弹的关不掉。
- **修复**：去掉共享字段，每个弹窗用闭包捕获自己，关闭时各关各的、各调各的回调。
- 注意：用户反馈“单城单弹窗也卡死”是另一回事（尚未定位，需运行日志）。

## 10. 全文明奇观效果传播 + 奇观效果数据（进行中）

- **关键事实**：奇观效果原本**完全没定义** —— 改良效果在 `Engine/Scripts/improvements.lua` 里逐个 `Effects.Add`，但脚本只写到基础建筑（1–34）就停了，所有奇观的 `Effects` 字典是空的。连 Cathedral / Colosseum 等基础建筑的效果也没定义。
- **传播框架**（已建）:
  - `Model/Core/Cities/Improvement.cs`:`CivWide` 标志;`Engine/src/Scripting/ScriptObjects/CityImprovement.cs`:Lua 入口 `improvement.CivWide = true`。
  - `Engine/src/Cities/CityExtensions.cs`:`City.EffectImprovements()` = 本城改良 + 全文明其它城的 CivWide 奇观;已接入 `GetMultiplier`（科研 / 税收 / 奢侈）、`ContentFace`（幸福度）、`GetFoodStorage`（存粮）。
  - 低风险：无奇观标 CivWide 时行为完全不变。
- **已填的奇观数据**（`improvements.lua`，效果按原版、且能映射到现有 `Effects` 枚举的）:
  - 金字塔（索引 39）：全文明等同粮仓 → `FoodStorage 50` + CivWide。
  - 悬空花园（索引 40）：全文明每城 +1 内容 → `ContentFace 1` + CivWide。
- **未填 / 待办**：大多数奇观要么数值需可靠来源核实（维基 403，游戏自带 `Describe.txt` 只有风味文字、无机制数值），要么需要新机制（列奥纳多工坊升级单位、达尔文送科技、马可波罗建大使馆、神谕翻倍庙宇、巴赫按大陆范围等），且需先补齐基础建筑（Cathedral / Colosseum）的效果。索引换算：改良索引 = `RULES.TXT @IMPROVE` 段序号 − 1。

## 11. iCloud 重复 DLL 免疫 + custom map 不出现（第三轮）

- **现象**：点“Customize World”后没有进入选择地貌 / 海洋比例的界面。
- **排查**：自定义世界对话框（`Civ2/Dialogs/NewGame/CustomWorldDialogs/`）与流程（`WorldSizeHandler` → `CustomisePercentageLand` → …）都正常，弹窗文本键（`@CUSTOMLAND` 等）也在 `Game.txt` 里 —— **代码流程本身没问题**。
- **真因**:iCloud 重复 DLL（这次是 `Civ2TOT 3.dll`,“ N” 后缀不止 “ 2”）。`LoadInterfaces` 加载 bin 里每个 `*.dll`，重复副本让同一 interface 被实例化两次 → `AllRuleSets > 1` → 点“Customize World”后冒出“Select game version”弹窗;若点 Quick Start 会走 `InitNewGame(true)` 生成随机地图、**跳过所有自定义**。
- **根治修复**(`RaylibUI/Initialization/Helpers.cs`):`LoadInterfaces` 按程序集标识 `AssemblyName.FullName` 去重（iCloud 副本与原件标识相同），同一程序集只加载一次 —— 从根上免疫 iCloud 副本，不再依赖手动清理。
- 清理残留副本（任意 “ N” 后缀）:`find RaylibUI/bin -regex '.* [0-9]+\.[^/]*' -delete`。

## 12. 地图永远是离散群岛 / Landform 不起作用（第三轮）

- **现象**：每局新地图都是很离散的小岛群，自定义世界里选 “Continents” 也没用。
- **根因**:`Engine/src/MapGeneration/MapGenerator.cs` 的随机地图生成（私有 `GenerateMap`）把岛屿尺寸写死成 `minIslandSize=3 / maxIslandSize=30`，**完全没用 `config.Landform`** —— 所以无论选什么都是 3–30 格的小岛。（同文件里注释掉的参考实现本来 `maxIslandSize=300`。）
- **Landform 取值**（`@CUSTOMFORM` 选项，`config.Landform = SelectedIndex`）:0=Archipelago，1=Varied（默认），2=Continents。
- **修复**：岛屿尺寸改为按 `config.Landform` 取档 —— 0 → 3..30（群岛）、2 → 20..300（大陆）、其余 → 8..120（混合）。`PropLand`（陆地比例）本来就有用，这次补上 Landform（陆地聚团程度）。快速开局默认 Landform=1，也比原来全是小岛更像样。
- 备注：`Climate` / `Temperature` / `Age` 等其它自定义项目前同样未接入地图生成，待办。

## 13. 地图只有草地、没有山 / 河 / 沙漠等（第三轮）

- **现象**：地图上全是草地，没有山、丘陵、河流、沙漠、平原等地形。
- **根因**：`MapGenerator` 的私有 `GenerateMap` 生成陆地时把每个land tile 一律设成 `grassland`，从没做地形类型分配（注释里的参考 `selectTerrain` 没启用）。河流（`Tile.River`）也从没设过。
- **修复**：新增 `AssignTerrain` + `PickTerrain` —— 生成完陆地后，按纬度加权随机分配地形：
  - 计算每个 tile 的纬度系数 `latFrac`（赤道 0、两极 1）；
  - 按纬度档位用加权随机表选地形：高纬 → 苔原 / 冰原 / 丘陵 / 山；温带 → 草地 / 平原 / 森林 / 丘陵 / 山；低纬 → 丛林 / 沼泽 / 沙漠 / 平原 等；
  - **苔原只在高纬两档、冰原只在最高纬一档**出现，中低纬完全没有（满足“苔原 / 冰原只在南北两极”）；
  - 非冰原陆地约 7% 概率带河流。
- 这是简化版的纬度气候模型，非完全复刻原版算法；`Climate` / `Temperature` 档位尚未纳入加权（待办）。
