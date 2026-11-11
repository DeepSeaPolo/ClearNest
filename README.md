# ClearNest · 清巢（WinUI 3 / Windows 11 x64）

已通过 GitHub Actions Windows 编译、GUI 主窗口启动检查和 10 项核心清理保护测试。这是首版便携工具，尚未完成 Windows 11 全功能人工验收；内存回收 Native API 和托盘/迷你窗口交互仍需用户实机验证。

[直接下载 Windows 运行包](https://github.com/DeepSeaPolo/ClearNest/releases/download/v0.3.0/ClearNest-v0.3.0-win11-x64.exe) · [发布页面](https://github.com/DeepSeaPolo/ClearNest/releases/tag/v0.3.0)。单文件版本直接运行 EXE；包体包含 .NET 与 WinUI 运行时。旧版便携 ZIP 仍须完整解压。

## v0.3.0 单文件、自动回收与实时趋势

当前功能位于开发分支及 PR #3；尚未获得主分支合并与公开发布的确认。上方正式发布链接将在合并并通过发布检查后生效。

- 官方 .NET/WinUI 自包含单文件发布：一个 EXE 包含运行时、原生依赖、界面和图标。首次启动自动在 bundle 缓存展开依赖，不要求 EXE 旁有依赖目录。设置仍保存到用户配置目录。
- 构建使用 `./build.ps1 -SingleFile`。检查会将唯一 EXE 复制到空目录，再运行主题和主/Mini 切换测试。
- 图标改为蓝紫青扁平几何色块，嵌入 EXE、标题栏、任务栏与托盘；启动项导航使用 Windows 11 的 SpeedHigh 仪表盘符号。
- 内存卡片使用真实物理内存采样，展示使用中容量、占用率、可用容量与淡色面积趋势图。最多保留 120 个样本，纵轴随实际数据调整；没有模拟历史数据。
- 首页可启用每 5～1440 分钟的自动待机缓存回收，或 1～99% 的占用率阈值；默认均关闭，需要管理员权限。
- 两次自动回收至少相隔 5 分钟；阈值触发后，需降至阈值以下 5 个百分点才会重新武装。休眠恢复不会补跑多次任务。
- 自动任务只在应用运行时执行，仅使用待机缓存回收接口，不处理进程工作集、文件或注册表。原生回收失败会暂停自动任务；修改自动设置后可重新启用。
- 文件和启动项清理仍要求预览、选择和确认。高占用率不保证会因待机缓存回收而下降。

## v0.2.0 仪表盘与主题

- 借鉴 FlClash 的卡片化与信息集中方式，使用独立编写的原生 WinUI 3 实现。
- 首页平铺内存、临时文件、注册表启动项、进程摘要和后台常驻开关；扫描只更新摘要，详细候选列表按需打开。
- 页面按宽度调整为三列、两列或单列，较小窗口可纵向滚动。
- 主题支持跟随系统、浅色、深色，提供鸢尾蓝、紫罗兰、青绿、玫瑰和琥珀强调色，以及云母/纯色背景。
- 主题与 Mini 置顶、启动时进入 Mini、后台常驻、2/5/10 秒刷新间隔自动保存到本机 preferences.json。
- 主窗口与 Mini 使用居中标题区域，保留原生拖动和系统窗口按钮；Mini 继续与主窗口互斥。

## v0.1.1 窗口与界面更新

- 使用原生 WinUI 3 NavigationView 侧栏、系统主题卡片和强调色按钮；没有使用 WebView、HTML 或 Electron 界面。
- 切换 Mini 时隐藏主窗口，返回主窗口时隐藏 Mini，同时只显示一个窗口。
- Mini 内回收内存的确认框和结果提示保留在 Mini 内。
- 关闭 Mini：常驻关闭时返回主窗口；常驻开启时隐藏至托盘。
- Windows 自动验证反复切换三次，检查两个原生 HWND 的可见状态。

## 构建与运行

Windows 11 x64；Visual Studio 2022 的 WinUI / Windows 应用开发工具、Windows SDK 和 .NET 8 SDK。

在源码目录运行 PowerShell：

```powershell
.\build.ps1 -Portable
.\publish\portable\ClearNest.exe
```

Portable 为自带 .NET 和 Windows App SDK 的完整目录，不能只复制 EXE。
默认 ` .\build.ps1 ` 为较小的框架依赖目录，需要预装 .NET 8 Runtime x64 和 Windows App Runtime 1.8 x64。
WinUI 3 和 .NET 本身有运行时成本；不承诺极小安装体积。SDK 固定为 1.8.12 以便复现；该分支已结束维护，正式发布前需要迁移到受支持分支并回归测试。
附带 GitHub Actions Windows 构建配置；已在 Windows Server 2025 构建并通过主窗口启动检查。测试不主动清空系统待机缓存，只操作自动生成的临时文件与测试注册表值。

## 已实现范围

- 原生 WinUI 3 主窗口、Mica 背景、可关闭且置顶的迷你窗口。
- 每 5 秒显示物理内存负载及可用量，进程扫描时进行短时间 CPU 采样。
- 进程列表按工作集排序；提示无窗口且短时低 CPU 的大内存进程，但不推断其无用，不结束进程。
- 手动清空系统 Standby list：仅非修改待机页，不调用工作集压缩或清空修改页的功能。
- C 盘当前用户临时目录和 Windows Temp：仅最后修改超过 7 天的文件，预览、多选、二次确认后永久删除。
- 跳过重解析点，不递归跟随目录链接；不扫描下载、文档、浏览器数据、WinSxS、驱动或系统更新缓存。
- 当前用户 Run 启动项检查：仅带引号的绝对 C 盘 EXE 路径且确定缺失的项。模糊、网络和无权限路径跳过。
- 注册表先写入并刷新 JSON 备份再删除，恢复时保留现有同名键值。没有全局注册表“优化”。
- 后台常驻开关持久化；关闭主窗口后留在托盘；双击托盘恢复；退出会销毁迷你窗口和托盘。
- 不含自动清理或开机启动，不需要联网运行。

## 内存边界

Windows 已把待机缓存计入可用内存；清空它主要改变待机页与空闲页的分布，不应当被宣传为增加可用内存或一定加速。
内存按钮需要以管理员身份启动程序以获得 SeProfileSingleProcessPrivilege。
NtSetSystemInformation / SystemMemoryListInformation 是非公开的系统接口，存在系统版本兼容风险。仅传入 MemoryPurgeStandbyList=4；失败时报告错误，没有其他回退清理。
不访问进程内存，不调用 EmptyWorkingSet、SetProcessWorkingSetSize、EmptyWorkingSets、FlushModifiedList，也不终止进程。
这能避免主动挤出正在使用的工作集，但无法保证清缓存对程序性能毫无影响。若你要求只使用微软公开支持的稳定接口，应禁用该按钮，由 Windows 自行回收缓存。

## 清理边界

删除文件不等于确认该文件无用。7 天仅为候选规则，必须自行核对。
文件独占打开后复核大小和最后修改时间，再通过同一句柄标记删除，避免删除正被占用的文件；与扫描前完全相同的元数据不能证明内容相同。
路径和注册表复核可减少竞态，仍非对抗恶意本机程序的安全边界。不建议在管理员模式下清理不可信用户可写的目录。
备份在 `%LOCALAPPDATA%\ClearNest\RegistryBackups`。备份可包含私人路径和启动命令，请妥善保管。
注册表删除并非事务；部分失败时以报告的数量为准，备份可用于恢复。无法确定是否无用的项不会自动判定。

## Windows 11 完整人工验收清单（待执行）

1. 执行两种发布配置，运行主窗口，切换四个标签页。
2. 开关常驻，关闭/恢复主窗口，打开/关闭迷你窗口，退出后验证无残留进程与托盘。
3. 非管理员点击回收应明确提示；管理员回收只使用命令 4，不应改变应用工作集。
4. Temp 中创建超过 7 天和新文件；扫描只出现旧文件；选择其中一个确认删除。
5. 用 FileShare.None 锁定旧文件；清理应跳过；修改已扫描文件后清理也应跳过。
6. 创建 Temp 内外目录链接；不得遍历链接目标。
7. HKCU Run 创建测试值，命令为 `"C:\ClearNestMissingTest\gone.exe"`；扫描、备份、删除、恢复。
8. 创建同名现有值后恢复备份；必须跳过，不覆盖。
9. 空列表、访问被拒、备份目录只读、系统拒绝 Native API 时应显示错误而不崩溃。

参考：
https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app
https://learn.microsoft.com/en-us/windows/win32/memory/memory-performance-information
https://learn.microsoft.com/en-us/windows/win32/memory/working-set

## 已执行的自动验证

- 内存状态读取有效。
- 旧的已选临时文件可以删除。
- 近期文件、扫描后变化的文件、被独占打开的文件和允许目录之外的文件均保留。
- 目标缺失的测试启动项可识别、备份后删除、精确恢复。
- 恢复跳过现有同名值。
- 自包含发布生成 Windows PE EXE；GUI 主窗口成功出现，启动 12 秒内未退出。

各版本源码以对应发布标签为准；自动测试记录位于 Actions。完整 Windows 11 人工验收清单仍适用。
