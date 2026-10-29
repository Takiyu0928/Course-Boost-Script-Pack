# Time Saver

<img src="launcher/assets/icon-v1/time-saver-icon.png" width="96" alt="Time Saver icon">

几个主流网课平台脚本的懒人整合包，为了方便管理加了一个启动器。

启动器支持集中管理本地工具的启动、环境检查、常用配置、日志和备份。

## 功能

- 四个内置适配：Autovisor、UnipusAI、domooc、学习通工具；也可用本地 `launcher/tools.json` 添加启动入口。
- 启动 / 正常停止 / 显式强制结束；只管理启动器自己启动的进程。
- Autovisor INI 与 UnipusAI JSON 常用配置表单；保存前备份，保留未知字段。
- 未保存编辑在内存保留，支持切换工具与页面；启动操作使用已保存配置。
- 五页签：启动、设置、备份、诊断、帮助；托盘驻留与窗口位置记忆。
- 秒表与闪电图标已嵌入 EXE、窗口和托盘。
- 四工具统一“账号配置”入口：Autovisor、UnipusAI、学习通提供账号密码表单，domooc 使用随包 Firefox 网页登录；保存配置前自动备份。
- “探测启动路径”检查入口文件、发现改名目录，支持选择文件夹和记住路径；启动和账号设置会同步使用所选目录，探测不执行脚本。

## 项目来源

| 接入工具 | 原项目仓库或名称 |
| --- | --- |
| Autovisor | [CXRunfree/Autovisor](https://github.com/CXRunfree/Autovisor) |
| UnipusAI | [Zzj-klwgxdz/UnipusAI](https://github.com/Zzj-klwgxdz/UnipusAI) |
| 学习通刷课脚本 | [Mortal004/Xuexitong_shuake](https://github.com/Mortal004/Xuexitong_shuake) |
| domooc 本机版 | domooc |

本启动器负责本地工具的启动与配置管理，相关工具由原项目各自维护。完整整合包包含各工具的许可证与对应源码归档；学习通脚本仅限非商业用途。

## 获取与构建

发行包与版本说明见 [Releases](https://github.com/Takiyu0928/Course-Boost-Script-Pack/releases)。

v0.1.2 完整包已包含最新启动器、四工具、U 校园使用说明及增量补抓 CMD。已有旧完整包的用户，可下载 `TimeSaver-v0.1.2-launcher-update.zip`，先从托盘退出启动器，再解压到原根目录并合并覆盖。

用法见 [账号配置](docs/launcher/accounts.md)、[启动路径探测](docs/launcher/path-probe.md)、[UnipusAI 使用说明](docs/unipus/U校园使用说明.md)。补抓不解决服务端权限与限流问题。

需要 Windows x64、.NET Framework 4.8 及其 `Framework64` 编译器。本项目使用系统编译器构建，不需要 .NET SDK、Visual Studio 或 NuGet 包。

在仓库根目录执行：

```powershell
.\launcher\build.cmd
```

双击根目录生成的 `TimeSaver.exe`。构建同时保留 `launcher/build/TimeSaver.exe`。程序正在运行时先从托盘退出，再重新构建。

## 配置本地工具

Release 分为仅启动器包与 `full` 完整整合包。完整包已包含四个工具与干净配置；选择仅启动器包时，请自行准备对应工具，将文件夹放在同级目录：

```text
time-saver/
  TimeSaver.exe
  launcher/
  Autovisor-3.18.4-windows-amd64/
  UnipusAI-v3.6-windows-x86-64/
  domooc_free/
  xuexitong_shuake26.10/
```

启动器会显示缺失文件和环境依赖。domooc 本地题库模式另需 Node.js；手动使用其 Python 自动化程序时另需 Python。第三方工具的授权与运行要求由各自项目决定。

在“设置”页修改配置并点击保存；空白密钥输入默认保留已存值，清除需显式勾选。配置备份、状态和日志位于本地 `launcher/` 下，已加入 `.gitignore`。

在“启动”页点“账号配置”可进入对应工具的设置；U 校园和学习通使用“保存账号”，Autovisor 使用“保存设置”。domooc 点击“打开 MOOC 登录页面”，在其独立浏览器中登录或切换账号。账号密码按第三方工具要求保存在本机配置中，备份也可能包含凭证，请勿公开。

工具被移动或改名时，点击“探测启动路径”，选择候选目录后应用。选择记录仅保存在本机 `launcher/tool-paths.json`；账号、登录态及个人路径不随发行包分发。

关闭窗口默认隐藏到托盘；从托盘菜单退出，或在“帮助”页取消“关闭窗口时隐藏到托盘”。退出启动器不会结束已启动工具。

## 测试

```powershell
.\launcher\test.cmd
```

最近验证：核心 263 项、离屏界面与草稿 98 项通过。测试使用合成配置和可控辅助进程，不启动真实课程工具；UI 测试不显示窗口、不控制光标。

测试覆盖参数转义、路径解析、进程所有权、INI/JSON 保存、密钥保留与清除、备份恢复、端口身份、日志脱敏，以及下拉框连续重建和改回原值。

## 当前限制

- 真实平台运行、AI 供应商连接及代理/TUN 兼容性需使用具体第三方工具另行验证。
- 学习通工具的 `start.exe` / `main.exe` 实际入口尚需人工确认，界面会提示。
- 启动器不提供第三方工具未公开的课程进度与暂停接口。
- 停止只作用于已记录的所属进程，不结束其浏览器等子进程。
- 修改后改回文件原值，仍可能显示未保存提示；显示和保存值一致。

图标由内置图像生成工具制作，提示词保存在 `launcher/assets/icon-v1/prompt.txt`。
