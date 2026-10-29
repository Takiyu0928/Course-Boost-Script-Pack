# 脚本启动路径探测（v0.1.2）

打开 TimeSaver.exe，选择工具，在“启动”页点击“探测启动路径”。

窗口显示当前目录、各入口文件的存在状态，并扫描整合包目录内的候选。也可点击“选择文件夹…”在其他目录中查找。选中候选后点击“使用所选路径”，确认保存；随后启动、账号设置、配置备份均使用所选工具目录，重启启动器后仍然有效。

探测不执行 EXE，不移动或复制工具文件，不读取账号密码。默认最多检查 4 层、1000 个目录、50 个候选，跳过 Profile、_internal、node_modules、.git、launcher、模型缓存目录及目录链接。扫描范围受限时可直接选择更接近工具的文件夹。

候选需包含该工具的 EXE 及必需配置。学习通支持根目录 main.exe 或 task/main.exe；domooc 要求独立 Profile 的 prefs.js 和 user.js 齐全。存在这些文件并不代表脚本运行或在线登录已经通过验收。

应用路径前需退出对应工具，并保存设置页的未保存修改。路径记录在 launcher/tool-paths.json，修改已有路径记录时自动备份；不会更改课程工具自己的账号配置。若工具再次移动，重新探测并应用；要恢复原目录，选择原目录后应用即可。记录为绝对路径，因此整合包整体搬家后可能需要重新探测。

新增源码 launcher/src/Model/ToolPathProbe.cs；修改 ToolCatalog.cs、MainWindow.cs；新增 ToolPathProbeTests.cs，并更新 ConfigTests.cs、OtherAccountTests.cs。已更新本地 TimeSaver.exe 及 launcher/build 构建产物，旧 EXE 备份在 launcher/backups/application/TimeSaver-before-path-probe-*.exe.bak。

验证：.NET Framework 构建成功；核心测试 263 PASS / 0 FAIL，离屏界面测试 98 PASS / 0 FAIL。路径测试使用临时假文件，覆盖改名目录、扫描边界、无效候选、持久化与重载、备份、学习通目录结构、domooc 必需文件和损坏路径配置。未执行真实课程工具或在线登录，也未自动操作原生文件夹选择窗口。个人启动路径记录和运行时备份不随发行包分发。
