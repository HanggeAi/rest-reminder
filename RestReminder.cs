// RestReminder —— 休息提醒小工具
// 功能：每隔 n 分钟提醒休息；午饭/晚饭时间提醒；声音提示；托盘常驻。
// 使用 Windows 自带的 .NET Framework 编译（见 build.bat），单个 exe，绿色免安装。

using System;
using System.Drawing;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace RestReminder
{
    internal static class Program
    {
        private static System.Threading.Mutex single;

        [STAThread]
        private static void Main()
        {
            bool createdNew;
            single = new System.Threading.Mutex(true, "RestReminder_SingleInstance", out createdNew);
            if (!createdNew)
            {
                MessageBox.Show("休息提醒已经在运行了，请在任务栏右下角的托盘图标中打开。",
                    "休息提醒", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Win32.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());

            GC.KeepAlive(single);
        }
    }

    internal static class Win32
    {
        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();
    }

    // 统一的配色 / 字体 / 小控件工厂，保持界面风格一致
    internal static class Ui
    {
        public static readonly Color Bg     = Color.FromArgb(30, 30, 36);    // 窗口背景
        public static readonly Color Panel  = Color.FromArgb(42, 42, 50);    // 卡片背景
        public static readonly Color Field  = Color.FromArgb(54, 54, 64);    // 输入控件底色
        public static readonly Color Text   = Color.FromArgb(235, 235, 240);
        public static readonly Color Dim    = Color.FromArgb(150, 154, 162);
        public static readonly Color Teal   = Color.FromArgb(38, 166, 154);  // 休息主题色
        public static readonly Color Orange = Color.FromArgb(235, 160, 59);  // 吃饭主题色
        public static readonly Color Red    = Color.FromArgb(176, 74, 64);   // 下班主题色

        public static readonly Font FTitle      = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold);
        public static readonly Font FSub        = new Font("Microsoft YaHei UI", 9F);
        public static readonly Font FCardHead   = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
        public static readonly Font FBody       = new Font("Microsoft YaHei UI", 9.75F);
        public static readonly Font FPopupTitle = new Font("Microsoft YaHei UI", 19F, FontStyle.Bold);
        public static readonly Font FPopupBody  = new Font("Microsoft YaHei UI", 10.5F);
        public static readonly Font FCountdown  = new Font("Microsoft YaHei UI", 22F, FontStyle.Bold);

        private static Icon appIcon;
        public static Icon AppIcon
        {
            get
            {
                if (appIcon != null) return appIcon;
                try
                {
                    // xiu.ico 在编译时以 /resource:xiu.ico 嵌入，运行时无需外部文件
                    System.Reflection.Assembly asm = System.Reflection.Assembly.GetExecutingAssembly();
                    using (Stream s = asm.GetManifestResourceStream("xiu.ico"))
                    {
                        if (s != null) appIcon = new Icon(s);
                    }
                }
                catch { }
                if (appIcon == null) appIcon = SystemIcons.Application; // 资源缺失时兜底
                return appIcon;
            }
        }

        public static Label Label(Control parent, string text, Font font, Color color, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = font;
            l.ForeColor = color;
            l.AutoSize = true;
            l.BackColor = Color.Transparent;
            l.Location = new Point(x, y);
            parent.Controls.Add(l);
            return l;
        }

        // 布局坐标按 96dpi 设计；高 DPI 屏上字号会随 DPI 变大而像素坐标不变，
        // 导致文字溢出、控件被裁剪，因此在控件全部创建后按 DPI 因子整体放大。
        public static void ApplyDpiScale(Form form)
        {
            float k;
            using (Graphics g = form.CreateGraphics())
            {
                k = g.DpiX / 96f;
            }
            if (Math.Abs(k - 1f) < 0.01f) return;
            form.Scale(new SizeF(k, k));
        }

        public static CheckBox CheckBox(Control parent, string text, int x, int y)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.Font = FBody;
            c.ForeColor = Text;
            c.BackColor = Color.Transparent;
            c.AutoSize = true;
            c.Location = new Point(x, y);
            parent.Controls.Add(c);
            return c;
        }

        // 深色卡片（可带标题）；标题为空则只画底板
        public static Panel AddCard(Control parent, int top, int height, string title)
        {
            Panel card = new Panel();
            card.SetBounds(20, top, 380, height);
            card.BackColor = Ui.Panel;
            parent.Controls.Add(card);
            if (!string.IsNullOrEmpty(title)) Label(card, title, FCardHead, Text, 18, 12);
            return card;
        }

        public static DateTimePicker MakeTimePicker(Control parent, TimeSpan value, int x, int y)
        {
            DateTimePicker d = new DateTimePicker();
            d.Format = DateTimePickerFormat.Time;
            d.ShowUpDown = true;
            d.Font = Ui.FBody;
            d.BackColor = Ui.Field;
            d.ForeColor = Ui.Text;
            d.Value = DateTime.Today.Add(value);
            d.SetBounds(x, y, 100, 30);
            parent.Controls.Add(d);
            return d;
        }

        public static Button MakeButton(string text, Color back, Color fore, bool border)
        {
            Button b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = border ? 1 : 0;
            b.FlatAppearance.BorderColor = Ui.Dim;
            b.BackColor = back;
            b.ForeColor = fore;
            b.Font = Ui.FCardHead;
            b.Cursor = Cursors.Hand;
            return b;
        }
    }

    internal enum ReminderKind { Rest, Lunch, Dinner }

    // 提醒弹窗：无边框深色卡片 + 主题色描边，20 秒后自动关闭
    internal sealed class ReminderForm : Form
    {
        private readonly Timer countdown = new Timer();
        private int secondsLeft = 180; // 弹窗停留 3 分钟，保证休息到位
        private readonly Label lblCount;

        public ReminderForm(ReminderKind kind, bool sound, int workMinutes, TimeSpan mealTime)
        {
            string title, detail;
            Color accent;
            switch (kind)
            {
                case ReminderKind.Lunch:
                    title = "该吃午饭了";
                    detail = "午饭时间到（" + MealStr(mealTime) + "），按时吃饭，下午更有精神";
                    accent = Ui.Orange;
                    break;
                case ReminderKind.Dinner:
                    title = "该吃晚饭了";
                    detail = "晚饭时间到（" + MealStr(mealTime) + "），好好吃饭，犒劳一下自己";
                    accent = Ui.Orange;
                    break;
                default:
                    title = "该休息一下了";
                    detail = "已连续工作 " + workMinutes + " 分钟，起来活动活动，看看远处吧";
                    accent = Ui.Teal;
                    break;
            }

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(460, 250);
            BackColor = accent; // 露出 1px 描边

            Panel card = new Panel();
            card.SetBounds(1, 1, 458, 248);
            card.BackColor = Ui.Bg;
            Controls.Add(card);

            Panel bar = new Panel();
            bar.SetBounds(0, 0, 6, 248);
            bar.BackColor = accent;
            card.Controls.Add(bar);

            Ui.Label(card, title, Ui.FPopupTitle, Ui.Text, 36, 48);
            Ui.Label(card, detail, Ui.FPopupBody, Ui.Dim, 38, 104);

            lblCount = new Label();
            lblCount.Text = RemainText(secondsLeft);
            lblCount.Font = Ui.FSub;
            lblCount.ForeColor = Ui.Dim;
            lblCount.AutoSize = true;
            lblCount.BackColor = Color.Transparent;
            lblCount.Location = new Point(38, 172);
            card.Controls.Add(lblCount);

            Button btnOk = new Button();
            btnOk.Text = "知道了";
            btnOk.FlatStyle = FlatStyle.Flat;
            btnOk.FlatAppearance.BorderSize = 0;
            btnOk.FlatAppearance.MouseOverBackColor = ControlPaint.Light(accent, 0.2F);
            btnOk.BackColor = accent;
            btnOk.ForeColor = Color.White;
            btnOk.Font = Ui.FCardHead;
            btnOk.Cursor = Cursors.Hand;
            btnOk.SetBounds(314, 160, 112, 44);
            btnOk.Click += delegate { ClosePopup(); };
            card.Controls.Add(btnOk);

            countdown.Interval = 1000;
            countdown.Tick += delegate
            {
                secondsLeft--;
                if (secondsLeft <= 0) ClosePopup();
                else lblCount.Text = RemainText(secondsLeft);
            };
            countdown.Start();

            Shown += delegate
            {
                if (!sound) return;
                if (kind == ReminderKind.Rest) SystemSounds.Exclamation.Play();
                else SystemSounds.Asterisk.Play();
            };

            Ui.ApplyDpiScale(this); // 同主窗体，高 DPI 屏整体放大
        }

        private static string MealStr(TimeSpan t) { return t.ToString(@"hh\:mm"); }

        private static string RemainText(int s)
        {
            if (s >= 60) return string.Format("{0} 分 {1:00} 秒后自动关闭（Esc 也可关闭）", s / 60, s % 60);
            return s + " 秒后自动关闭（Esc 也可关闭）";
        }

        private void ClosePopup()
        {
            countdown.Stop();
            Close();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { ClosePopup(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            countdown.Dispose();
            base.OnFormClosed(e);
        }
    }

    // 开机自启：当前用户注册表 Run 键（无需管理员权限），注册表即唯一事实源
    internal static class AutoStart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Name = "RestReminder";

        public static bool Get()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    return k != null && k.GetValue(Name) != null;
                }
            }
            catch { return false; }
        }

        public static bool Set(bool on)
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue(Name, "\"" + Application.ExecutablePath + "\"");
                    else if (k.GetValue(Name) != null) k.DeleteValue(Name);
                }
                return true;
            }
            catch { return false; }
        }
    }

    // 配置：保存在 %APPDATA%\RestReminder\settings.ini
    internal sealed class Settings
    {
        public int RestMinutes = 45;
        public bool RestEnabled = true;
        public bool LunchEnabled = true;
        public TimeSpan Lunch = new TimeSpan(11, 30, 0);
        public bool DinnerEnabled = true;
        public TimeSpan Dinner = new TimeSpan(17, 0, 0);
        public bool Sound = true;

        private static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RestReminder"); }
        }

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                string path = Path.Combine(Dir, "settings.ini");
                if (!File.Exists(path)) return s;
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    Apply(s, line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim());
                }
            }
            catch { /* 配置读取失败时使用默认值 */ }
            return s;
        }

        private static void Apply(Settings s, string key, string val)
        {
            int n;
            TimeSpan t;
            switch (key)
            {
                case "RestMinutes":
                    { if (int.TryParse(val, out n) && n >= 1 && n <= 240) s.RestMinutes = n; break; }
                case "RestEnabled":
                    { s.RestEnabled = val == "1"; break; }
                case "LunchEnabled":
                    { s.LunchEnabled = val == "1"; break; }
                case "DinnerEnabled":
                    { s.DinnerEnabled = val == "1"; break; }
                case "Lunch":
                    { if (TimeSpan.TryParse(val, out t) && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1)) s.Lunch = t; break; }
                case "Dinner":
                    { if (TimeSpan.TryParse(val, out t) && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1)) s.Dinner = t; break; }
                case "Sound":
                    { s.Sound = val == "1"; break; }
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                string[] lines = new string[]
                {
                    "RestMinutes=" + RestMinutes,
                    "RestEnabled=" + (RestEnabled ? "1" : "0"),
                    "LunchEnabled=" + (LunchEnabled ? "1" : "0"),
                    "Lunch=" + Lunch.ToString(@"hh\:mm"),
                    "DinnerEnabled=" + (DinnerEnabled ? "1" : "0"),
                    "Dinner=" + Dinner.ToString(@"hh\:mm"),
                    "Sound=" + (Sound ? "1" : "0")
                };
                File.WriteAllLines(Path.Combine(Dir, "settings.ini"), lines, Encoding.UTF8);
            }
            catch { /* 保存失败不影响运行 */ }
        }
    }

    // 设置窗口：所有可调参数集中于此，主页保持简洁；修改立即生效并保存
    internal sealed class SettingsForm : Form
    {
        private readonly Settings cfg;
        private readonly Action<string> onChanged; // key: "Lunch"/"Dinner"/其他
        private bool busy;

        private CheckBox chkRest, chkLunch, chkDinner, chkSound, chkAuto;
        private NumericUpDown numMinutes;
        private DateTimePicker dtpLunch, dtpDinner;

        public SettingsForm(Settings cfg, Action<string> onChanged)
        {
            this.cfg = cfg;
            this.onChanged = onChanged;

            Text = "设置 · 休息提醒";
            Font = Ui.FBody;
            Icon = Ui.AppIcon;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Ui.Bg;
            ForeColor = Ui.Text;
            ClientSize = new Size(420, 662);

            BuildUi();
            Ui.ApplyDpiScale(this);
        }

        private void BuildUi()
        {
            Ui.Label(this, "设置", Ui.FTitle, Ui.Text, 24, 22);
            Ui.Label(this, "修改立即生效并自动保存", Ui.FSub, Ui.Dim, 26, 62);

            Panel p1 = Ui.AddCard(this, 100, 104, "定时休息");
            chkRest = Ui.CheckBox(p1, "启用", 308, 16);
            Ui.Label(p1, "每隔", Ui.FBody, Ui.Text, 20, 62);
            numMinutes = new NumericUpDown();
            numMinutes.Minimum = 1;
            numMinutes.Maximum = 240;
            numMinutes.Value = Math.Max(1, Math.Min(240, cfg.RestMinutes));
            numMinutes.TextAlign = HorizontalAlignment.Center;
            numMinutes.Font = Ui.FBody;
            numMinutes.BackColor = Ui.Field;
            numMinutes.ForeColor = Ui.Text;
            numMinutes.SetBounds(66, 58, 64, 30);
            p1.Controls.Add(numMinutes);
            Ui.Label(p1, "分钟提醒我休息", Ui.FBody, Ui.Text, 142, 62);

            Panel p2 = Ui.AddCard(this, 220, 146, "吃饭提醒");
            chkLunch = Ui.CheckBox(p2, "午饭", 20, 58);
            dtpLunch = Ui.MakeTimePicker(p2, cfg.Lunch, 92, 54);
            chkDinner = Ui.CheckBox(p2, "晚饭", 20, 102);
            dtpDinner = Ui.MakeTimePicker(p2, cfg.Dinner, 92, 98);

            // 注意：卡片标题标签行高较大（雅黑约 25px），行控件必须留足纵向间距，
            // 否则标签矩形会盖住复选框导致无法点击
            Panel p3 = Ui.AddCard(this, 382, 90, "提示音");
            chkSound = Ui.CheckBox(p3, "弹出提醒时播放声音", 20, 52);

            Panel p4 = Ui.AddCard(this, 488, 90, "开机自启");
            chkAuto = Ui.CheckBox(p4, "开机时自动启动休息提醒", 20, 52);

            Button btnDone = Ui.MakeButton("完成", Ui.Teal, Color.White, false);
            btnDone.SetBounds(215, 594, 185, 44);
            btnDone.Click += delegate { Close(); };
            Controls.Add(btnDone);

            // 先赋初值、后挂事件，避免初始化期间写配置
            chkRest.Checked = cfg.RestEnabled;
            chkLunch.Checked = cfg.LunchEnabled;
            chkDinner.Checked = cfg.DinnerEnabled;
            chkSound.Checked = cfg.Sound;
            chkAuto.Checked = AutoStart.Get();

            chkRest.CheckedChanged += delegate { Apply("Rest"); };
            numMinutes.ValueChanged += delegate { Apply("Rest"); };
            chkLunch.CheckedChanged += delegate { Apply("Lunch"); };
            chkDinner.CheckedChanged += delegate { Apply("Dinner"); };
            dtpLunch.ValueChanged += delegate { Apply("Lunch"); };
            dtpDinner.ValueChanged += delegate { Apply("Dinner"); };
            chkSound.CheckedChanged += delegate { Apply("Sound"); };
            chkAuto.CheckedChanged += delegate { Apply("AutoStart"); };
        }

        // 统一收集所有控件当前值写入配置；busy 防止回滚勾选时递归触发
        private void Apply(string key)
        {
            if (busy) return;
            busy = true;
            try
            {
                cfg.RestEnabled = chkRest.Checked;
                cfg.RestMinutes = (int)numMinutes.Value;
                cfg.LunchEnabled = chkLunch.Checked;
                cfg.Lunch = dtpLunch.Value.TimeOfDay;
                cfg.DinnerEnabled = chkDinner.Checked;
                cfg.Dinner = dtpDinner.Value.TimeOfDay;
                cfg.Sound = chkSound.Checked;
                cfg.Save();

                if (key == "AutoStart" && !AutoStart.Set(chkAuto.Checked))
                {
                    chkAuto.Checked = !chkAuto.Checked; // 回滚
                    MessageBox.Show("无法修改开机自启，请检查注册表权限。", "休息提醒",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (onChanged != null) onChanged(key);
            }
            finally { busy = false; }
        }
    }

    // 主窗口：状态卡片 + 按钮区 + 托盘常驻 + 秒级计时
    internal sealed class MainForm : Form
    {
        private readonly Settings cfg = Settings.Load();
        private readonly NotifyIcon tray;
        private readonly Timer clock = new Timer();
        private DateTime lastRest = DateTime.Now;
        private DateTime lunchFiredOn = DateTime.MinValue; // 每天只提醒一次
        private DateTime dinnerFiredOn = DateTime.MinValue;
        private bool reallyExit;
        private bool hiddenOnce;
        private bool working; // 点击「开始工作」后才开始计时；「暂停工作」停止计时

        private Button btnStart, btnPause;
        private Label lblState, lblCountdown;

        public MainForm()
        {
            Text = "休息提醒";
            Font = Ui.FBody;
            Icon = Ui.AppIcon;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Ui.Bg;
            ForeColor = Ui.Text;
            ClientSize = new Size(420, 380);

            BuildUi();
            Ui.ApplyDpiScale(this); // 高 DPI 屏（如 150%）按 DPI 整体放大布局与窗口

            ContextMenuStrip trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("打开主界面", null, delegate { Restore(); });
            trayMenu.Items.Add("设置", null, delegate { Restore(); OpenSettings(); });
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("退出", null, delegate { reallyExit = true; Close(); });

            tray = new NotifyIcon();
            tray.Icon = Ui.AppIcon;
            tray.Text = "休息提醒";
            tray.ContextMenuStrip = trayMenu;
            tray.Visible = true;
            tray.DoubleClick += delegate { Restore(); };

            clock.Interval = 1000;
            clock.Tick += OnSecond;
            clock.Start();

            UpdateWorkState(); // 初始：未开始计时
        }

        private void BuildUi()
        {
            Ui.Label(this, "休息提醒", Ui.FTitle, Ui.Text, 24, 22);
            Ui.Label(this, "劳逸结合 · 按时吃饭", Ui.FSub, Ui.Dim, 26, 62);

            // 状态卡片：工作状态行 + 距下次休息倒计时（工作中每秒刷新）
            Panel card = Ui.AddCard(this, 100, 140, null);
            lblState = new Label();
            lblState.Font = Ui.FSub;
            lblState.ForeColor = Ui.Dim;
            lblState.AutoSize = true;
            lblState.BackColor = Color.Transparent;
            lblState.Location = new Point(16, 16);
            card.Controls.Add(lblState);
            Ui.Label(card, "距下次休息", Ui.FSub, Ui.Dim, 16, 48);
            lblCountdown = new Label();
            lblCountdown.Font = Ui.FCountdown;
            lblCountdown.ForeColor = Ui.Dim;
            lblCountdown.AutoSize = true;
            lblCountdown.BackColor = Color.Transparent;
            lblCountdown.Location = new Point(14, 74);
            lblCountdown.Text = "--:--";
            card.Controls.Add(lblCountdown);

            btnStart = Ui.MakeButton("开始工作", Ui.Teal, Color.White, false);
            btnStart.SetBounds(20, 260, 118, 44);
            btnStart.Click += delegate
            {
                working = true;
                lastRest = DateTime.Now; // （重新）开始计时
                UpdateWorkState();
            };
            Controls.Add(btnStart);

            btnPause = Ui.MakeButton("暂停工作", Ui.Panel, Ui.Text, true);
            btnPause.SetBounds(146, 260, 118, 44);
            btnPause.Click += delegate
            {
                working = false; // 下次「开始工作」将重新计时
                UpdateWorkState();
            };
            Controls.Add(btnPause);

            Button btnOffWork = Ui.MakeButton("下班", Ui.Red, Color.White, false);
            btnOffWork.SetBounds(272, 260, 128, 44);
            btnOffWork.Click += delegate
            {
                DialogResult r = MessageBox.Show("确定下班，退出休息提醒吗？", "下班确认",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r == DialogResult.Yes)
                {
                    reallyExit = true;
                    Close();
                }
            };
            Controls.Add(btnOffWork);

            Button btnSettings = Ui.MakeButton("设置", Ui.Panel, Ui.Text, true);
            btnSettings.SetBounds(20, 312, 96, 44);
            btnSettings.Click += delegate { OpenSettings(); };
            Controls.Add(btnSettings);

            Button btnPreview = Ui.MakeButton("预览提醒效果", Ui.Panel, Ui.Text, true);
            btnPreview.SetBounds(124, 312, 128, 44);
            btnPreview.Click += delegate { lastRest = DateTime.Now; ShowPopup(ReminderKind.Rest); };
            Controls.Add(btnPreview);

            Button btnHide = Ui.MakeButton("最小化到托盘", Ui.Teal, Color.White, false);
            btnHide.SetBounds(260, 312, 140, 44);
            btnHide.Click += delegate { Hide(); };
            Controls.Add(btnHide);
        }

        private void OnSecond(object sender, EventArgs e)
        {
            DateTime now = DateTime.Now;

            // 用时间差判断，系统睡眠唤醒后依然正确；仅在「工作中」才计时
            if (working && cfg.RestEnabled && now - lastRest >= TimeSpan.FromMinutes(cfg.RestMinutes))
            {
                lastRest = now;
                ShowPopup(ReminderKind.Rest);
            }

            TimeSpan t = now.TimeOfDay;
            if (cfg.LunchEnabled && InMinute(t, cfg.Lunch) && lunchFiredOn != now.Date)
            {
                lunchFiredOn = now.Date;
                ShowPopup(ReminderKind.Lunch);
            }
            if (cfg.DinnerEnabled && InMinute(t, cfg.Dinner) && dinnerFiredOn != now.Date)
            {
                dinnerFiredOn = now.Date;
                ShowPopup(ReminderKind.Dinner);
            }

            UpdateCountdownText(now);
            UpdateTrayText();
        }

        // 同步状态行、按钮可用性、倒计时与托盘提示
        private void UpdateWorkState()
        {
            if (working)
            {
                lblState.Text = "● 工作计时中";
                lblState.ForeColor = Ui.Teal;
            }
            else
            {
                lblState.Text = "○ 计时未开始或已暂停 · 点击「开始工作」（重新）开始计时";
                lblState.ForeColor = Ui.Dim;
            }
            btnStart.Enabled = !working;
            btnPause.Enabled = working;
            UpdateCountdownText(DateTime.Now);
            UpdateTrayText();
        }

        private void UpdateCountdownText(DateTime now)
        {
            if (working && cfg.RestEnabled)
            {
                TimeSpan left = lastRest.AddMinutes(cfg.RestMinutes) - now;
                if (left.TotalSeconds < 0) left = TimeSpan.Zero;
                lblCountdown.Text = FormatSpan(left);
                lblCountdown.ForeColor = Ui.Teal;
            }
            else
            {
                lblCountdown.Text = working ? "未启用" : "--:--";
                lblCountdown.ForeColor = Ui.Dim;
            }
        }

        private static string FormatSpan(TimeSpan t)
        {
            int s = (int)t.TotalSeconds;
            if (s >= 3600) return string.Format("{0}:{1:00}:{2:00}", s / 3600, (s / 60) % 60, s % 60);
            return string.Format("{0:00}:{1:00}", s / 60, s % 60);
        }

        private void OpenSettings()
        {
            using (SettingsForm f = new SettingsForm(cfg, OnSettingChanged))
            {
                f.ShowDialog(this);
            }
        }

        // 设置变化后同步主页；饭点被修改时清除当日已提醒标记，允许当天重新提醒
        private void OnSettingChanged(string key)
        {
            if (key == "Lunch") lunchFiredOn = DateTime.MinValue;
            else if (key == "Dinner") dinnerFiredOn = DateTime.MinValue;
            UpdateWorkState();
        }

        private void UpdateTrayText()
        {
            if (!working) tray.Text = "休息提醒 · 计时已暂停";
            else if (cfg.RestEnabled)
                tray.Text = string.Format("休息提醒 · 工作中 · 下次休息 {0:HH:mm}", lastRest.AddMinutes(cfg.RestMinutes));
            else
                tray.Text = "休息提醒 · 工作中（定时休息已停用）";
        }

        private static bool InMinute(TimeSpan now, TimeSpan target)
        {
            return now >= target && now < target + TimeSpan.FromMinutes(1);
        }

        private void ShowPopup(ReminderKind kind)
        {
            ReminderForm f = new ReminderForm(
                kind, cfg.Sound, cfg.RestMinutes,
                kind == ReminderKind.Lunch ? cfg.Lunch : cfg.Dinner);
            f.Show(this);
        }

        private void Restore()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!reallyExit)
            {
                // 点 X 视为最小化到托盘，程序继续后台计时
                e.Cancel = true;
                Hide();
                if (!hiddenOnce)
                {
                    hiddenOnce = true;
                    tray.ShowBalloonTip(2000, "休息提醒仍在运行", "已最小化到托盘，双击图标可重新打开。", ToolTipIcon.Info);
                }
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            clock.Stop();
            tray.Visible = false;
            tray.Dispose();
            base.OnFormClosed(e);
        }
    }
}
