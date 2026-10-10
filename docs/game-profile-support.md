# 游戏配置支持范围

实现日期：2026-10-10。引擎位于 `desktop/Radiant.Games`，独立于授权和 WPF。它修改本地、已经存在的配置中的少量字段，提供预览、精确备份、应用回读和恢复。**本机未安装这三款游戏，本轮没有启动目标游戏或修改真实游戏配置。** 下表描述本版解析器接受的文件结构，不代表已验证所有当前游戏版本会采用这些值，也不代表厂商认可第三方编辑器。

## 0.4 免费 Performance 预设

三个游戏均增加无需激活的 Performance 预设。CS2 关闭 VSync 和 MSAA；Fortnite 关闭 VSync，把已有阴影、效果、后处理和植被品质设为 0，纹理设为 1；VALORANT 把已有材质和细节品质设为 0。降低画质可减少渲染工作量，实际帧率仍取决于硬件和游戏场景。

默认 Performance 保留当前帧率上限；VALORANT 也保留当前“始终限制帧率”开关。Fortnite / VALORANT 另外提供明确选择的 60、90、120、144、165、240、360 FPS 上限；选择 VALORANT 帧率预设时会启用该限制。这些数字是上限，不是预测帧率。分辨率、输入配置和不相关字段保持原样。

以下 Competitive / Balanced 是原有 Premium 预设，目标值保持不变，以兼容之前生成的恢复记录。所有预设使用同一严格格式识别、逐项预览和精确字节恢复机制。

## 支持清单

| 游戏             | 接受的文件与格式标识                                                                                                                                                                           | competitive / 竞技                                     | balanced / 均衡                                        |
| ---------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------ | ------------------------------------------------------ |
| Counter-Strike 2 | `Steam/userdata/<数字账号>/730/local/cfg/cs2_video.txt`；单个 `video.cfg` KeyValues 根；`Version` 必须为 `15` 或 `16`                                                                          | VSync 关闭，MSAA 2x                                    | VSync 开启，MSAA 4x                                    |
| Fortnite         | `%LOCALAPPDATA%/FortniteGame/Saved/Config/WindowsClient/GameUserSettings.ini`；`[/Script/FortniteGame.FortGameUserSettings]`；`FortniteReleaseVersion=14`；若该节存在 UE `Version`，必须为 `5` | 帧率上限 144，VSync 关闭，已有阴影/纹理/效果品质设为 1 | 帧率上限 120，VSync 开启，已有阴影/纹理/效果品质设为 2 |
| VALORANT         | `%LOCALAPPDATA%/VALORANT/Saved/Config/<账号>/Windows/RiotUserSettings.ini`，或同账号 `WindowsClient` 下该文件；`[Settings]`；`LocalSettingsVersion=5` 或 `7`                                   | 启用始终限制帧率 144，已有材质/细节品质设为 0          | 启用始终限制帧率 120，已有材质/细节品质设为 1          |

CS2 只修改 `setting.mat_vsync` 和 `setting.msaa_samples`。它还要求存在合理的 GPU 厂商/设备 ID 和分辨率字段以识别视频配置，但不改这些字段；不改 `fps_max`、阴影、启动选项或输入配置。

Fortnite 只修改 `bUseVSync`、明确选择帧率预设时的 `FrameRateLimit`，以及 `[ScalabilityGroups]` 中已经存在的 `sg.ShadowQuality`、`sg.TextureQuality`、`sg.EffectsQuality`。Performance 还会处理已有的 `sg.PostProcessQuality` 和 `sg.FoliageQuality`。不会插入缺失的画质键；预览会说明哪些键缺失。

VALORANT 只修改 `EAresBoolSettingName::LimitFramerateAlways`、`EAresFloatSettingName::MaxFramerateAlways`，以及已经存在的 `EAresIntSettingName::MaterialQuality`、`EAresIntSettingName::DetailQuality`。不修改 `GameUserSettings.ini`、VSync、灵敏度、准星、输入绑定、账号/漫游字段。选择 Riot 文件中的帧率键，避免把通用 UE 的帧率字段误标成 VALORANT 的实际“始终限制帧率”选项。

原有 Balanced / Competitive 的 120/144 是明确可预览的固定预设；新 Performance 默认保留当前上限，也可选择上述七种上限。程序不读取显示器刷新率来猜测最佳帧率。VSync 还可能将实际帧率限制到刷新率。游戏渲染模式、驱动、云同步、游戏更新及服务端漫游都可能影响最终生效值；应用后需要在游戏视频菜单核对。没有性能提升保证。

## 来源与证据边界

查阅日期为 2026-10-10。厂商文档用于确认配置类别、路径及设置含义；原始公开配置文件用于确认本适配器接受的节、键和显式版本标识。原始配置来自其他作者自行发布的数据，不是官方长期兼容协议，也没有被整份复制进程序。测试只创建包含必要字段、虚构账号标识和任意分辨率的最小 fixture。

| 来源                                                                                                                                                                  | 用于什么                                                             | 不能据此证明什么                                                                   |
| --------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------- | ---------------------------------------------------------------------------------- |
| [Epic：Fortnite 设置不保存](https://www.epicgames.com/help/c-34254770/c-0/a22311370?lang=en-US)                                                                       | WindowsClient 路径、文件名及只读属性会影响保存                       | 没有列出 Fortnite 配置格式版本                                                     |
| [Epic：Fortnite FPS 上限](https://www.epicgames.com/help/c-34254770/c-38015632/a17266354)                                                                             | VSync、帧率上限及游戏菜单核对方法                                    | 没有保证编辑文件会提升性能                                                         |
| [Epic：UGameUserSettings API](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Runtime/Engine/GameFramework/UGameUserSettings?application_version=5.5) | `bUseVSync`、`FrameRateLimit` 和画质设置含义                         | 通用引擎 API 不等于每款游戏的全部字段都会生效                                      |
| [Epic：INI 配置语法](https://dev.epicgames.com/documentation/en-us/unreal-engine/configuration-files-in-unreal-engine)                                                | 节、键值、注释、数组及 `+/-/!/.` 操作符                              | 本版不宣称支持引擎的全部配置语法                                                   |
| [Riot：Windows 日志和配置路径](https://support.riotgames.com/en-us/riot/performance/network-system-and-logs-windows)                                                  | VALORANT 的 LocalAppData 配置根目录                                  | 没有公开 RiotUserSettings 的稳定版本协议                                           |
| [Riot：设置读取错误](https://support.riotgames.com/en-us/valorant/support-tools/how-to-fix-error-retrieving-settings-from-server)                                     | 修改/排障前关闭游戏和 Riot 客户端的要求                              | 不代表官方认可本工具                                                               |
| [Valve：KeyValues 头文件](https://github.com/ValveSoftware/source-sdk-2013/blob/master/src/public/tier1/KeyValues.h)                                                  | Valve KeyValues 层次与 token 格式                                    | Source SDK 2013 不是 CS2 视频配置的版本协议                                        |
| [gucu112 原始 CS2 配置](https://github.com/gucu112/cs2-config/blob/master/cs2_video.txt)                                                                              | `video.cfg`、Version 15、视频字段及数值示例                          | 非本项目目标机器验证                                                               |
| [tzdanows 的 actual/cs2_video.txt](https://github.com/tzdanows/cs2-cfg/blob/main/actual/cs2_video.txt)                                                                | Version 16、相同视频字段及 MSAA 值                                   | 不保证未来 Version 17 等结构仍兼容                                                 |
| [公开 Fortnite 配置模板](https://github.com/tinodin/AutoOS-Resources/blob/main/Files/Fortnite/GameUserSettings.ini)                                                   | Fortnite 节和 `FortniteReleaseVersion=14` 标识                       | 这是配置模板，部分值空缺，不能作为可直接应用的完整配置                             |
| [公开 Fortnite 配置](https://pastebin.com/m2c4NeXi)                                                                                                                   | 较早版本 9 的输出形式额外对照，本版不接受版本 9                      | 其中有手工编辑内容；不从中复制个人字段，也不宣称经过厂商认证                       |
| [tioxy 自行发布的 VALORANT 配置](https://gist.github.com/tioxy/95b7bb70986431dbc4d844bdfc9a90b2)                                                                      | RiotUserSettings `[Settings]`、LocalSettingsVersion 5、帧率/画质字段 | 旧配置样本不能证明当前所有安装兼容                                                 |
| [公开 VALORANT 配置对照](https://pastebin.com/Ckw13EUe)                                                                                                               | LocalSettingsVersion 7 和同名帧率字段                                | 带作者注释的两个账号对照，不是可整份应用的配置；匿名数据不等于 Riot 的长期兼容承诺 |

未知版本、缺失版本、缺失必需键、不支持的数值、重复目标键、重复节、目标键含 INI 数组操作符、嵌套/带指令的 CS2 视频文件等都会阻止预览或应用，不会自动降级成通用写文件功能。

## 发现、预览和安全写入

自动发现只读取 HKCU 的 Steam 安装路径与标准 Steam 路径，以及当前用户 LocalAppData 中的上述位置。最多八个 Steam 根、每个已知根下 64 个账号目录；不做递归全盘扫描。没有配置表示需要先由游戏生成配置，程序不会伪造账号或创建整份新配置。

手动选择允许非标准安装盘，但仍要求相同规范路径后缀和准确文件名。只接受本地绝对路径；拒绝 UNC、设备路径、NTFS alternate stream、硬链接、符号链接/重解析目录、只读文件和超过 2 MiB 的文件。文件访问权限不足时直接显示失败，不修改 ACL、不移除只读属性、不提权。

预览对象只有引擎可构造，包含完整路径、版本、原始/目标 SHA-256、逐项变化和提醒。目标字节不暴露给 UI。应用要求同一引擎创建、15 分钟内、尚未使用的预览，并重新解析当前文件、核对哈希和目标哈希。UI 对所有游戏写入提供会话开关和每次确认；Performance 免费，Competitive / Balanced 另外校验 Premium。恢复不依赖 Premium。

应用与恢复都在准备时及实际替换前检查固定游戏进程。VALORANT 还检查 `RiotClientServices`。检查失败时阻止写入。应用不会关闭游戏、修改运行进程、注入模块或操作反作弊。

文本转换只替换指定值所在字符区间。UTF-8（可带 BOM）、UTF-16 LE/BE（带 BOM）、原换行、注释、缩进、其余配置字节保留；恢复直接写回原始字节，不重新序列化配置。未知编码和未支持语法拒绝操作。

## 备份与恢复

客户端把引擎数据目录传入 `GameProfileEngine`。每条记录存于 `<数据目录>/<记录ID>/`：

- `original.bin`：应用前精确原始字节。
- `target.bin`：预览中将写入的字节。
- `record.json`：路径、格式、哈希、逐项修改、状态及时间。
- `before-restore-<SHA256>.bin`：恢复前的文件，包括明确强制恢复时的外部编辑。
- `displaced-<SHA256>.bin`：原子替换实际置换的文件；去重保存。

原始/目标备份使用创建新文件、WriteThrough 和 `Flush(true)`；原始备份回读验证后，Pending 记录也必须落盘，才允许替换配置。配置同目录的临时文件通过 `File.Replace` 原子替换，回读必须匹配目标哈希。Steam 位于另一块盘也可工作：置换捕获先留在游戏同一卷，再持久化到恢复目录；保留成功后才删除程序自己的临时 sidecar。若捕获归档失败，sidecar 留在配置旁供排障，原始备份仍保留。

引擎目录有跨实例文件锁，实例内操作也串行。重新打开程序时，Pending/Restoring 记录依实际文件哈希分类为已应用、未执行、已恢复或冲突；不会因启动检查自动修改游戏文件。普通恢复遇到外部更改只返回 Conflict；强制恢复必须由 UI 再次明确确认，并先保存该外部版本。最终写入前发生的新冲突，即使强制恢复也不会直接覆盖。

0.4 的 Recovery → Restore all changes 同时尝试恢复 Windows 设置与游戏配置。游戏引擎在一个锁内按最新记录优先恢复；同一文件发生冲突、游戏仍在运行或恢复失败时，保留备份并阻止更早记录覆盖它，其他文件仍继续恢复。单条强制恢复也不能越过同一文件尚未恢复的新记录。已经恢复或没有变化的记录不会重复写入。

文件系统没有面向所有外部进程的内容 compare-and-swap。引擎执行临近替换的路径/哈希检查，并捕获实际置换文件；若极短窗口内有外部 rename 竞态，会保留其字节，在当前文件仍等于本次目标时尝试退回外部版本，并报告失败。程序不会声称能阻止游戏/同步工具在操作结束之后再次覆盖配置。

最多保留 500 条记录。单条恢复记录最多新增 64 种不同的外部版本，再次遇到同一内容会复用已校验备份。原始备份不自动删除。备份文件或日志损坏时恢复被阻止；缺失配置不会被本版强制自动重建，原始 `.bin` 仍可供人工恢复。备份可能包含配置中原有账号字段，保留在本地用户目录、不上传；归档、分享或删除前应按个人数据处理。

## 可重复验收

运行：

```powershell
dotnet run --project desktop/Radiant.Games.Tests -c Release
```

2026-10-10：47 项测试通过。覆盖三适配器原有双预设、所有免费预设与帧率选项、默认保留帧率上限、精确恢复、BOM/换行、未知版本/语法、缺键/重复、可选字段、文件缺失/只读/硬链接/实际目录符号链接、路径边界、有限发现、伪造预览、外部编辑竞态及置换捕获、游戏状态阻止、备份/日志失败、回读失败、进程检测不可用、跨实例锁、取消、中断重开、恢复冲突与重试。批量恢复还验证同一文件的先后顺序、重复恢复不写入、冲突阻止更早记录、运行中的游戏不阻止恢复其他游戏，以及拒绝乱序单条强制恢复。所有写入仅在生成的临时测试目录内；本机实际游戏目录只做存在性检查，未写入。

这些测试验证解析和文件事务行为。正式宣称某游戏当前发行版本完整兼容前，仍需在拥有该游戏的测试机上分别验证：游戏菜单中的原值 → 退出 → 预览/应用 → 启动后菜单值 → 退出/恢复 → 重启后原菜单值，以及该账号的云同步行为。

## 0.5 自定义 Premium 配置

新增逐项选择与命名保存：CS2 的 VSync/MSAA；Fortnite 的 FPS 上限、VSync 及已有画质字段；VALORANT 的常开 FPS 上限、材质和细节质量。每项默认 Keep current，未选字段不改动。选择的可选键缺失时显示提示，不插入新键。自定义 FPS 上限允许 30–1000，UI 提供常见值；CS2 不修改帧率上限。

命名配置保存在独立 `saved-game-profiles` 文件夹，最多 200 份不可变副本。保存或加载不会改游戏文件，应用前仍需产生当前文件的不可变预览。恢复日志内保留完整 `custom-v1` 选项，无需依赖命名配置文件，因此删掉或更改命名配置不影响恢复旧日志。

0.5 游戏测试为 **51/51**，新增自定义字段隔离、版本/边界校验、重启后精确恢复、命名配置保存/读取/损坏和预览竞态用例。原有 47 项仍通过。所有写入均限于生成的测试文件。
