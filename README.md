# ClearNest · 清巢（WinUI 3 / Windows 11 x64）

这是可继续开发的源码版本，尚未在 Windows 上编译或实机验收，不是已经验证的成品安装包。

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
附带 GitHub Actions Windows 构建配置；没有在本次交付中运行此工作流。

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

## Windows 验收清单（待执行）

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
