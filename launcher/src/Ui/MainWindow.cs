using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TimeSaver.App;
using TimeSaver.Core;
using TimeSaver.Model;

namespace TimeSaver.Ui
{
    /// <summary>
    /// Left: tool list. Right: tabs for the selected tool.
    ///
    /// The first tab is only what a user needs to act (start / stop / open things). Technical
    /// background (entry evidence, PyInstaller layout, environment variables, environment
    /// diagnostics) lives in the 诊断 and 帮助 tabs so it cannot push the settings entry off screen.
    ///
    /// Honesty rules: a running process is reported as "进程在运行", never as a course in
    /// progress; no pause button exists because no tool exposes one; destructive actions ask first.
    /// </summary>
    public sealed class MainWindow : Window
    {
        private const string RepositoryUrl = "https://github.com/Takiyu0928/Course-Boost-Script-Pack";
        private readonly LauncherContext _context;
        private readonly WindowStateStore _state;
        private readonly string _statePath;

        private ListBox _toolList;
        private TabControl _tabs;
        private TextBlock _statusLine;
        private TextBlock _processLine;
        private TextBlock _serviceLine;
        private Button _stopButton;
        private Button _forceButton;
        private DispatcherTimer _timer;

        private ToolDefinition _current;
        private ToolMode _currentMode;
        private string _lastMessage = "就绪";

        private System.Windows.Forms.NotifyIcon _tray;
        private bool _exiting;

        /* ---- 未保存草稿：按工具保存在内存里 ----------------------------------
         * 切工具 / 切模式 / 启动 / 立即备份 都会重建页签；没有草稿时用户刚输入的
         * 内容会被静默丢掉。草稿只活在本进程内存：secret 只来自 PasswordBox 自身，
         * 绝不写入 state.json 或日志。保存成功后清除。 */
        private sealed class FormDraft
        {
            public readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.Ordinal);
            public string Secret = string.Empty;
            public string AccountSecret = string.Empty;
            public bool Dirty;
        }

        private readonly Dictionary<string, FormDraft> _drafts =
            new Dictionary<string, FormDraft>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TextBlock> _hintBlocks =
            new Dictionary<string, TextBlock>(StringComparer.OrdinalIgnoreCase);

        /* 测试接缝：非 null 时替代 MessageBox，避免离屏测试被模态对话框卡住 */
        internal Func<string, string, bool> ConfirmHook { get; set; }
        internal Action<string, string> AlertHook { get; set; }

        public MainWindow(LauncherContext context, WindowStateStore state, string statePath)
        {
            _context = context;
            _state = state;
            _statePath = statePath;

            Title = "Time Saver · 工具整合启动器";
            using (Stream iconStream = typeof(MainWindow).Assembly.GetManifestResourceStream("TimeSaver.AppIcon.ico"))
            {
                Icon = BitmapFrame.Create(iconStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            }
            Background = Theme.Background;
            FontFamily = Theme.UiFont;
            FontSize = Theme.FontBody;
            Foreground = Theme.Ink;
            MinWidth = 900;
            MinHeight = 560;
            Width = double.IsNaN(_state.Width) ? 1100 : _state.Width;
            Height = double.IsNaN(_state.Height) ? 720 : _state.Height;
            if (!double.IsNaN(_state.Left)) Left = _state.Left;
            if (!double.IsNaN(_state.Top)) Top = _state.Top;
            if (_state.Maximized) WindowState = WindowState.Maximized;

            Content = BuildLayout();
            BuildTray();
            if (_toolList.Items.Count > 0) _toolList.SelectedIndex = 0;

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(1500);
            _timer.Tick += delegate { RefreshRuntimeStatus(); };
            _timer.Start();

            Closing += OnClosing;
            Closed += OnClosed;
        }

        private UIElement BuildLayout()
        {
            DockPanel root = new DockPanel();
            root.LastChildFill = true;

            Border header = new Border();
            header.Background = Theme.Background;
            header.BorderBrush = Theme.Line;
            header.BorderThickness = new Thickness(0, 0, 0, 1);
            header.Padding = new Thickness(18, 14, 18, 12);
            DockPanel headerLayout = new DockPanel();
            Button repository = Theme.Button("项目仓库 ↗", false, false);
            repository.ToolTip = RepositoryUrl;
            repository.VerticalAlignment = VerticalAlignment.Center;
            repository.Margin = new Thickness(16, 0, 0, 0);
            repository.Click += delegate { OpenRepository(); };
            DockPanel.SetDock(repository, Dock.Right);
            headerLayout.Children.Add(repository);
            StackPanel headerPanel = new StackPanel();
            headerPanel.Children.Add(Theme.Title("Time Saver"));
            headerPanel.Children.Add(Theme.Muted13("选择左侧工具 → 启动。配置只在点“保存设置”时写入，并先自动备份。"));
            headerLayout.Children.Add(headerPanel);
            header.Child = headerLayout;
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            Border footer = new Border();
            footer.Background = Theme.Surface;
            footer.BorderBrush = Theme.Line;
            footer.BorderThickness = new Thickness(0, 1, 0, 0);
            footer.Padding = new Thickness(18, 8, 18, 8);
            _statusLine = Theme.Muted13("就绪");
            footer.Child = _statusLine;
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(264) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _toolList = new ListBox();
            _toolList.Background = Theme.Background;
            _toolList.BorderBrush = Theme.Line;
            _toolList.BorderThickness = new Thickness(0, 0, 1, 0);
            _toolList.Padding = new Thickness(8);
            _toolList.SelectionChanged += delegate { OnSelectionChanged(); };
            foreach (ToolDefinition tool in _context.Catalog.Tools) _toolList.Items.Add(BuildToolListItem(tool));
            Grid.SetColumn(_toolList, 0);
            grid.Children.Add(_toolList);

            _tabs = new TabControl();
            _tabs.Background = Theme.Background;
            _tabs.BorderThickness = new Thickness(0);
            _tabs.Padding = new Thickness(0);
            _tabs.FontFamily = Theme.UiFont;
            _tabs.FontSize = Theme.FontBody;
            _tabs.Margin = new Thickness(12, 10, 12, 10);
            Grid.SetColumn(_tabs, 1);
            grid.Children.Add(_tabs);

            root.Children.Add(grid);
            return root;
        }

        private ListBoxItem BuildToolListItem(ToolDefinition tool)
        {
            ListBoxItem item = new ListBoxItem();
            item.Tag = tool;
            item.Padding = new Thickness(8);
            item.Margin = new Thickness(0, 0, 0, 2);
            item.BorderBrush = Theme.Line;
            item.BorderThickness = new Thickness(0, 0, 0, 1);

            StackPanel panel = new StackPanel();
            TextBlock name = Theme.Body(tool.Name);
            name.FontWeight = FontWeights.SemiBold;
            name.TextWrapping = TextWrapping.Wrap;
            panel.Children.Add(name);

            TextBlock sub = Theme.Muted13(StatusHint(tool));
            sub.Margin = new Thickness(0, 3, 0, 0);
            sub.TextWrapping = TextWrapping.Wrap;
            panel.Children.Add(sub);
            _hintBlocks[tool.Id] = sub;

            item.Content = panel;
            return item;
        }

        private string StatusHint(ToolDefinition tool)
        {
            string text;
            if (_context.Supervisor.IsRunning(tool.Id)) text = "运行中（启动器启动）";
            else if (tool.Modes.Count > 1) text = tool.Modes.Count + " 种启动方式";
            else text = "未运行";
            if (HasDirtyDraft(tool.Id)) text += " · 有未保存修改";
            return text;
        }

        private void OnSelectionChanged()
        {
            ListBoxItem item = _toolList.SelectedItem as ListBoxItem;
            if (item == null) return;
            ToolDefinition tool = item.Tag as ToolDefinition;
            if (tool == null) return;
            bool changedTool = _current == null || !string.Equals(_current.Id, tool.Id, StringComparison.OrdinalIgnoreCase);
            _current = tool;
            if (changedTool || _currentMode == null)
                _currentMode = _current.Modes.Count > 0 ? _current.Modes[0] : null;
            RebuildTabs();
        }

        private void RebuildTabs()
        {
            if (_current == null) return;
            int selected = _tabs.SelectedIndex;
            _tabs.Items.Clear();
            _tabs.Items.Add(MakeTab("启动", BuildStartTab()));
            _tabs.Items.Add(MakeTab("设置", BuildSettingsTab()));
            _tabs.Items.Add(MakeTab("备份", BuildBackupTab()));
            _tabs.Items.Add(MakeTab("诊断", BuildDiagnosticsTab()));
            _tabs.Items.Add(MakeTab("帮助", BuildHelpTab()));
            _tabs.SelectedIndex = selected >= 0 && selected < _tabs.Items.Count ? selected : 0;
            RefreshToolListHints();   // 草稿被保存或清除后，左侧“有未保存修改”标记要同步
            RefreshRuntimeStatus();
        }

        private TabItem MakeTab(string header, UIElement content)
        {
            TabItem item = new TabItem();
            item.Header = header;
            item.FontFamily = Theme.UiFont;
            item.FontSize = Theme.FontBody;
            item.Foreground = Theme.Ink;
            item.Padding = new Thickness(14, 6, 14, 6);

            ScrollViewer scroller = new ScrollViewer();
            scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroller.Padding = new Thickness(16, 14, 16, 16);
            scroller.Background = Theme.Background;
            StackPanel stack = new StackPanel();
            stack.Children.Add(content);
            scroller.Content = stack;
            item.Content = scroller;
            return item;
        }

        /* ---------------- 启动 tab ---------------- */

        private UIElement BuildStartTab()
        {
            StackPanel panel = new StackPanel();
            panel.Children.Add(Theme.Title(_current.Name));

            TextBlock path = Theme.Muted13(_context.ToolDirectory(_current));
            path.Margin = new Thickness(0, 4, 0, 0);
            panel.Children.Add(path);

            {
                Button account = Theme.Button("账号配置", true, false);
                account.Margin = new Thickness(0, 12, 0, 0);
                account.HorizontalAlignment = HorizontalAlignment.Left;
                account.ToolTip = "设置当前工具账号或打开其登录入口";
                account.Click += delegate
                {
                    _tabs.SelectedIndex = 1;
                    ScrollViewer scroller = ((TabItem)_tabs.SelectedItem).Content as ScrollViewer;
                    if (scroller != null) scroller.ScrollToTop();
                };
                panel.Children.Add(account);
            }

            foreach (ToolEntryPoint entry in _current.Entries)
            {
                if (entry.Confirmed) continue;
                Border warn = new Border();
                warn.BorderBrush = Theme.AccentRed;
                warn.BorderThickness = new Thickness(1);
                warn.Background = Theme.Surface;
                warn.Padding = new Thickness(10, 6, 10, 6);
                warn.Margin = new Thickness(0, 10, 0, 0);
                StackPanel warnPanel = new StackPanel();
                warnPanel.Children.Add(Theme.Label("入口待确认：" + entry.Name, Theme.FontSmall, Theme.AccentRed));
                warnPanel.Children.Add(Theme.Muted13("说明文档只间接提到该入口，运行后请确认是否出现工具界面；也可在下方切换另一个入口。"));
                warn.Child = warnPanel;
                panel.Children.Add(warn);
            }

            TextBlock description = Theme.Body(_current.Description);
            description.Margin = new Thickness(0, 10, 0, 0);
            panel.Children.Add(description);

            if (HasDirtyDraft(_current.Id))
            {
                Border dirty = new Border();
                dirty.BorderBrush = Theme.AccentYellow;
                dirty.BorderThickness = new Thickness(1);
                dirty.Background = Theme.Surface;
                dirty.Padding = new Thickness(10, 6, 10, 6);
                dirty.Margin = new Thickness(0, 10, 0, 0);
                StackPanel dirtyPanel = new StackPanel();
                dirtyPanel.Children.Add(Theme.Label("设置页有未保存的修改", Theme.FontSmall, Theme.AccentYellow));
                dirtyPanel.Children.Add(Theme.Muted13("「启动」使用的是已经保存的配置；要让修改生效，请到「设置」页点“保存设置”。"));
                dirty.Child = dirtyPanel;
                panel.Children.Add(dirty);
            }

            if (_current.Modes.Count > 1) panel.Children.Add(BuildModeChooser());

            panel.Children.Add(BuildPreflightSummary());
            panel.Children.Add(BuildActions());

            StackPanel runtime = new StackPanel();
            runtime.Margin = new Thickness(0, 12, 0, 0);
            _processLine = Theme.Body("进程: 检查中…");
            runtime.Children.Add(_processLine);
            _serviceLine = Theme.Muted13("");
            _serviceLine.Margin = new Thickness(0, 4, 0, 0);
            runtime.Children.Add(_serviceLine);
            runtime.Children.Add(Theme.Muted13("运行中只代表进程活着；课程进度以工具自身界面和平台显示为准。"));
            panel.Children.Add(runtime);

            return panel;
        }

        private UIElement BuildModeChooser()
        {
            Border border = new Border();
            border.BorderBrush = Theme.Line;
            border.BorderThickness = new Thickness(1);
            border.Padding = new Thickness(12, 10, 12, 10);
            border.Margin = new Thickness(0, 12, 0, 0);

            StackPanel panel = new StackPanel();
            TextBlock header = Theme.Label("启动方式", Theme.FontSection, Theme.Ink);
            header.FontWeight = FontWeights.SemiBold;
            header.Margin = new Thickness(0, 0, 0, 6);
            panel.Children.Add(header);

            foreach (ToolMode mode in _current.Modes)
            {
                RadioButton radio = new RadioButton();
                radio.GroupName = "mode-" + _current.Id;
                radio.FontFamily = Theme.UiFont;
                radio.FontSize = Theme.FontBody;
                radio.Foreground = Theme.Ink;
                radio.Margin = new Thickness(0, 5, 0, 5);
                radio.Content = mode.Name + " — " + mode.Summary;
                ToolMode captured = mode;
                radio.Checked += delegate
                {
                    if (_currentMode != null && string.Equals(_currentMode.Id, captured.Id, StringComparison.OrdinalIgnoreCase))
                        return;
                    _currentMode = captured;
                    RebuildTabs();
                };
                if (_currentMode != null && string.Equals(_currentMode.Id, mode.Id, StringComparison.OrdinalIgnoreCase))
                    radio.IsChecked = true;
                panel.Children.Add(radio);
            }

            border.Child = panel;
            return border;
        }

        private UIElement BuildPreflightSummary()
        {
            PreflightReport report = _context.Runner.Preflight(_current, _currentMode);
            int ok = 0;
            int warn = 0;
            int bad = 0;
            List<PreflightItem> problems = new List<PreflightItem>();
            foreach (PreflightItem item in report.Items)
            {
                if (!item.Ok) { bad++; problems.Add(item); }
                else if (item.Warning) { warn++; problems.Add(item); }
                else ok++;
            }

            StackPanel panel = new StackPanel();
            panel.Margin = new Thickness(0, 12, 0, 0);

            TextBlock summary = Theme.Label(
                "预检：通过 " + ok + " · 注意 " + warn + " · 失败 " + bad,
                Theme.FontBody,
                bad > 0 ? Theme.AccentRed : (warn > 0 ? Theme.AccentYellow : Theme.OkInk));
            panel.Children.Add(summary);

            int shown = 0;
            foreach (PreflightItem item in problems)
            {
                if (shown >= 3) break;
                TextBlock row = Theme.Muted13((item.Ok ? "注意 · " : "失败 · ") + item.Name + "：" + item.Detail);
                row.Margin = new Thickness(0, 4, 0, 0);
                panel.Children.Add(row);
                shown++;
            }
            if (problems.Count > shown)
                panel.Children.Add(Theme.Muted13("… 其余 " + (problems.Count - shown) + " 项见「诊断」页"));

            return panel;
        }

        private UIElement BuildActions()
        {
            StackPanel panel = new StackPanel();
            panel.Margin = new Thickness(0, 14, 0, 0);

            StackPanel first = new StackPanel();
            first.Orientation = Orientation.Horizontal;
            Button launch = Theme.Button("启动", true, false);
            launch.Click += delegate { OnLaunch(); };
            _stopButton = Theme.Button("正常停止", false, false);
            _stopButton.Click += delegate { OnStop(false); };
            _forceButton = Theme.Button("强制结束", false, true);
            _forceButton.Click += delegate { OnStop(true); };
            first.Children.Add(launch);
            first.Children.Add(_stopButton);
            first.Children.Add(_forceButton);
            panel.Children.Add(first);

            StackPanel second = new StackPanel();
            second.Orientation = Orientation.Horizontal;
            second.Margin = new Thickness(0, 8, 0, 0);
            Button dir = Theme.Button("打开目录", false, false);
            dir.Click += delegate { OpenPath(_context.ToolDirectory(_current)); };
            Button config = Theme.Button("打开配置", false, false);
            config.IsEnabled = _current.ConfigKind != ToolConfigKind.None;
            config.Click += delegate { OnOpenConfig(); };
            second.Children.Add(dir);
            second.Children.Add(config);
            Button probe = Theme.Button("探测启动路径", false, false);
            probe.Click += delegate { OnProbeToolPath(); };
            second.Children.Add(probe);
            panel.Children.Add(second);

            return panel;
        }

        /* ---------------- 设置 tab ---------------- */

        private void OnProbeToolPath()
        {
            ToolDefinition tool = _current;
            if (tool == null) return;
            Window dialog = new Window
            {
                Owner = this, Title = "探测启动路径 · " + tool.Name,
                Width = 760, Height = 480, MinWidth = 620, MinHeight = 380,
                Background = Theme.Background, FontFamily = Theme.UiFont,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            StackPanel panel = new StackPanel { Margin = new Thickness(18) };
            panel.Children.Add(Theme.Label("当前目录：" + _context.ToolDirectory(tool), Theme.FontBody, Theme.Ink));
            foreach (ToolEntryPoint entry in tool.Entries)
            {
                string path = Path.Combine(_context.ToolDirectory(tool), entry.ExeRelativePath);
                panel.Children.Add(Theme.Muted13((File.Exists(path) ? "已找到 · " : "缺失 · ") + entry.Name + "：" + path));
            }
            panel.Children.Add(Theme.Muted13("默认只在整合包目录内探测（最多 4 层、1000 个目录）；也可选择其他文件夹。探测不会执行脚本，找到候选后需选择并应用。"));
            ListBox list = new ListBox { Height = 140, Margin = new Thickness(0, 10, 0, 8) };
            panel.Children.Add(list);
            TextBlock status = Theme.Muted13(string.Empty);
            panel.Children.Add(status);
            StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            Button scan = Theme.Button("重新探测", false, false);
            Button browse = Theme.Button("选择文件夹…", false, false);
            Button apply = Theme.Button("使用所选路径", true, false);
            apply.IsEnabled = false;
            Action<string> search = delegate(string root)
            {
                list.Items.Clear();
                foreach (ToolPathCandidate candidate in ToolPathProbe.Find(tool, root)) list.Items.Add(candidate);
                status.Text = list.Items.Count == 0 ? "未找到完整工具目录。请选工具所在文件夹，确认 EXE 和配置文件齐全。" : "找到 " + list.Items.Count + " 个候选；请选择要使用的目录。";
                apply.IsEnabled = false;
                if (list.Items.Count > 0) list.SelectedIndex = 0;
            };
            list.SelectionChanged += delegate { apply.IsEnabled = list.SelectedItem is ToolPathCandidate; };
            scan.Click += delegate { search(_context.Paths.InstallRoot); };
            browse.Click += delegate
            {
                using (System.Windows.Forms.FolderBrowserDialog picker = new System.Windows.Forms.FolderBrowserDialog())
                {
                    picker.Description = "选择脚本文件夹或包含脚本的上级文件夹";
                    picker.SelectedPath = _context.Paths.InstallRoot;
                    if (picker.ShowDialog() == System.Windows.Forms.DialogResult.OK) search(picker.SelectedPath);
                }
            };
            apply.Click += delegate
            {
                ToolPathCandidate candidate = list.SelectedItem as ToolPathCandidate;
                if (candidate == null) return;
                bool running = _context.Supervisor.IsRunning(tool.Id);
                foreach (ToolEntryPoint entry in tool.Entries)
                    if (ProcessSupervisor.FindRunningByExecutable(Path.Combine(_context.ToolDirectory(tool), entry.ExeRelativePath)).Count > 0) running = true;
                if (running) { Alert("请先退出该工具，再更改启动路径。", "启动路径"); return; }
                if (HasDirtyDraft(tool.Id)) { Alert("设置页有未保存的修改，请先保存后再更改路径，避免把草稿写入其他副本。", "启动路径"); return; }
                if (!Confirm("将此工具的启动、配置、备份目标切换到：\n" + candidate.Directory + "\n路径会保存到 launcher/tool-paths.json。不会复制或移动工具文件。", "使用启动路径", false)) return;
                try
                {
                    ToolPathProbe.Save(_context.Paths, tool, candidate, _context.Backups);
                    _drafts.Remove(tool.Id);
                    _lastMessage = "已更新 " + tool.Name + " 的启动路径";
                    _context.Log.Info("更新工具启动路径：" + tool.Id);
                    dialog.Close();
                    RebuildTabs();
                }
                catch (Exception) { Alert("路径保存失败，请检查必需文件及路径配置文件的格式和权限。", "启动路径"); }
            };
            actions.Children.Add(scan);
            actions.Children.Add(browse);
            actions.Children.Add(apply);
            panel.Children.Add(actions);
            dialog.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            search(_context.Paths.InstallRoot);
            dialog.ShowDialog();
        }

        private UIElement BuildSettingsTab()
        {
            StackPanel panel = new StackPanel();
            if (_current.Id == "xuexitong") BuildXuexitongAccount(panel);
            else if (_current.Id == "domooc") BuildDomoocAccount(panel);
            else if (_current.ConfigKind == ToolConfigKind.Ini) BuildAutovisorSettings(panel);
            else if (_current.ConfigKind == ToolConfigKind.Json) BuildUnipusSettings(panel);
            else
            {
                panel.Children.Add(Theme.Body("请使用该工具自身的账号或登录界面。"));
                Button native = Theme.Button("打开工具登录界面", true, false);
                native.Click += delegate { OnLaunch(); };
                panel.Children.Add(native);
                panel.Children.Add(Theme.Muted13("请在其自身界面里设置；启动器不会替它生成或改动配置。"));
            }
            return panel;
        }

        private void BuildDomoocAccount(StackPanel panel)
        {
            panel.Children.Add(Theme.Label("中国大学 MOOC 账号登录", Theme.FontSection, Theme.Ink));
            panel.Children.Add(Theme.Body("domooc 使用网页账号登录。点击下方按钮，在随包 Firefox 打开的中国大学 MOOC 页面选择“登录 / 注册”，填写账号密码或扫码登录。"));
            panel.Children.Add(Theme.Muted13("登录态保存在 domooc 的独立 Profile 中。更换账号请先在网页退出，再登录新账号；启动器不保存额外密码，也不会删除浏览器登录数据。"));
            Button login = Theme.Button("打开 MOOC 登录页面", true, false);
            login.Margin = new Thickness(0, 12, 0, 0);
            login.HorizontalAlignment = HorizontalAlignment.Left;
            login.Click += delegate
            {
                ToolMode mode = new ToolMode { Id = "login", Name = "网页登录", EntryPointId = "login" };
                PreflightReport report = _context.Runner.Preflight(_current, mode);
                if (report.Blocked)
                {
                    Alert("请检查 domooc 文件是否齐全；若浏览器已运行，请在原浏览器中打开 https://www.icourse163.org/ 登录。", "账号登录");
                    return;
                }
                LaunchResult result = _context.Runner.Launch(_current, mode);
                _lastMessage = result.Message;
                if (result.Outcome != LaunchOutcome.Started)
                    Alert(result.Message + "\n如果浏览器已打开，请在原窗口访问 https://www.icourse163.org/ 登录或切换账号。", "账号登录");
                RebuildTabs();
            };
            panel.Children.Add(login);
        }

        private void BuildXuexitongAccount(StackPanel panel)
        {
            panel.Children.Add(Theme.Label("学习通账号配置", Theme.FontSection, Theme.Ink));
            panel.Children.Add(Theme.Muted13("保存后再启动学习通工具。更换账号时请填写新密码，并在工具中重新选择课程。"));
            XuexitongAccountSettings settings;
            try { settings = XuexitongAccountSettings.Load(_context.ConfigPath(_current)); }
            catch (Exception)
            {
                panel.Children.Add(Theme.Label("无法读取学习通账号配置，请检查 task/tool/account_info.json 是否存在且格式正确。", Theme.FontSmall, Theme.AccentRed));
                return;
            }
            FormDraft draft = GetDraft(_current.Id);
            TextBox username = Theme.TextField(string.Empty, false);
            username.Name = "XuexitongUsername";
            BindText(username, draft, "accountUsername", settings.Username);
            PasswordBox password = Theme.PasswordField(draft.AccountSecret);
            password.Name = "XuexitongPassword";
            password.PasswordChanged += delegate { draft.AccountSecret = password.Password; MarkDirty(draft); };
            CheckBox clear = Theme.Check("清除已保存的账号密码", false);
            BindCheck(clear, draft, "clearAccountPassword", false);
            panel.Children.Add(Theme.Field("学习通账号 / 手机号", username));
            panel.Children.Add(Theme.Field(settings.PasswordConfigured ? "密码（已保存；留空即保留）" : "密码（未设置）", password));
            panel.Children.Add(clear);
            Button save = Theme.Button("保存账号", true, false);
            save.Margin = new Thickness(0, 12, 0, 0);
            save.HorizontalAlignment = HorizontalAlignment.Left;
            save.Click += delegate
            {
                bool running = _context.Supervisor.IsRunning("xuexitong");
                foreach (ToolEntryPoint entry in _current.Entries)
                    if (ProcessSupervisor.FindRunningByExecutable(Path.Combine(_context.ToolDirectory(_current), entry.ExeRelativePath)).Count > 0) running = true;
                if (running) { Alert("请先退出学习通工具，再保存账号，避免配置被覆盖。", "账号配置"); return; }
                string name = username.Text.Trim();
                SecretChange change = clear.IsChecked == true ? SecretChange.Clear
                    : (password.Password.Length > 0 ? SecretChange.Replace : SecretChange.Keep);
                if (name.Length == 0 || (name != settings.Username && (change != SecretChange.Replace || password.Password.Length == 0)))
                { Alert("请填写账号；更换账号时需要同时填写新密码。", "账号配置"); return; }
                if (!Confirm("保存学习通账号配置？会先备份。\n更换账号将清除旧课程选择，API 和其他设置保持不变。\n账号密码保存在本地 account_info.json 中。", "保存账号", false)) return;
                try
                {
                    settings.Save(_context.Backups, name, change, password.Password);
                    _drafts.Remove(_current.Id);
                    _lastMessage = "学习通账号已保存；请重新启动工具";
                    _context.Log.Info("保存学习通账号配置（已备份）");
                    RebuildTabs();
                }
                catch (Exception) { Alert("保存失败，请检查配置文件格式或是否被占用。", "账号配置"); }
            };
            panel.Children.Add(save);
        }

        private void BuildUnipusAccount(StackPanel panel, UnipusSettings settings, FormDraft draft)
        {
            panel.Children.Add(Theme.Label("U 校园账号配置", Theme.FontSection, Theme.Ink));
            panel.Children.Add(Theme.Muted13("保存后重新启动 UnipusAI 生效。更换账号需要填写新密码，并重新选择课程。"));
            TextBox username = Theme.TextField(string.Empty, false);
            username.Name = "UnipusUsername";
            BindText(username, draft, "accountUsername", settings.Username);
            PasswordBox password = Theme.PasswordField(draft.AccountSecret);
            password.Name = "UnipusPassword";
            password.PasswordChanged += delegate { draft.AccountSecret = password.Password; MarkDirty(draft); };
            CheckBox clear = Theme.Check("清除已保存的账号密码", false);
            BindCheck(clear, draft, "clearAccountPassword", false);
            panel.Children.Add(Theme.Field("U 校园账号", username));
            panel.Children.Add(Theme.Field(settings.PasswordConfigured ? "账号密码（已保存；留空即保留）" : "账号密码（未设置）", password));
            panel.Children.Add(clear);
            Button save = Theme.Button("保存账号", true, false);
            save.Margin = new Thickness(0, 10, 0, 14);
            save.HorizontalAlignment = HorizontalAlignment.Left;
            save.Click += delegate
            {
                bool running = _context.Supervisor.IsRunning("unipus");
                foreach (Process process in Process.GetProcessesByName("UnipusAI"))
                {
                    running = true;
                    process.Dispose();
                }
                if (running)
                {
                    Alert("请先正常停止 UnipusAI，再保存账号，避免运行中的工具覆盖配置。", "账号配置");
                    return;
                }
                SecretChange change = clear.IsChecked == true ? SecretChange.Clear
                    : (password.Password.Length > 0 ? SecretChange.Replace : SecretChange.Keep);
                string name = username.Text.Trim();
                if (name.Length == 0 || (name != settings.Username && (change != SecretChange.Replace || password.Password.Length == 0)))
                {
                    Alert("请填写账号；更换账号时需要同时填写新密码。", "账号配置");
                    return;
                }
                if (!Confirm("保存 U 校园账号配置？会先备份。\n更改账号或密码将清除旧登录信息；更换账号还会清除旧课程选择，下次启动重新登录。\n账号密码保存在工具本地 config.json 中。AI 设置保持不变。", "保存账号", false)) return;
                try
                {
                    settings.SaveAccount(_context.Backups, name, change, password.Password);
                    draft.Values.Remove("accountUsername");
                    draft.Values.Remove("clearAccountPassword");
                    draft.AccountSecret = string.Empty;
                    draft.Dirty = draft.Values.Count > 0 || draft.Secret.Length > 0;
                    _lastMessage = "账号已保存；请重新启动 UnipusAI";
                    _context.Log.Info("保存 unipus 账号配置（已备份）");
                    RebuildTabs();
                }
                catch (Exception)
                {
                    Alert("账号保存失败，请检查配置文件格式或是否被其他程序占用。", "账号配置");
                }
            };
            panel.Children.Add(save);
            panel.Children.Add(Theme.Label("AI 与学习设置", Theme.FontSection, Theme.Ink));
        }

                private void BuildAutovisorSettings(StackPanel panel)
        {
            panel.Children.Add(Theme.Label("Autovisor 账号配置", Theme.FontSection, Theme.Ink));
            string configPath = _context.ConfigPath(_current);
            AutovisorSettings settings;
            try
            {
                settings = AutovisorSettings.Load(configPath);
            }
            catch (Exception error)
            {
                panel.Children.Add(Theme.Label("读取 config.ini 失败：" + error.Message, Theme.FontSmall, Theme.AccentRed));
                return;
            }

            FormDraft draft = GetDraft(_current.Id);

            TextBox username = Theme.TextField(string.Empty, false);
            BindText(username, draft, "username", settings.Username);
            PasswordBox password = Theme.PasswordField(string.Empty);
            password.ToolTip = settings.PasswordConfigured ? "已保存，留空表示保留" : "未设置";
            BindSecret(password, draft);
            CheckBox clearPassword = Theme.Check("清除已保存的密码", false);
            BindCheck(clearPassword, draft, "clearPassword", false);
            ComboBox driver = Theme.Combo(new string[] { "Edge", "Chrome" }, null);
            BindCombo(driver, draft, "driver", settings.Driver);
            TextBox exePath = Theme.TextField(string.Empty, false);
            BindText(exePath, draft, "exePath", settings.ExePath);
            CheckBox autoCaptcha = Theme.Check("自动过登录验证", false);
            BindCheck(autoCaptcha, draft, "autoCaptcha", settings.EnableAutoCaptcha);
            CheckBox hideWindow = Theme.Check("自动隐藏浏览器窗口", false);
            BindCheck(hideWindow, draft, "hideWindow", settings.EnableHideWindow);
            CheckBox donate = Theme.Check("显示赞赏码", false);
            BindCheck(donate, draft, "donate", settings.ShowDonateCode);
            CheckBox soundOff = Theme.Check("静音播放", false);
            BindCheck(soundOff, draft, "soundOff", settings.SoundOff);
            TextBox maxTime = Theme.TextField(string.Empty, false);
            BindText(maxTime, draft, "maxTime", settings.LimitMaxTime.ToString());
            TextBox speed = Theme.TextField(string.Empty, false);
            BindText(speed, draft, "speed", settings.LimitSpeed.ToString("0.0##"));
            TextBox urls = Theme.TextField(string.Empty, true);
            urls.MinHeight = 70;
            BindText(urls, draft, "urls", string.Join(Environment.NewLine, settings.CourseUrls.ToArray()));

            panel.Children.Add(Theme.Field("账号", username));
            panel.Children.Add(Theme.Field(settings.PasswordConfigured ? "密码（已保存；留空即保留）" : "密码（未设置）", password));
            panel.Children.Add(clearPassword);
            panel.Children.Add(Theme.Field("浏览器驱动", driver));
            panel.Children.Add(Theme.Field("浏览器路径（留空用默认）", exePath));
            panel.Children.Add(autoCaptcha);
            panel.Children.Add(hideWindow);
            panel.Children.Add(donate);
            panel.Children.Add(soundOff);

            StackPanel numbers = new StackPanel();
            numbers.Orientation = Orientation.Horizontal;
            maxTime.Width = 90;
            speed.Width = 90;
            StackPanel maxTimeBox = Theme.Field("每门课限时(分钟)", maxTime);
            maxTimeBox.Margin = new Thickness(0, 0, 18, 0);
            numbers.Children.Add(maxTimeBox);
            numbers.Children.Add(Theme.Field("播放倍速(≤1.8)", speed));
            panel.Children.Add(numbers);

            panel.Children.Add(Theme.Field("课程链接（每行一个；删掉某行就会从 config.ini 里移除对应 URL）", urls));

            foreach (string warning in settings.Warnings)
                panel.Children.Add(Theme.Label("· " + warning, Theme.FontSmall, Theme.AccentYellow));

            Button save = Theme.Button("保存设置", true, false);
            save.HorizontalAlignment = HorizontalAlignment.Left;
            save.Margin = new Thickness(0, 10, 0, 0);
            save.Click += delegate
            {
                SecretChange change = SecretChange.Keep;
                string newPassword = password.Password;
                if (clearPassword.IsChecked == true) change = SecretChange.Clear;
                else if (!string.IsNullOrEmpty(newPassword)) change = SecretChange.Replace;

                int parsedMax;
                if (!NumberInput.TryParseInt32(maxTime.Text, out parsedMax) || parsedMax < 0 || parsedMax > 100000)
                {
                    Alert("每门课限时必须是非负整数。", "输入有误");
                    return;
                }
                double parsedSpeed;
                if (!NumberInput.TryParseFinite(speed.Text, out parsedSpeed) || parsedSpeed <= 0)
                {
                    Alert("播放倍速必须是大于 0 的有限数字（不接受 NaN / Infinity）。", "输入有误");
                    return;
                }
                if (parsedSpeed > 1.8)
                {
                    if (!Confirm("工具文档写明倍速上限为 1.8。仍然保存 " + parsedSpeed + " 吗？", "倍速超出建议范围", true))
                        return;
                }

                List<string> lines = new List<string>();
                foreach (string line in urls.Text.Replace("\r\n", "\n").Split('\n'))
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length > 0) lines.Add(trimmed);
                }

                settings.Username = username.Text.Trim();
                settings.Driver = string.IsNullOrEmpty(driver.Text) ? "Edge" : driver.Text.Trim();
                settings.ExePath = exePath.Text.Trim();
                settings.EnableAutoCaptcha = autoCaptcha.IsChecked == true;
                settings.EnableHideWindow = hideWindow.IsChecked == true;
                settings.ShowDonateCode = donate.IsChecked == true;
                settings.SoundOff = soundOff.IsChecked == true;
                settings.LimitMaxTime = parsedMax;
                settings.LimitSpeed = parsedSpeed;
                settings.CourseUrls = lines;

                if (!Confirm(
                    "将修改 config.ini（会先备份）:\n" +
                    "  账号: " + (settings.Username.Length == 0 ? "(留空)" : settings.Username) + "\n" +
                    "  密码: " + (change == SecretChange.Clear ? "清除" : (change == SecretChange.Replace ? "替换为新值" : "保持原值")) + "\n" +
                    "  驱动: " + settings.Driver + " · 限时: " + parsedMax + " 分钟 · 倍速: " + parsedSpeed + "\n" +
                    "  课程链接: " + lines.Count + " 条（会同时清除未选中的旧 URL）\n\n继续保存？",
                    "确认保存", false))
                    return; // 取消：表单与草稿都保留

                try
                {
                    string backup = settings.Save(configPath, _context.Backups, change, newPassword);
                    _context.Backups.Prune("autovisor", 30);
                    _drafts.Remove(_current.Id); // 保存成功才丢弃草稿
                    _lastMessage = "已保存 config.ini" + (string.IsNullOrEmpty(backup) ? "" : "（备份 " + Path.GetFileName(backup) + "）");
                    _context.Log.Info("保存 autovisor 配置，备份 " + backup);
                    RebuildTabs();
                }
                catch (Exception error)
                {
                    _context.Log.Error("保存 autovisor 配置失败: " + error.Message);
                    Alert("保存失败：" + error.Message, "保存失败");
                }
            };
            panel.Children.Add(save);
        }
                private void BuildUnipusSettings(StackPanel panel)
        {
            string configPath = _context.ConfigPath(_current);
            UnipusSettings settings;
            try
            {
                settings = UnipusSettings.Load(configPath);
            }
            catch (Exception error)
            {
                panel.Children.Add(Theme.Label("读取 config.json 失败：" + error.Message, Theme.FontSmall, Theme.AccentRed));
                return;
            }

            FormDraft draft = GetDraft(_current.Id);

            BuildUnipusAccount(panel, settings, draft);

            TextBox baseUrl = Theme.TextField(string.Empty, false);
            BindText(baseUrl, draft, "baseUrl", settings.BaseUrl);
            TextBox model = Theme.TextField(string.Empty, false);
            BindText(model, draft, "model", settings.Model);
            ComboBox strategy = Theme.Combo(UnipusSettings.LearningStrategies, null);
            BindCombo(strategy, draft, "strategy", settings.LearningStrategy);
            TextBox maxTokens = Theme.TextField(string.Empty, false);
            BindText(maxTokens, draft, "maxTokens", settings.MaxTokens.ToString());
            TextBox temperature = Theme.TextField(string.Empty, false);
            BindText(temperature, draft, "temperature",
                settings.Temperature.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            TextBox interval = Theme.TextField(string.Empty, false);
            BindText(interval, draft, "interval", settings.IntervalMs.ToString());
            CheckBox whisper = Theme.Check("启用 whisper 语音识别", false);
            BindCheck(whisper, draft, "whisper", settings.WhisperEnabled);
            TextBox whisperModel = Theme.TextField(string.Empty, false);
            BindText(whisperModel, draft, "whisperModel", settings.WhisperModel);
            TextBox whisperLanguage = Theme.TextField(string.Empty, false);
            BindText(whisperLanguage, draft, "whisperLanguage", settings.WhisperLanguage);
            PasswordBox apiKey = Theme.PasswordField(string.Empty);
            apiKey.ToolTip = settings.ApiKeyConfigured ? "已保存，留空表示保留" : "未设置";
            BindSecret(apiKey, draft);
            CheckBox clearKey = Theme.Check("清除已保存的 API Key", false);
            BindCheck(clearKey, draft, "clearKey", false);

            panel.Children.Add(Theme.Field("base_url", baseUrl));
            panel.Children.Add(Theme.Field("model", model));
            panel.Children.Add(Theme.Field("learning_strategy", strategy));

            StackPanel numbers = new StackPanel();
            numbers.Orientation = Orientation.Horizontal;
            maxTokens.Width = 90;
            temperature.Width = 90;
            interval.Width = 90;
            StackPanel a = Theme.Field("max_tokens", maxTokens); a.Margin = new Thickness(0, 0, 14, 0);
            StackPanel b = Theme.Field("temperature", temperature); b.Margin = new Thickness(0, 0, 14, 0);
            numbers.Children.Add(a);
            numbers.Children.Add(b);
            numbers.Children.Add(Theme.Field("interval_ms", interval));
            panel.Children.Add(numbers);

            panel.Children.Add(whisper);
            panel.Children.Add(Theme.Field("whisper_model", whisperModel));
            panel.Children.Add(Theme.Field("whisper_language", whisperLanguage));
            panel.Children.Add(Theme.Field(settings.ApiKeyConfigured ? "api_key（已保存；留空即保留）" : "api_key（未设置）", apiKey));
            panel.Children.Add(clearKey);

            foreach (string warning in settings.Warnings)
                panel.Children.Add(Theme.Label("· " + warning, Theme.FontSmall, Theme.AccentYellow));

            Button save = Theme.Button("保存设置", true, false);
            save.HorizontalAlignment = HorizontalAlignment.Left;
            save.Margin = new Thickness(0, 10, 0, 0);
            save.Click += delegate
            {
                int parsedMaxTokens;
                if (!NumberInput.TryParseInt32(maxTokens.Text, out parsedMaxTokens) || parsedMaxTokens < 1)
                {
                    Alert("max_tokens 必须是正整数。", "输入有误");
                    return;
                }
                double parsedTemperature;
                if (!NumberInput.TryParseFiniteRange(temperature.Text, 0, 2, out parsedTemperature))
                {
                    Alert("temperature 必须是 0..2 之间的有限数字（不接受 NaN / Infinity）。", "输入有误");
                    return;
                }
                int parsedInterval;
                if (!NumberInput.TryParseInt32(interval.Text, out parsedInterval) || parsedInterval < 0)
                {
                    Alert("interval_ms 必须是非负整数。", "输入有误");
                    return;
                }

                SecretChange change = SecretChange.Keep;
                if (clearKey.IsChecked == true) change = SecretChange.Clear;
                else if (!string.IsNullOrEmpty(apiKey.Password)) change = SecretChange.Replace;

                settings.BaseUrl = baseUrl.Text.Trim();
                settings.Model = model.Text.Trim();
                settings.LearningStrategy = strategy.Text.Trim();
                settings.MaxTokens = parsedMaxTokens;
                settings.Temperature = parsedTemperature;
                settings.IntervalMs = parsedInterval;
                settings.WhisperEnabled = whisper.IsChecked == true;
                settings.WhisperModel = whisperModel.Text.Trim();
                settings.WhisperLanguage = whisperLanguage.Text.Trim();

                if (!Confirm(
                    "将修改 config.json（会先备份，其余字段原样保留）:\n" +
                    "  base_url: " + settings.BaseUrl + "\n  model: " + settings.Model + "\n" +
                    "  max_tokens: " + parsedMaxTokens + " · temperature: " + parsedTemperature + " · interval_ms: " + parsedInterval + "\n" +
                    "  API Key: " + (change == SecretChange.Clear ? "清除" : (change == SecretChange.Replace ? "替换为新值" : "保持原值")) + "\n\n继续保存？",
                    "确认保存", false))
                    return; // 取消：表单与草稿都保留

                try
                {
                    string backup = settings.Save(_context.Backups, change, apiKey.Password);
                    _context.Backups.Prune("unipus", 30);
                    foreach (string key in new List<string>(draft.Values.Keys))
                        if (key != "accountUsername" && key != "clearAccountPassword") draft.Values.Remove(key);
                    draft.Secret = string.Empty;
                    draft.Dirty = draft.Values.Count > 0 || draft.AccountSecret.Length > 0;
                    _lastMessage = "已保存 config.json" + (string.IsNullOrEmpty(backup) ? "" : "（备份 " + Path.GetFileName(backup) + "）");
                    _context.Log.Info("保存 unipus 配置，备份 " + backup);
                    RebuildTabs();
                }
                catch (Exception error)
                {
                    _context.Log.Error("保存 unipus 配置失败: " + error.Message);
                    Alert("保存失败：" + error.Message, "保存失败");
                }
            };
            panel.Children.Add(save);
        }
        /* ---------------- 备份 tab ---------------- */

        private UIElement BuildBackupTab()
        {
            StackPanel panel = new StackPanel();
            if (_current.ConfigKind == ToolConfigKind.None)
            {
                panel.Children.Add(Theme.Body("该工具没有配置文件，因此没有可备份的内容。"));
                return panel;
            }

            IList<BackupEntry> entries = _context.Backups.List(_current.Id);
            panel.Children.Add(Theme.Body("共 " + entries.Count + " 份备份（最近的在前）"));

            ListBox list = new ListBox();
            list.Height = Math.Min(240, 30 + entries.Count * 20);
            list.Margin = new Thickness(0, 8, 0, 0);
            list.Background = Brushes.White;
            list.BorderBrush = Theme.Line;
            list.BorderThickness = new Thickness(1);
            list.FontFamily = Theme.UiFont;
            list.FontSize = Theme.FontSmall;
            foreach (BackupEntry entry in entries) list.Items.Add(entry.DisplayName);
            if (entries.Count > 0) list.SelectedIndex = 0;
            panel.Children.Add(list);

            StackPanel row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            row.Margin = new Thickness(0, 10, 0, 0);

            Button backupNow = Theme.Button("立即备份", false, false);
            backupNow.Click += delegate
            {
                try
                {
                    BackupEntry entry = _context.Backups.Create(_current.Id, _context.ConfigPath(_current));
                    _lastMessage = entry == null ? "没有可备份的文件" : "已备份 " + Path.GetFileName(entry.FilePath);
                    _context.Log.Info("手动备份 " + _current.Id + "：" + (entry == null ? "(无文件)" : entry.FilePath));
                    RebuildTabs();
                }
                catch (Exception error)
                {
                    Alert("备份失败：" + error.Message, "备份失败");
                }
            };
            row.Children.Add(backupNow);

            Button restore = Theme.Button("恢复到所选备份", false, true);
            restore.IsEnabled = entries.Count > 0;
            restore.Click += delegate
            {
                int index = list.SelectedIndex;
                if (index < 0 || index >= entries.Count) return;
                BackupEntry entry = entries[index];
                if (!Confirm("将用下面这份备份覆盖当前配置：\n" + entry.DisplayName + "\n" + entry.FilePath +
                             "\n\n当前文件会先自动备份。继续？", "恢复备份", true))
                    return;
                try
                {
                    string safety = _context.Backups.Restore(_current.Id, entry.FilePath, _context.ConfigPath(_current));
                    _lastMessage = "已恢复备份" + (string.IsNullOrEmpty(safety) ? "" : "（原文件备份 " + Path.GetFileName(safety) + "）");
                    _context.Log.Info("恢复 " + _current.Id + " 备份 " + entry.FilePath + "，原文件备份 " + safety);
                    RebuildTabs();
                }
                catch (Exception error)
                {
                    _context.Log.Error("恢复备份失败: " + error.Message);
                    Alert("恢复失败：" + error.Message, "恢复失败");
                }
            };
            row.Children.Add(restore);
            panel.Children.Add(row);

            TextBlock hint = Theme.Muted13("备份目录：" + _context.Backups.RootDirectory + "（每个工具保留最近 30 份）");
            hint.Margin = new Thickness(0, 10, 0, 0);
            panel.Children.Add(hint);
            return panel;
        }

        /* ---------------- 诊断 tab ---------------- */

        private UIElement BuildDiagnosticsTab()
        {
            StackPanel panel = new StackPanel();

            PreflightReport report = _context.Runner.Preflight(_current, _currentMode);
            TextBlock header = Theme.Label("该工具的预检", Theme.FontSection, Theme.Ink);
            header.FontWeight = FontWeights.SemiBold;
            header.Margin = new Thickness(0, 0, 0, 6);
            panel.Children.Add(header);
            foreach (PreflightItem item in report.Items) panel.Children.Add(BuildRow(item.Ok, item.Warning, item.Name, item.Detail));

            TextBlock envHeader = Theme.Label("运行环境", Theme.FontSection, Theme.Ink);
            envHeader.FontWeight = FontWeights.SemiBold;
            envHeader.Margin = new Thickness(0, 16, 0, 6);
            panel.Children.Add(envHeader);
            foreach (ProbeItem item in _context.Probe.Run(true)) panel.Children.Add(BuildRow(item.Ok, item.IsWarning, item.Name, item.Detail));

            StackPanel row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            row.Margin = new Thickness(0, 14, 0, 0);
            Button openLogs = Theme.Button("打开日志目录", false, false);
            openLogs.Click += delegate { OpenPath(_context.Paths.LogsDirectory); };
            Button viewLog = Theme.Button("查看日志", false, false);
            viewLog.Click += delegate { OnViewLog(); };
            row.Children.Add(openLogs);
            row.Children.Add(viewLog);
            panel.Children.Add(row);

            TextBlock logPath = Theme.Muted13("日志：" + _context.Log.CurrentFile);
            logPath.Margin = new Thickness(0, 10, 0, 0);
            panel.Children.Add(logPath);
            return panel;
        }

        private UIElement BuildRow(bool ok, bool warning, string name, string detail)
        {
            Grid grid = new Grid();
            grid.Margin = new Thickness(0, 0, 0, 4);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            string mark = ok ? (warning ? "注意" : "通过") : "失败";
            Brush brush = !ok ? Theme.AccentRed : (warning ? Theme.AccentYellow : Theme.OkInk);
            TextBlock markBlock = Theme.Label(mark, Theme.FontSmall, brush);
            Grid.SetColumn(markBlock, 0);
            grid.Children.Add(markBlock);

            TextBlock nameBlock = Theme.Label(name, Theme.FontBody, Theme.Ink);
            nameBlock.TextWrapping = TextWrapping.Wrap;
            Grid.SetColumn(nameBlock, 1);
            grid.Children.Add(nameBlock);

            TextBlock detailBlock = Theme.Muted13(detail);
            Grid.SetColumn(detailBlock, 2);
            grid.Children.Add(detailBlock);
            return grid;
        }

        /* ---------------- 帮助 tab ---------------- */

        private UIElement BuildHelpTab()
        {
            StackPanel panel = new StackPanel();

            panel.Children.Add(Theme.Label("项目仓库", Theme.FontSection, Theme.Ink));
            TextBlock repository = Theme.Body(string.Empty);
            repository.Margin = new Thickness(0, 4, 0, 14);
            Hyperlink link = new Hyperlink(new Run(RepositoryUrl));
            link.NavigateUri = new Uri(RepositoryUrl);
            link.Foreground = Theme.Ink;
            link.ToolTip = "使用默认浏览器打开项目仓库";
            link.Click += delegate { OpenRepository(); };
            repository.Inlines.Add(link);
            panel.Children.Add(repository);

            panel.Children.Add(Theme.Label("启动入口依据", Theme.FontSection, Theme.Ink));
            foreach (ToolEntryPoint entry in _current.Entries)
            {
                TextBlock line = Theme.Muted13("· " + entry.Name + (entry.Confirmed ? "" : "（待确认）") + "：" + entry.Evidence);
                line.Margin = new Thickness(0, 4, 0, 6);
                panel.Children.Add(line);
            }

            panel.Children.Add(Theme.Label("该工具的说明", Theme.FontSection, Theme.Ink));
            TextBlock hint = Theme.Muted13(string.IsNullOrEmpty(_current.DocsHint) ? "（无）" : _current.DocsHint);
            hint.Margin = new Thickness(0, 4, 0, 8);
            panel.Children.Add(hint);

            foreach (ToolMode mode in _current.Modes)
            {
                if (mode.Notes == null || mode.Notes.Length == 0) continue;
                foreach (string note in mode.Notes)
                    panel.Children.Add(Theme.Muted13("· " + mode.Name + "：" + note));
            }

            panel.Children.Add(Theme.Label("启动器行为", Theme.FontSection, Theme.Ink));
            AddBullet(panel, "只有启动器自己启动的进程会被停止；外部打开的实例只提示，不接管、不结束。");
            AddBullet(panel, "“正常停止”先请求关闭窗口并等待；控制台程序无法优雅关闭时会提示，是否强制结束由你决定。");
            AddBullet(panel, "强制结束只作用于那一个进程，不会结束它启动的子进程（例如浏览器）。");
            AddBullet(panel, "配置文件只在点“保存设置”时写入，写入前自动备份；未知字段与注释保持不变。");
            AddBullet(panel, "启动器不代替任何工具刷课、答题或提交作业，也不推断或伪造课程进度。");
            AddBullet(panel, "不需要管理员权限；不修改 hosts、代理、防火墙或注册表。");
            AddBullet(panel, "运行依赖 Windows 内置的 .NET Framework 4.8（非自包含发行）。");

            CheckBox tray = Theme.Check("关闭窗口时隐藏到托盘（取消勾选则直接退出）", _state.CloseToTray);
            tray.Margin = new Thickness(0, 12, 0, 0);
            tray.Click += delegate
            {
                _state.CloseToTray = tray.IsChecked == true;
                _state.AskedClosePreference = true;
                _state.Save(_statePath);
                _lastMessage = _state.CloseToTray ? "关闭窗口将隐藏到托盘" : "关闭窗口将退出启动器";
                RefreshRuntimeStatus();
            };
            panel.Children.Add(tray);

            return panel;
        }

        private static void AddBullet(StackPanel panel, string text)
        {
            TextBlock line = Theme.Muted13("· " + text);
            line.Margin = new Thickness(0, 3, 0, 0);
            panel.Children.Add(line);
        }

        /* ---------------- 运行状态 ---------------- */

        private void RefreshRuntimeStatus()
        {
            if (_current == null) return;

            bool running = _context.Supervisor.IsRunning(_current.Id);
            OwnedProcess owned = _context.Supervisor.GetOwned(_current.Id);

            if (_processLine != null)
            {
                if (running && owned != null)
                {
                    _processLine.Text = "进程：运行中 · PID " + owned.ProcessId + " · 已运行 " +
                                        FormatDuration(DateTime.UtcNow - owned.StartedAtUtc);
                    _processLine.Foreground = Theme.OkInk;
                }
                else
                {
                    _processLine.Text = "进程：未运行";
                    _processLine.Foreground = Theme.Ink;
                }
            }

            if (_serviceLine != null)
            {
                if (string.Equals(_current.Id, "domooc", StringComparison.OrdinalIgnoreCase))
                {
                    PortState state = _context.Runner.AnswerServiceState();
                    if (state == PortState.OurService) _serviceLine.Text = "本地答题服务：已运行（8791，已验证身份）";
                    else if (state == PortState.Foreign) _serviceLine.Text = "本地答题服务：8791 被其它程序占用（不会结束它）";
                    else _serviceLine.Text = "本地答题服务：未运行（需要时自动启动）";
                    _serviceLine.Foreground = state == PortState.Foreign ? Theme.AccentRed : Theme.Muted;
                }
                else
                {
                    _serviceLine.Text = "";
                }
            }

            if (_stopButton != null) _stopButton.IsEnabled = running;
            if (_forceButton != null) _forceButton.IsEnabled = running;

            _statusLine.Text = _lastMessage + "   |   日志：" + _context.Log.CurrentFile;
        }

        /* ---------------- 未保存草稿 ---------------- */

        private FormDraft GetDraft(string toolId)
        {
            FormDraft draft;
            if (!_drafts.TryGetValue(toolId, out draft))
            {
                draft = new FormDraft();
                _drafts[toolId] = draft;
            }
            return draft;
        }

        private bool HasDirtyDraft(string toolId)
        {
            FormDraft draft;
            return _drafts.TryGetValue(toolId, out draft) && draft.Dirty;
        }

        private void MarkDirty(FormDraft draft)
        {
            bool wasDirty = draft.Dirty;
            draft.Dirty = true;
            if (!wasDirty) RefreshToolListHints();
        }

        private void RefreshToolListHints()
        {
            if (_toolList == null) return;
            for (int i = 0; i < _toolList.Items.Count; i++)
            {
                ListBoxItem row = _toolList.Items[i] as ListBoxItem;
                if (row == null) continue;
                ToolDefinition tool = row.Tag as ToolDefinition;
                if (tool == null) continue;
                TextBlock block;
                if (!_hintBlocks.TryGetValue(tool.Id, out block) || block == null) continue;
                block.Text = StatusHint(tool);
            }
        }

        /// <summary>Value comes from the draft when present, otherwise from the file. Wiring after the
        /// assignment keeps programmatic initialisation from marking the form dirty.</summary>
        private void BindText(TextBox box, FormDraft draft, string key, string baseline)
        {
            box.Text = draft.Values.ContainsKey(key) ? draft.Values[key] : (baseline ?? string.Empty);
            box.TextChanged += delegate
            {
                draft.Values[key] = box.Text;
                MarkDirty(draft);
            };
        }

        private void BindCombo(ComboBox combo, FormDraft draft, string key, string baseline)
        {
            string initial = draft.Values.ContainsKey(key) ? draft.Values[key] : (baseline ?? string.Empty);
            combo.Text = initial;
            // editable ComboBox 没有 TextChanged 事件；它的内部 TextBox 会冒泡 TextBoxBase.TextChanged。
            // 注意：模板应用时会把初始文本回填进内部 TextBox，那也会冒泡一次 TextChanged ——
            // 值与初始值相同就必须忽略，否则一进设置页就被标成“有未保存修改”。
            combo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                new TextChangedEventHandler(delegate
                {
                    string current = combo.Text ?? string.Empty;
                    if (string.Equals(current, initial, StringComparison.Ordinal))
                    {
                        // 这个分支有两种来源，必须分开处理：
                        //   1) 模板把 combo.Text 回填进内部 TextBox，冒泡一次同值 TextChanged
                        //      → 草稿里没有该键，或草稿值就等于 current：纯回填，忽略（防伪 dirty）
                        //   2) 用户手动把值改回初始值（例如 Chrome 改回 Edge）
                        //      → 草稿里已有不同的值：必须写回 current，否则界面显示 Edge
                        //        而下次重建会恢复旧的 Chrome（数据丢失）
                        string existing;
                        if (!draft.Values.TryGetValue(key, out existing)) return;
                        if (string.Equals(existing, current, StringComparison.Ordinal)) return;
                        draft.Values[key] = current;
                        MarkDirty(draft);
                        return;
                    }
                    draft.Values[key] = current;
                    MarkDirty(draft);
                }));
        }

        private void BindCheck(CheckBox box, FormDraft draft, string key, bool baseline)
        {
            bool value = baseline;
            if (draft.Values.ContainsKey(key)) value = draft.Values[key] == "1";
            box.IsChecked = value;
            box.Click += delegate
            {
                draft.Values[key] = box.IsChecked == true ? "1" : "0";
                MarkDirty(draft);
            };
        }

        private void BindSecret(PasswordBox box, FormDraft draft)
        {
            if (!string.IsNullOrEmpty(draft.Secret)) box.Password = draft.Secret;
            box.PasswordChanged += delegate
            {
                draft.Secret = box.Password;
                MarkDirty(draft);
            };
        }

        private bool Confirm(string text, string caption, bool warning)
        {
            if (ConfirmHook != null) return ConfirmHook(text, caption);
            MessageBoxResult result = MessageBox.Show(this, text, caption, MessageBoxButton.OKCancel,
                warning ? MessageBoxImage.Warning : MessageBoxImage.Question);
            return result == MessageBoxResult.OK;
        }

        private void Alert(string text, string caption)
        {
            if (AlertHook != null)
            {
                AlertHook(text, caption);
                return;
            }
            MessageBox.Show(this, text, caption, MessageBoxButton.OK,
                caption == "启动失败" || caption == "保存失败" || caption == "恢复失败"
                    ? MessageBoxImage.Error
                    : MessageBoxImage.Warning);
        }

        private static string FormatDuration(TimeSpan span)
        {
            if (span.TotalHours >= 1) return ((int)span.TotalHours) + " 小时 " + span.Minutes + " 分";
            if (span.TotalMinutes >= 1) return ((int)span.TotalMinutes) + " 分 " + span.Seconds + " 秒";
            return ((int)span.TotalSeconds) + " 秒";
        }

        /* ---------------- 动作 ---------------- */

        private void OnLaunch()
        {
            if (_current == null || _currentMode == null) return;

            PreflightReport report = _context.Runner.Preflight(_current, _currentMode);
            if (report.Blocked)
            {
                Alert("预检有失败项，先处理后再启动。详情见「诊断」页。", "无法启动");
                return;
            }

            bool unconfirmed = false;
            for (int i = 0; i < _current.Entries.Count; i++)
            {
                if (!_current.Entries[i].Confirmed &&
                    string.Equals(_current.Entries[i].Id, _currentMode.EntryPointId, StringComparison.OrdinalIgnoreCase))
                    unconfirmed = true;
            }
            if (unconfirmed)
            {
                if (!Confirm("该入口尚未经验证（只有说明文档的间接依据）。\n即将运行：" +
                             _current.Name + " → " + _currentMode.Name + "\n\n继续？", "入口待确认", true))
                    return;
            }

            LaunchResult result = _context.Runner.Launch(_current, _currentMode);
            _lastMessage = result.Message;
            if (result.Outcome == LaunchOutcome.Failed)
                Alert(result.Message, "启动失败");
            else if (result.Outcome == LaunchOutcome.AlreadyRunning)
                Alert(result.Message, "已在运行");
            RebuildTabs();
        }

        private void OnStop(bool force)
        {
            if (_current == null) return;
            OwnedProcess owned = _context.Supervisor.GetOwned(_current.Id);
            if (owned == null)
            {
                Alert("启动器没有该工具的进程记录，不会结束任何进程。", "不会结束外部实例");
                return;
            }

            string action = force ? "强制结束" : "正常停止";
            if (!Confirm(action + " PID " + owned.ProcessId + "（" + _current.Name + "）？\n" +
                         (force
                             ? "只会结束这一个进程，不会结束它的子进程。"
                             : "会先请求关闭窗口并等待 5 秒。"), action, force))
                return;

            StopResult result = _context.Runner.StopTool(_current, force);
            _lastMessage = result.Message;
            if (result.Outcome == StopOutcome.StillRunning)
            {
                if (Confirm(result.Message + "\n\n现在强制结束它吗？", "仍在运行", true))
                {
                    StopResult forced = _context.Runner.StopTool(_current, true);
                    _lastMessage = forced.Message;
                }
            }
            RebuildTabs();
        }

        private void OnOpenConfig()
        {
            if (_current == null) return;
            string path = _context.ConfigPath(_current);
            if (!File.Exists(path))
            {
                Alert("配置文件不存在：" + path, "找不到配置");
                return;
            }
            OpenWith("notepad.exe", path, "打开配置");
        }

        private void OnViewLog()
        {
            Window window = new Window();
            window.Title = "启动器日志";
            window.Width = 900;
            window.Height = 560;
            window.Background = Theme.Background;
            window.FontFamily = Theme.UiFont;
            window.FontSize = Theme.FontSmall;
            window.Owner = this;

            DockPanel panel = new DockPanel();
            TextBox text = new TextBox();
            text.IsReadOnly = true;
            text.FontFamily = new FontFamily("Consolas, Courier New, monospace");
            text.FontSize = Theme.FontSmall;
            text.Background = Brushes.White;
            text.BorderThickness = new Thickness(0);
            text.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            text.Text = _context.Log.Tail(500);
            panel.Children.Add(text);

            StackPanel row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            row.Margin = new Thickness(10);
            Button open = Theme.Button("打开日志目录", false, false);
            open.Click += delegate { OpenPath(_context.Paths.LogsDirectory); };
            Button refresh = Theme.Button("刷新", false, false);
            refresh.Click += delegate { text.Text = _context.Log.Tail(500); };
            row.Children.Add(open);
            row.Children.Add(refresh);
            DockPanel.SetDock(row, Dock.Bottom);
            panel.Children.Add(row);
            window.Content = panel;
            window.ShowDialog();
        }

        private void OpenPath(string path)
        {
            OpenWith("explorer.exe", path, "打开目录");
        }

        private void OpenRepository()
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = RepositoryUrl, UseShellExecute = true });
            }
            catch (Exception error) { Alert("打开项目仓库失败：" + error.Message, "项目仓库"); }
        }

        /// <summary>Fixed executable + one quoted path. Never a shell string built from input.</summary>
        private void OpenWith(string exePath, string argument, string title)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = exePath;
                info.Arguments = ArgumentList.Join(new string[] { argument });
                info.UseShellExecute = false;
                Process.Start(info);
            }
            catch (Exception error)
            {
                Alert(title + "失败：" + error.Message, title);
            }
        }

        /* ---------------- 托盘与关闭 ---------------- */

        /// <summary>离屏测试用：不创建托盘图标（托盘图标是桌面可见元素，测试不应打扰用户）。</summary>
        internal static bool SuppressTrayForTests { get; set; }

        private void BuildTray()
        {
            if (SuppressTrayForTests) return;
            _tray = new System.Windows.Forms.NotifyIcon();
            using (Stream iconStream = typeof(MainWindow).Assembly.GetManifestResourceStream("TimeSaver.AppIcon.ico"))
            using (System.Drawing.Icon embeddedIcon = new System.Drawing.Icon(iconStream))
            {
                _tray.Icon = (System.Drawing.Icon)embeddedIcon.Clone();
            }
            _tray.Text = "Time Saver";
            _tray.Visible = true;
            _tray.DoubleClick += delegate { ShowFromTray(); };

            System.Windows.Forms.ContextMenuStrip menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("显示主窗口", null, delegate { ShowFromTray(); });
            menu.Items.Add("退出", null, delegate { ExitApplication(); });
            _tray.ContextMenuStrip = menu;
        }

        private void ShowFromTray()
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }

        private void ExitApplication()
        {
            _exiting = true;
            Close();
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_exiting && _state.CloseToTray)
            {
                e.Cancel = true;
                SaveState();
                Hide();
                if (!_state.AskedClosePreference)
                {
                    _state.AskedClosePreference = true;
                    SaveState();
                    _tray.ShowBalloonTip(2500, "Time Saver 仍在运行",
                        "已隐藏到托盘。要彻底退出请右键托盘图标选择“退出”。",
                        System.Windows.Forms.ToolTipIcon.Info);
                }
                return;
            }

            SaveState();
            IList<OwnedProcess> owned = _context.Supervisor.Snapshot();
            if (owned.Count > 0)
            {
                MessageBoxResult answer = MessageBox.Show(this,
                    "启动器仍记录了 " + owned.Count + " 个正在运行的工具进程。\n退出启动器不会结束它们。继续退出？",
                    "退出启动器", MessageBoxButton.OKCancel, MessageBoxImage.Question);
                if (answer != MessageBoxResult.OK)
                {
                    e.Cancel = true;
                    return;
                }
            }
        }

        private void SaveState()
        {
            if (WindowState == WindowState.Normal)
            {
                _state.Left = Left;
                _state.Top = Top;
                _state.Width = Width;
                _state.Height = Height;
            }
            _state.Maximized = WindowState == WindowState.Maximized;
            _state.Save(_statePath);
        }

        private void OnClosed(object sender, EventArgs e)
        {
            if (_timer != null) _timer.Stop();
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
            }
            _context.Log.Info("启动器退出");
        }
    }
}
