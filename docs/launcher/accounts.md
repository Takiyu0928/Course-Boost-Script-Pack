# 四工具账号与登录入口（v0.1.2）

四个工具的“启动”页现在都提供“账号配置”按钮，跳转到设置页顶部。

| 工具 | 账号配置方式 |
| --- | --- |
| Autovisor | 使用设置页现有账号、密码表单，点击“保存设置” |
| UnipusAI | 使用 U 校园账号、密码表单，点击“保存账号” |
| 学习通 | 新增账号 / 手机号、密码表单，点击“保存账号”；写入实际工具的 task/tool/account_info.json |
| domooc | 设置页点击“打开 MOOC 登录页面”，使用随包 Firefox 的独立 Profile 登录；浏览器已运行时在原窗口访问中国大学 MOOC 网站 |

domooc 没有供脚本读取的独立账号密码配置，其登录态由网站和浏览器管理，因此启动器不另存无法生效的密码。更换账号在网页退出后重新登录，不删除 Cookie 或 Profile。

学习通账号密码默认不回显，留空保留，勾选清除才清除。更换账号需新密码，并清除旧课程/文件选择；API、浏览器及未知设置保留。保存前备份配置，工具运行时不保存。目录识别兼容本机“学习通/exe)”及旧“xuexitong_shuake26.10/exe)”，并适配 task/main.exe。

以下是 U 校园账号配置的具体说明：

打开根目录 TimeSaver.exe → 选择 UnipusAI → 启动页“账号配置”。设置页顶部新增 U 校园账号及密码，点击“保存账号”；下方 AI 与学习设置仍使用“保存设置”。

- 密码隐藏显示，已保存密码不回显；同一账号密码留空保留原值，勾选清除可删除已保存密码。
- 更换账号必须填写新密码。更改凭证会清除旧登录信息；更换账号还会清除旧课程选择，重新启动 UnipusAI 后登录并重新选课。
- 保存账号前自动备份 config.json。账号密码按第三方工具要求保存在本机 config.json，备份同样含登录信息，请勿公开。
- 账号密码和 API Key 使用独立草稿；分别保存不会丢弃另一部分未保存的输入。
- 检测到 UnipusAI 进程运行时不保存账号，请先退出工具。

发行包不包含个人账号配置、登录态或备份；使用前请自行配置账号。

验证：.NET Framework 构建成功；核心测试 263 PASS / 0 FAIL，离屏界面测试 98 PASS / 0 FAIL。新增测试使用临时假配置，覆盖四工具入口、学习通目录和配置识别、MOOC 登录参数、备份、换账号清理、密码与 API Key 隔离、草稿切换与分别保存。未进行真实账号在线登录验收，也未在测试中启动浏览器或课程工具。

修改源码：launcher/src/Model/UnipusSettings.cs、XuexitongAccountSettings.cs、ToolCatalog.cs、launcher/src/Ui/MainWindow.cs。
测试：launcher/tests/AccountTests.cs、OtherAccountTests.cs、ConfigTests.cs、UiSmoke.cs。
构建产物：TimeSaver.exe、launcher/build/TimeSaver.exe、launcher/build/TimeSaver.Tests.exe。
旧根目录 EXE 备份在 launcher/backups/application/TimeSaver-before-*.exe.bak。
