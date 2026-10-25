# Launcher

使用方法、依赖与测试命令见仓库根目录 [README](../README.md)。

- `src/`：WPF 界面、配置处理和进程管理。
- `tests/`：合成配置、可控进程和离屏界面验证。
- `assets/icon-v1/`：ICO、PNG 和图标生成提示词。
- `build.cmd` / `build.ps1`：编译启动器，复制产物到仓库根目录。
- `test.cmd`：构建并执行两套测试。

`build/`、`logs/`、`backups/`、`state.json`、`tools.json` 为本地生成数据，不提交。
