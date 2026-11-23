using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace BatchIPServer
{
    public partial class MainForm : Form
    {
        private static readonly string[] PrefixCycle =
            { "z", "x", "c", "v", "a", "b", "d", "e", "f", "g", "h", "k", "m", "n", "p", "s", "t" };

        private readonly NetworkService _net = new NetworkService();
        private readonly string _appDir;
        private readonly string _configPath;
        private readonly string _backupDir;
        private readonly List<AdapterItem> _adapters = new List<AdapterItem>();
        private AppConfig _savedCfg;
        private bool _suppressEvents;

        // Colors
        private readonly Color _clrBgMain = Color.FromArgb(243, 246, 250);
        private readonly Color _clrCardBg = Color.White;
        private readonly Color _clrBorder = Color.FromArgb(212, 220, 230);
        private readonly Color _clrPrimary = Color.FromArgb(0, 103, 192);
        private readonly Color _clrPrimaryHover = Color.FromArgb(0, 90, 170);
        private readonly Color _clrHeaderBg = Color.FromArgb(234, 240, 248);
        private readonly Color _clrTextMuted = Color.FromArgb(95, 106, 120);
        private readonly Color _clrBtnBg = Color.FromArgb(248, 250, 252);
        private readonly Color _clrBtnHover = Color.FromArgb(232, 240, 252);

        // Controls
        private GroupBox _gbIp, _gbNet, _gbOpt;
        private TextBox _txtSubnetBase, _txtNamePrefix, _txtGw1, _txtGw2, _txtDns1, _txtDns2, _txtLog;
        private ComboBox _cmbSubnetMask, _cmbGwMode;
        private NumericUpDown _numStartOctet;
        private RadioButton _rbAsc, _rbDesc;
        private CheckBox _chkRename, _chkSetDns, _chkClearOld, _chkDisableNetbios, _chkDisableFileShare, _chkDisableQoS, _chkDisableIPv6, _chkDisablePowerSave, _chkShowVirtual;
        private Button _btnNextPrefix, _btnResetDns, _btnCheckIp, _btnRollback, _btnRefresh, _btnApply;
        private Panel _pnlToolbar, _pnlOnboard;
        private Label _lblSummary, _lblOnboardAlert;
        private SplitContainer _splitMain;
        private DataGridView _dgv;

        public MainForm()
        {
            InitializeComponent();
            string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
            _appDir = string.IsNullOrEmpty(exeDir) ? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\') : exeDir;
            try
            {
                string testBackup = Path.Combine(_appDir, "BatchIP_Backups");
                if (!Directory.Exists(testBackup)) Directory.CreateDirectory(testBackup);
                string probe = Path.Combine(testBackup, ".write_test");
                File.WriteAllText(probe, "1");
                File.Delete(probe);
                _backupDir = testBackup;
            }
            catch
            {
                _appDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BatchIPServer");
                _backupDir = Path.Combine(_appDir, "BatchIP_Backups");
                if (!Directory.Exists(_backupDir)) Directory.CreateDirectory(_backupDir);
            }
            _configPath = Path.Combine(_appDir, "BatchIP_Config.json");

            try { this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            _savedCfg = _net.LoadConfig(_configPath);
            BuildUi();
            WireEvents();
        }

        private void BuildUi()
        {
            this.Font = new Font("Segoe UI", 9F);
            this.BackColor = _clrBgMain;
            var regularFont = new Font("Segoe UI", 9F, FontStyle.Regular);

            // --- Top 3-Column Container ---
            var pnlTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 278,
                Padding = new Padding(10, 8, 10, 4)
            };

            var tlpTop = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1
            };
            tlpTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            tlpTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
            tlpTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            tlpTop.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            pnlTop.Controls.Add(tlpTop);

            // Card 1: Dải IP & Sinh số thứ tự
            _gbIp = CreateCard("1. Dải IP && Sinh số thứ tự");
            tlpTop.Controls.Add(_gbIp, 0, 0);

            _gbIp.Controls.Add(MakeLabel("Preset nhanh:", 12, 28, regularFont));
            var btnSub1 = MakeFlatButton("192.168.1.", 104, 23, 96, 27, regularFont);
            var btnSub254 = MakeFlatButton("192.168.254.", 206, 23, 108, 27, regularFont);
            _gbIp.Controls.Add(btnSub1);
            _gbIp.Controls.Add(btnSub254);

            _gbIp.Controls.Add(MakeLabel("3 octet đầu:", 12, 64, regularFont));
            _txtSubnetBase = new TextBox { Text = _savedCfg.SubnetBase, Font = regularFont, Location = new Point(104, 61), Size = new Size(104, 23) };
            _cmbSubnetMask = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = regularFont, Location = new Point(214, 61), Size = new Size(194, 23) };
            _cmbSubnetMask.Items.AddRange(new object[] { "255.255.255.0 (/24)", "255.255.0.0 (/16)", "255.255.255.128 (/25)", "255.255.255.192 (/26)" });
            _cmbSubnetMask.SelectedItem = _savedCfg.SubnetMask;
            if (_cmbSubnetMask.SelectedIndex < 0) _cmbSubnetMask.SelectedIndex = 0;
            _gbIp.Controls.Add(_txtSubnetBase);
            _gbIp.Controls.Add(_cmbSubnetMask);

            _gbIp.Controls.Add(MakeLabel("IP bắt đầu (.x):", 12, 98, regularFont));
            _numStartOctet = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 254,
                Value = Math.Max(1, Math.Min(254, _savedCfg.StartOctet)),
                Font = regularFont,
                Location = new Point(104, 95),
                Size = new Size(62, 23)
            };
            _rbAsc = new RadioButton { Text = "Tăng (+1)", Font = regularFont, ForeColor = Color.Black, Location = new Point(174, 96), AutoSize = true };
            _rbDesc = new RadioButton { Text = "Giảm (-1)", Font = regularFont, ForeColor = Color.Black, Location = new Point(258, 96), AutoSize = true };
            if (_savedCfg.Direction == "Down") _rbDesc.Checked = true; else _rbAsc.Checked = true;
            _gbIp.Controls.Add(_numStartOctet);
            _gbIp.Controls.Add(_rbAsc);
            _gbIp.Controls.Add(_rbDesc);

            var btnSeq200 = MakeFlatButton("Preset: .200, .201, .202...", 12, 130, 172, 28, regularFont);
            var btnSeq253 = MakeFlatButton("Preset: .253, .252, .251...", 190, 130, 172, 28, regularFont);
            _gbIp.Controls.Add(btnSeq200);
            _gbIp.Controls.Add(btnSeq253);

            _chkRename = new CheckBox
            {
                Text = "Đổi tên card theo IP (VD: z200, z201, z202...)",
                Font = regularFont,
                ForeColor = Color.Black,
                Checked = true,
                Location = new Point(12, 172),
                AutoSize = true
            };
            _gbIp.Controls.Add(_chkRename);

            _gbIp.Controls.Add(MakeLabel("Ký tự tiền tố:", 12, 206, regularFont));
            _txtNamePrefix = new TextBox { Font = regularFont, Location = new Point(104, 203), Size = new Size(56, 23) };
            _btnNextPrefix = MakeFlatButton("Xoay ký tự", 166, 201, 92, 27, regularFont);
            _gbIp.Controls.Add(_txtNamePrefix);
            _gbIp.Controls.Add(_btnNextPrefix);

            _gbIp.Resize += (s, e) =>
            {
                int avail = _gbIp.ClientSize.Width - 226;
                if (avail >= 120) _cmbSubnetMask.Width = avail;
            };

            // Card 2: Gateway & DNS
            _gbNet = CreateCard("2. Gateway (1 hoặc 2 Gateway) && DNS");
            tlpTop.Controls.Add(_gbNet, 1, 0);

            _gbNet.Controls.Add(MakeLabel("Chế độ:", 12, 29, regularFont));
            _cmbGwMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = regularFont, Location = new Point(88, 25), Size = new Size(320, 23) };
            _cmbGwMode.Items.AddRange(new object[]
            {
                "Chung 1 Gateway (Tất cả dùng Gateway 1)",
                "Chỉ card đầu dùng Gateway 1 (Còn lại trống)",
                "2 Gateway - Chia nửa (Đầu Gateway 1 / Sau Gateway 2)",
                "2 Gateway - Xen kẽ (Lẻ Gateway 1 / Chẵn Gateway 2)",
                "Tùy chỉnh từng card trên bảng"
            });
            _cmbGwMode.SelectedIndex = 0;
            _gbNet.Controls.Add(_cmbGwMode);

            _gbNet.Controls.Add(MakeLabel("Gateway 1:", 12, 64, regularFont));
            _txtGw1 = new TextBox { Text = _savedCfg.Gateway1, Font = regularFont, Location = new Point(88, 61), Size = new Size(130, 23) };
            _gbNet.Controls.Add(_txtGw1);
            _gbNet.Controls.Add(MakeMutedLabel("(Gateway chính)", 224, 64, regularFont));

            _gbNet.Controls.Add(MakeLabel("Gateway 2:", 12, 98, regularFont));
            _txtGw2 = new TextBox { Text = _savedCfg.Gateway2, Font = regularFont, Location = new Point(88, 95), Size = new Size(130, 23) };
            _gbNet.Controls.Add(_txtGw2);
            _gbNet.Controls.Add(MakeMutedLabel("(Trống nếu chỉ 1 Gateway)", 224, 98, regularFont));

            _chkSetDns = new CheckBox
            {
                Text = "Cấu hình DNS cho các card được chọn",
                Font = regularFont,
                ForeColor = Color.Black,
                Checked = true,
                Location = new Point(12, 136),
                AutoSize = true
            };
            _gbNet.Controls.Add(_chkSetDns);

            _gbNet.Controls.Add(MakeLabel("DNS Chính:", 12, 169, regularFont));
            _txtDns1 = new TextBox { Text = _savedCfg.Dns1, Font = regularFont, Location = new Point(88, 166), Size = new Size(130, 23) };
            _btnResetDns = MakeFlatButton("Google DNS", 224, 164, 102, 27, regularFont);
            _gbNet.Controls.Add(_txtDns1);
            _gbNet.Controls.Add(_btnResetDns);

            _gbNet.Controls.Add(MakeLabel("DNS Phụ:", 12, 203, regularFont));
            _txtDns2 = new TextBox { Text = _savedCfg.Dns2, Font = regularFont, Location = new Point(88, 200), Size = new Size(130, 23) };
            _gbNet.Controls.Add(_txtDns2);

            _gbNet.Resize += (s, e) =>
            {
                int avail = _gbNet.ClientSize.Width - 100;
                if (avail >= 150) _cmbGwMode.Width = avail;
            };

            // Card 3: Tối ưu Server & Thao tác
            _gbOpt = CreateCard("3. Tối ưu Server && Thao tác");
            tlpTop.Controls.Add(_gbOpt, 2, 0);

            _chkClearOld = MakeOptionCheck("Xóa sạch IP && Gateway cũ trước khi gán", 24, true, regularFont);
            _chkDisableNetbios = MakeOptionCheck("Tắt NetBIOS over TCP/IP", 46, true, regularFont);
            _chkDisableFileShare = MakeOptionCheck("Tắt File and Printer Sharing for Microsoft Networks", 68, true, regularFont);
            _chkDisableQoS = MakeOptionCheck("Tắt QoS Packet Scheduler", 90, true, regularFont);
            _chkDisableIPv6 = MakeOptionCheck("Tắt IPv6 (Internet Protocol Version 6)", 112, true, regularFont);
            _chkDisablePowerSave = MakeOptionCheck("Tắt tiết kiệm điện NIC (Power Saving / EEE)", 134, false, regularFont);
            _chkShowVirtual = MakeOptionCheck("Hiện cả card ảo (VMware, Hyper-V, VPN...)", 156, false, regularFont);

            _gbOpt.Controls.AddRange(new Control[]
            {
                _chkClearOld, _chkDisableNetbios, _chkDisableFileShare,
                _chkDisableQoS, _chkDisableIPv6, _chkDisablePowerSave, _chkShowVirtual
            });

            _btnCheckIp = MakeFlatButton("Check trùng IP", 12, 184, 112, 28, regularFont);
            _btnRollback = MakeFlatButton("Khôi phục (Rollback)", 130, 184, 136, 28, regularFont);
            _btnRefresh = MakeFlatButton("Tải lại NIC", 272, 184, 92, 28, regularFont);
            _btnApply = MakeFlatButton("ÁP DỤNG", 12, 218, 396, 34, new Font("Segoe UI", 10F, FontStyle.Bold), true);

            _gbOpt.Controls.AddRange(new Control[] { _btnCheckIp, _btnRollback, _btnRefresh, _btnApply });

            _gbOpt.Resize += (s, e) =>
            {
                int innerW = _gbOpt.ClientSize.Width - 24;
                if (innerW >= 200)
                {
                    const int gap = 6;
                    int w1 = (int)Math.Floor((innerW - gap * 2) * 0.31);
                    int w2 = (int)Math.Floor((innerW - gap * 2) * 0.39);
                    int w3 = innerW - w1 - w2 - gap * 2;

                    _btnCheckIp.SetBounds(12, 184, w1, 28);
                    _btnRollback.SetBounds(12 + w1 + gap, 184, w2, 28);
                    _btnRefresh.SetBounds(12 + w1 + gap + w2 + gap, 184, w3, 28);
                    _btnApply.SetBounds(12, 218, innerW, 34);
                }
            };

            // --- Middle Toolbar ---
            _pnlToolbar = new Panel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(12, 4, 12, 4) };
            var btnSelectAll = MakeFlatButton("Chọn tất cả", 14, 5, 88, 27, regularFont);
            var btnSelectUp = MakeFlatButton("Chỉ chọn card Up", 108, 5, 116, 27, regularFont);
            var btnUnselectAll = MakeFlatButton("Bỏ chọn hết", 230, 5, 92, 27, regularFont);
            var btnMoveUp = MakeFlatButton("Lên thứ tự", 338, 5, 86, 27, regularFont);
            var btnMoveDown = MakeFlatButton("Xuống thứ tự", 430, 5, 96, 27, regularFont);
            var btnBlink = MakeFlatButton("Nháy cổng NIC", 540, 5, 112, 27, regularFont);
            var btnOpenNcpa = MakeFlatButton("Mở Network Connections", 658, 5, 162, 27, regularFont);

            _lblSummary = new Label
            {
                Text = "Đã chọn: 0 / 0 card",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = _clrPrimary,
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(830, 7),
                Size = new Size(492, 23)
            };

            _pnlToolbar.Controls.AddRange(new Control[]
            {
                btnSelectAll, btnSelectUp, btnUnselectAll, btnMoveUp, btnMoveDown, btnBlink, btnOpenNcpa, _lblSummary
            });
            _pnlToolbar.Resize += (s, e) =>
            {
                int w = _pnlToolbar.ClientSize.Width - 844;
                if (w >= 150) _lblSummary.SetBounds(830, 7, w, 23);
            };

            // --- Onboard Notification Banner ---
            _pnlOnboard = new Panel
            {
                Dock = DockStyle.Top,
                Height = 32,
                Padding = new Padding(16, 6, 16, 4),
                BackColor = Color.FromArgb(255, 244, 206)
            };
            _lblOnboardAlert = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(133, 77, 14),
                Text = "Đang quét card Onboard..."
            };
            _pnlOnboard.Controls.Add(_lblOnboardAlert);

            // --- Workspace SplitContainer ---
            var pnlWorkspace = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 6, 14, 10) };
            _splitMain = new SplitContainer
            {
                Size = new Size(1280, 440),
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 6,
                Panel1MinSize = 180,
                Panel2MinSize = 95,
                SplitterDistance = 280
            };
            pnlWorkspace.Controls.Add(_splitMain);

            // DataGridView
            _dgv = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = Color.FromArgb(230, 235, 242),
                EnableHeadersVisualStyles = false,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
                ColumnHeadersHeight = 32,
                ShowCellToolTips = true
            };
            _dgv.RowTemplate.Height = 28;
            _dgv.DefaultCellStyle.SelectionBackColor = Color.FromArgb(214, 232, 252);
            _dgv.DefaultCellStyle.SelectionForeColor = Color.FromArgb(18, 28, 42);
            _dgv.ColumnHeadersDefaultCellStyle.BackColor = _clrHeaderBg;
            _dgv.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(25, 38, 56);
            _dgv.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);

            _dgv.Columns.Add(new DataGridViewCheckBoxColumn { Name = "colSelect", HeaderText = "Chọn", Width = 46 });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "colCurrName", HeaderText = "Tên hiện tại", Width = 88, ReadOnly = true });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "colType", HeaderText = "Loại card", Width = 76, ReadOnly = true });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "colInfo", HeaderText = "Trạng thái · Tốc độ", Width = 122, ReadOnly = true });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "colDesc", HeaderText = "Phần cứng NIC", MinimumWidth = 175, ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "colCurrIp", HeaderText = "IP hiện tại", Width = 132, ReadOnly = true });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "colNewName", HeaderText = "Tên mới", Width = 76 });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "colNewIP", HeaderText = "IP mới", Width = 112 });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "colPrefix", HeaderText = "Prefix", Width = 48 });

            var colGwRole = new DataGridViewComboBoxColumn { Name = "colGwRole", HeaderText = "Chọn Gateway", Width = 96 };
            colGwRole.Items.AddRange("Gateway 1", "Gateway 2", "Không");
            _dgv.Columns.Add(colGwRole);

            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "colNewGW", HeaderText = "Gateway mới", Width = 108, ReadOnly = true });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "colMetric", HeaderText = "Metric", Width = 64 });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "colNewDNS", HeaderText = "DNS mới", Width = 108, ReadOnly = true });
            _splitMain.Panel1.Controls.Add(_dgv);

            // Log Header + Box
            var pnlLogBar = new Panel { Dock = DockStyle.Top, Height = 28, BackColor = Color.FromArgb(34, 38, 46) };
            pnlLogBar.Controls.Add(new Label
            {
                Text = "Nhật ký thao tác (Log)  —  Mẹo: Sửa trực tiếp [Tên mới], [IP mới], [Chọn Gateway], [Metric] trên bảng phía trên",
                ForeColor = Color.FromArgb(200, 210, 224),
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(8, 6),
                AutoSize = true
            });
            var btnOpenBackup = MakeFlatButton("Mở thư mục Backup", 0, 3, 128, 22, new Font("Segoe UI", 8F));
            var btnClearLog = MakeFlatButton("Xóa Log", 0, 3, 70, 22, new Font("Segoe UI", 8F));
            pnlLogBar.Controls.Add(btnOpenBackup);
            pnlLogBar.Controls.Add(btnClearLog);
            pnlLogBar.Resize += (s, e) =>
            {
                int w = pnlLogBar.ClientSize.Width;
                btnClearLog.Location = new Point(w - 76, 3);
                btnOpenBackup.Location = new Point(w - 210, 3);
            };

            _txtLog = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(24, 26, 31),
                ForeColor = Color.FromArgb(220, 228, 235),
                Font = new Font("Consolas", 9F)
            };

            _splitMain.Panel2.Controls.Add(_txtLog);
            _splitMain.Panel2.Controls.Add(pnlLogBar);

            this.Controls.Add(pnlWorkspace);
            this.Controls.Add(_pnlOnboard);
            this.Controls.Add(_pnlToolbar);
            this.Controls.Add(pnlTop);

            // Bind button clicks created locally
            btnSub1.Click += (s, e) => SetSubnetPreset("192.168.1");
            btnSub254.Click += (s, e) => SetSubnetPreset("192.168.254");
            btnSeq200.Click += (s, e) => { _numStartOctet.Value = 200; _rbAsc.Checked = true; RecalculatePreview(); };
            btnSeq253.Click += (s, e) => { _numStartOctet.Value = 253; _rbDesc.Checked = true; RecalculatePreview(); };
            btnSelectAll.Click += (s, e) => { foreach (var a in _adapters) a.Selected = true; RecalculatePreview(); };
            btnSelectUp.Click += (s, e) => { foreach (var a in _adapters) a.Selected = a.Status == "Up"; RecalculatePreview(); };
            btnUnselectAll.Click += (s, e) => { foreach (var a in _adapters) a.Selected = false; RecalculatePreview(); };
            btnMoveUp.Click += (s, e) => SwapAdapterOrder(-1);
            btnMoveDown.Click += (s, e) => SwapAdapterOrder(1);
            btnBlink.Click += async (s, e) => await BlinkSelectedAdapterAsync();
            btnOpenNcpa.Click += (s, e) => { try { Process.Start("ncpa.cpl"); } catch (Exception ex) { WriteLog("Không mở được ncpa.cpl: " + ex.Message, "WARN"); } };
            btnOpenBackup.Click += (s, e) => { try { Process.Start("explorer.exe", "\"" + _backupDir + "\""); } catch { } };
            btnClearLog.Click += (s, e) => _txtLog.Clear();
        }

        private void WireEvents()
        {
            _btnNextPrefix.Click += (s, e) =>
            {
                _txtNamePrefix.Text = GetNextPrefixSuggestion(_txtNamePrefix.Text.Trim(), Array.Empty<string>());
                RecalculatePreview();
            };
            _btnResetDns.Click += (s, e) =>
            {
                _chkSetDns.Checked = true;
                _txtDns1.Text = "8.8.8.8";
                _txtDns2.Text = "8.8.4.4";
                RecalculatePreview();
            };

            _txtSubnetBase.TextChanged += (s, e) => RecalculatePreview();
            _cmbSubnetMask.SelectedIndexChanged += (s, e) => RecalculatePreview();
            _numStartOctet.ValueChanged += (s, e) => RecalculatePreview();
            _rbAsc.CheckedChanged += (s, e) => RecalculatePreview();
            _rbDesc.CheckedChanged += (s, e) => RecalculatePreview();
            _chkRename.CheckedChanged += (s, e) => RecalculatePreview();
            _txtNamePrefix.TextChanged += (s, e) => RecalculatePreview();
            _cmbGwMode.SelectedIndexChanged += (s, e) => { if (!_suppressEvents) RecalculatePreview(); };
            _txtGw1.TextChanged += (s, e) => RecalculatePreview();
            _txtGw2.TextChanged += (s, e) => RecalculatePreview();
            _chkSetDns.CheckedChanged += (s, e) => RecalculatePreview();
            _txtDns1.TextChanged += (s, e) => RecalculatePreview();
            _txtDns2.TextChanged += (s, e) => RecalculatePreview();

            _chkShowVirtual.CheckedChanged += (s, e) => RefreshAdapters();
            _btnRefresh.Click += (s, e) => RefreshAdapters();
            _btnCheckIp.Click += async (s, e) => await CheckIpConflictsAsync();
            _btnRollback.Click += (s, e) => RollbackLastBackup();
            _btnApply.Click += (s, e) => ApplyConfiguration();

            _dgv.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_dgv.IsCurrentCellDirty && _dgv.CurrentCell != null)
                {
                    string colName = _dgv.Columns[_dgv.CurrentCell.ColumnIndex].Name;
                    if (colName == "colSelect" || colName == "colGwRole")
                        _dgv.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };

            _dgv.CellValueChanged += (s, e) =>
            {
                if (_suppressEvents || e.RowIndex < 0) return;
                string colName = _dgv.Columns[e.ColumnIndex].Name;
                SyncGridEditsToItems();
                if (colName == "colSelect")
                {
                    RecalculatePreview();
                }
                else if (colName == "colGwRole")
                {
                    _suppressEvents = true;
                    _cmbGwMode.SelectedIndex = 4;
                    _suppressEvents = false;
                    var it = _adapters[e.RowIndex];
                    it.NewGateway = ResolveGatewayForRow(it.GwRole, _txtGw1.Text, _txtGw2.Text);
                    _dgv.Rows[e.RowIndex].Cells["colNewGW"].Value = it.NewGateway;
                }
            };

            _dgv.DataError += (s, e) => { e.ThrowException = false; };

            this.Shown += (s, e) =>
            {
                if (_splitMain.Height > _splitMain.Panel1MinSize + _splitMain.Panel2MinSize + 10)
                    _splitMain.SplitterDistance = Math.Max(_splitMain.Panel1MinSize, _splitMain.Height - 165);

                RefreshAdapters();
                _txtNamePrefix.Text = GetNextPrefixSuggestion(_savedCfg.LastPrefix, _adapters.Select(a => a.Name).ToArray());
                RecalculatePreview();
                WriteLog("BatchIP Server (C# Native) sẵn sàng. Tiền tố lần này: '" + _txtNamePrefix.Text + "'", "INFO");
            };
        }

        private void SetSubnetPreset(string newBase)
        {
            _txtSubnetBase.Text = newBase;
            var m = Regex.Match(_txtGw1.Text.Trim(), @"^192\.168\.\d+\.(\d+)$");
            if (m.Success)
                _txtGw1.Text = newBase + "." + m.Groups[1].Value;
            RecalculatePreview();
        }

        private void RefreshAdapters()
        {
            _adapters.Clear();
            _adapters.AddRange(_net.LoadAdapters(_chkShowVirtual.Checked));
            RecalculatePreview();
            var onboardDetails = UpdateOnboardNotification();
            WriteLog("Đã tải " + _adapters.Count + " card mạng.", "INFO");
            foreach (string ob in onboardDetails)
                WriteLog("Phát hiện Card Onboard: " + ob + " (Hãy tự đổi tên nếu cần)", "ONBOARD");
            if (onboardDetails.Count == 0)
                WriteLog("Không phát hiện card Onboard nào.", "INFO");
        }

        private List<string> UpdateOnboardNotification()
        {
            var onboard = _adapters.Where(a => a.IsOnboard).ToList();
            if (onboard.Count > 0)
            {
                string shortList = string.Join(" | ", onboard.Select(o => o.Name + " (" + o.CurrentIPText + ")"));
                _lblOnboardAlert.Text = "PHÁT HIỆN " + onboard.Count + " CARD ONBOARD: " + shortList + "  —  Bạn có thể bấm [Mở Network Connections] để tự đổi tên.";
                _pnlOnboard.BackColor = Color.FromArgb(255, 244, 206);
                _lblOnboardAlert.ForeColor = Color.FromArgb(133, 77, 14);
                return onboard.Select(o => o.Name + " [" + o.Description + "] (IP: " + o.CurrentIPText + " | MAC: " + o.MacAddress + ")").ToList();
            }

            _lblOnboardAlert.Text = "CARD ONBOARD: Không phát hiện card Onboard (tất cả đang nhận diện là card rời PCIe).";
            _pnlOnboard.BackColor = Color.FromArgb(234, 241, 250);
            _lblOnboardAlert.ForeColor = Color.FromArgb(40, 70, 110);
            return new List<string>();
        }

        private void RecalculatePreview()
        {
            if (_suppressEvents) return;
            UpdateInputStates();

            if (_adapters.Count == 0)
            {
                UpdateSummaryBadge();
                return;
            }

            string baseSub = NetworkService.NormalizeSubnetPrefix(_txtSubnetBase.Text);
            int prefixLen = GetPrefixLengthFromCombo(_cmbSubnetMask.Text);
            int startOctet = (int)_numStartOctet.Value;
            int step = _rbDesc.Checked ? -1 : 1;
            bool renameOn = _chkRename.Checked;
            string namePrefix = _txtNamePrefix.Text.Trim();
            string gw1 = _txtGw1.Text.Trim();
            string gw2 = _txtGw2.Text.Trim();
            int gwModeIdx = _cmbGwMode.SelectedIndex;

            string dnsText = "(Giữ nguyên)";
            if (_chkSetDns.Checked)
            {
                var d = new[] { _txtDns1.Text.Trim(), _txtDns2.Text.Trim() }.Where(x => !string.IsNullOrWhiteSpace(x));
                dnsText = string.Join(", ", d);
            }

            int selCount = _adapters.Count(a => a.Selected);
            int selPos = 0;

            foreach (var item in _adapters)
            {
                if (!item.Selected)
                {
                    item.NewIP = "";
                    item.NewName = item.Name;
                    item.NewGateway = "";
                    item.NewDNS = "";
                    continue;
                }

                int octet = startOctet + selPos * step;
                item.NewIP = (octet >= 1 && octet <= 254 && baseSub != null) ? (baseSub + "." + octet) : "NGOÀI DẢI (1-254)";
                item.PrefixLength = prefixLen;
                item.NewName = (renameOn && !string.IsNullOrWhiteSpace(namePrefix) && octet >= 1 && octet <= 254)
                    ? (namePrefix + octet)
                    : item.Name;

                switch (gwModeIdx)
                {
                    case 0: item.GwRole = "Gateway 1"; break;
                    case 1: item.GwRole = selPos == 0 ? "Gateway 1" : "Không"; break;
                    case 2:
                        int half = (int)Math.Ceiling(selCount / 2.0);
                        item.GwRole = selPos < half ? "Gateway 1" : "Gateway 2";
                        break;
                    case 3:
                        item.GwRole = (selPos % 2 == 0) ? "Gateway 1" : "Gateway 2";
                        break;
                }

                item.NewGateway = ResolveGatewayForRow(item.GwRole, gw1, gw2);
                item.NewDNS = dnsText;
                selPos++;
            }

            SyncItemsToGrid();
            UpdateSummaryBadge();
        }

        private void SyncItemsToGrid()
        {
            _suppressEvents = true;
            try
            {
                int prevIdx = _dgv.CurrentCell != null ? _dgv.CurrentCell.RowIndex : -1;
                _dgv.Rows.Clear();

                foreach (var item in _adapters)
                {
                    int rowIdx = _dgv.Rows.Add(
                        item.Selected,
                        item.Name,
                        item.IsOnboard ? "ONBOARD" : "Card rời",
                        item.Status + " · " + item.LinkSpeedText,
                        item.Description,
                        item.CurrentIPText,
                        item.NewName,
                        item.NewIP,
                        item.PrefixLength.ToString(),
                        item.GwRole,
                        item.NewGateway,
                        item.Metric,
                        item.NewDNS
                    );

                    var row = _dgv.Rows[rowIdx];
                    row.Cells["colDesc"].ToolTipText = item.Description + "\r\nMAC: " + item.MacAddress + "\r\nBus: " + item.BusNumber;

                    if (item.Status == "Up")
                    {
                        row.Cells["colInfo"].Style.ForeColor = Color.FromArgb(16, 124, 16);
                        row.Cells["colInfo"].Style.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
                    }
                    else
                    {
                        row.Cells["colInfo"].Style.ForeColor = Color.Gray;
                    }

                    if (item.IsOnboard)
                    {
                        row.Cells["colType"].Style.BackColor = Color.FromArgb(255, 244, 206);
                        row.Cells["colType"].Style.ForeColor = Color.FromArgb(154, 91, 0);
                        row.Cells["colType"].Style.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
                    }
                    else
                    {
                        row.Cells["colType"].Style.ForeColor = Color.FromArgb(90, 98, 110);
                    }

                    row.DefaultCellStyle.BackColor = item.Selected ? Color.FromArgb(238, 246, 255) : Color.White;

                    if (item.NewIP.StartsWith("NGOÀI DẢI", StringComparison.Ordinal))
                    {
                        row.Cells["colNewIP"].Style.ForeColor = Color.FromArgb(196, 43, 28);
                        row.Cells["colNewIP"].Style.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
                    }
                }

                if (prevIdx >= 0 && prevIdx < _dgv.Rows.Count)
                {
                    _dgv.ClearSelection();
                    _dgv.Rows[prevIdx].Selected = true;
                }
            }
            finally
            {
                _suppressEvents = false;
            }
        }

        private void SyncGridEditsToItems()
        {
            if (_suppressEvents) return;
            for (int i = 0; i < _dgv.Rows.Count; i++)
            {
                var row = _dgv.Rows[i];
                var item = _adapters[i];
                item.Selected = Convert.ToBoolean(row.Cells["colSelect"].Value);
                item.NewName = Convert.ToString(row.Cells["colNewName"].Value) ?? "";
                item.NewIP = Convert.ToString(row.Cells["colNewIP"].Value) ?? "";
                if (int.TryParse(Convert.ToString(row.Cells["colPrefix"].Value), out int pLen) && pLen >= 1 && pLen <= 32)
                    item.PrefixLength = pLen;
                item.GwRole = Convert.ToString(row.Cells["colGwRole"].Value) ?? "Gateway 1";
                item.Metric = Convert.ToString(row.Cells["colMetric"].Value) ?? "Tự động";
                if (_cmbGwMode.SelectedIndex == 4)
                {
                    item.NewGateway = ResolveGatewayForRow(item.GwRole, _txtGw1.Text, _txtGw2.Text);
                    row.Cells["colNewGW"].Value = item.NewGateway;
                }
            }
            UpdateSummaryBadge();
        }

        private void UpdateSummaryBadge()
        {
            var sel = _adapters.Where(a => a.Selected).ToList();
            if (sel.Count == 0)
            {
                _lblSummary.Text = "Đã chọn: 0 / " + _adapters.Count + " card";
                _lblSummary.ForeColor = _clrTextMuted;
            }
            else
            {
                string range = sel.Count == 1 ? sel[0].NewIP : (sel[0].NewIP + " -> " + sel[sel.Count - 1].NewIP);
                _lblSummary.Text = "Đã chọn: " + sel.Count + " / " + _adapters.Count + " card  |  Dải IP: " + range;
                _lblSummary.ForeColor = _clrPrimary;
            }
        }

        private void UpdateInputStates()
        {
            _txtNamePrefix.Enabled = _chkRename.Checked;
            _btnNextPrefix.Enabled = _chkRename.Checked;
            _txtDns1.Enabled = _chkSetDns.Checked;
            _txtDns2.Enabled = _chkSetDns.Checked;
            _btnResetDns.Enabled = _chkSetDns.Checked;
        }

        private void SwapAdapterOrder(int direction)
        {
            if (_dgv.SelectedRows.Count == 0)
            {
                MessageBox.Show("Hãy chọn 1 dòng card mạng để di chuyển.", "Thông báo");
                return;
            }
            int idx = _dgv.SelectedRows[0].Index;
            int newIdx = idx + direction;
            if (newIdx < 0 || newIdx >= _adapters.Count) return;

            var tmp = _adapters[idx];
            _adapters[idx] = _adapters[newIdx];
            _adapters[newIdx] = tmp;

            RecalculatePreview();
            _dgv.ClearSelection();
            _dgv.Rows[newIdx].Selected = true;
            _dgv.CurrentCell = _dgv.Rows[newIdx].Cells[1];
        }

        private async System.Threading.Tasks.Task CheckIpConflictsAsync()
        {
            SyncGridEditsToItems();
            var selected = _adapters.Where(a => a.Selected).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Chưa chọn card mạng nào.", "Kiểm tra IP");
                return;
            }

            _btnCheckIp.Enabled = false;
            _btnCheckIp.Text = "Đang Ping...";
            try
            {
                var conflicts = await _net.CheckIpConflictsAsync(selected, (msg, lvl) => BeginInvoke(new Action(() => WriteLog(msg, lvl))));
                if (conflicts.Count > 0)
                    MessageBox.Show("Phát hiện xung đột / lỗi IP:\r\n\r\n" + string.Join("\r\n", conflicts), "Cảnh báo trùng IP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else
                    MessageBox.Show("Tất cả " + selected.Count + " IP dự kiến đều trống, không phát hiện thiết bị nào đang chiếm dụng.", "Kiểm tra IP thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            finally
            {
                _btnCheckIp.Enabled = true;
                _btnCheckIp.Text = "Check trùng IP";
            }
        }

        private void ApplyConfiguration()
        {
            SyncGridEditsToItems();
            var selected = _adapters.Where(a => a.Selected).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Vui lòng tick chọn ít nhất 1 card mạng.", "Thông báo");
                return;
            }

            var usedIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var it in selected)
            {
                string ip = (it.NewIP ?? "").Trim();
                if (!NetworkService.IsValidIPv4(ip))
                {
                    MessageBox.Show("IP không hợp lệ cho card '" + it.Name + "': " + ip, "Lỗi dữ liệu", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                if (!usedIps.Add(ip))
                {
                    MessageBox.Show("Có 2 card bị trùng IP dự kiến: " + ip, "Lỗi dữ liệu", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                if (_chkRename.Checked)
                {
                    string tName = (it.NewName ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(tName))
                    {
                        MessageBox.Show("Tên mới cho card '" + it.Name + "' không được để trống.", "Lỗi dữ liệu", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    if (!usedNames.Add(tName))
                    {
                        MessageBox.Show("Có 2 card bị trùng tên mới: " + tName, "Lỗi dữ liệu", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }
                if (!string.IsNullOrWhiteSpace(it.NewGateway) && !NetworkService.IsValidIPv4(it.NewGateway))
                {
                    MessageBox.Show("Gateway không hợp lệ cho card '" + it.Name + "': " + it.NewGateway, "Lỗi dữ liệu", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            var dnsServers = new List<string>();
            if (_chkSetDns.Checked)
            {
                foreach (string d in new[] { _txtDns1.Text.Trim(), _txtDns2.Text.Trim() })
                {
                    if (!string.IsNullOrWhiteSpace(d))
                    {
                        if (!NetworkService.IsValidIPv4(d))
                        {
                            MessageBox.Show("DNS không hợp lệ: " + d, "Lỗi dữ liệu", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }
                        dnsServers.Add(d);
                    }
                }
            }

            string summary = string.Join("\r\n", selected.Select(it =>
            {
                string gwStr = string.IsNullOrWhiteSpace(it.NewGateway) ? "Không Gateway" : ("Gateway " + it.NewGateway);
                string nmStr = _chkRename.Checked ? (" -> [" + it.NewName + "]") : "";
                string onStr = it.IsOnboard ? " [CARD ONBOARD]" : "";
                return " - " + it.Name + nmStr + " : " + it.NewIP + "/" + it.PrefixLength + " (" + gwStr + ")" + onStr;
            }));

            var optsList = new List<string>();
            if (_chkClearOld.Checked) optsList.Add("Xóa IP & Gateway cũ");
            if (_chkDisableNetbios.Checked) optsList.Add("Tắt NetBIOS");
            if (_chkDisableFileShare.Checked) optsList.Add("Tắt File & Printer Sharing");
            if (_chkDisableQoS.Checked) optsList.Add("Tắt QoS Packet Scheduler");
            if (_chkDisableIPv6.Checked) optsList.Add("Tắt IPv6");
            if (_chkDisablePowerSave.Checked) optsList.Add("Tắt tiết kiệm điện NIC");
            string optText = optsList.Count > 0 ? string.Join(", ", optsList) : "Không";
            string dnsSummary = dnsServers.Count > 0 ? string.Join(", ", dnsServers) : "Giữ nguyên";

            var confirm = MessageBox.Show(
                "Xác nhận áp dụng cho " + selected.Count + " card mạng:\r\n\r\n" + summary + "\r\n\r\nDNS: " + dnsSummary + "\r\nTùy chọn: " + optText,
                "Xác nhận cấu hình",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return;

            _btnApply.Enabled = false;
            _btnApply.Text = "ĐANG ÁP DỤNG...";
            Application.DoEvents();

            try
            {
                string backupFile = _net.SaveBackup(_backupDir, selected);
                WriteLog("Đã sao lưu cấu hình cũ vào: " + backupFile, "INFO");

                var options = new ApplyOptions
                {
                    RenameEnabled = _chkRename.Checked,
                    SetDns = _chkSetDns.Checked,
                    DnsServers = dnsServers,
                    ClearOldIpAndGw = _chkClearOld.Checked,
                    DisableNetbios = _chkDisableNetbios.Checked,
                    DisableFileSharing = _chkDisableFileShare.Checked,
                    DisableQoS = _chkDisableQoS.Checked,
                    DisableIPv6 = _chkDisableIPv6.Checked,
                    DisablePowerSave = _chkDisablePowerSave.Checked
                };

                var result = _net.ApplyConfiguration(_adapters, selected, options, WriteLog);

                var cfg = new AppConfig
                {
                    LastPrefix = _chkRename.Checked ? _txtNamePrefix.Text.Trim() : "",
                    SubnetBase = _txtSubnetBase.Text.Trim(),
                    SubnetMask = _cmbSubnetMask.Text,
                    StartOctet = (int)_numStartOctet.Value,
                    Direction = _rbDesc.Checked ? "Down" : "Up",
                    Gateway1 = _txtGw1.Text.Trim(),
                    Gateway2 = _txtGw2.Text.Trim(),
                    Dns1 = _txtDns1.Text.Trim(),
                    Dns2 = _txtDns2.Text.Trim()
                };
                _net.SaveConfig(_configPath, cfg);

                RefreshAdapters();
                if (_chkRename.Checked && result.okCount > 0)
                {
                    _txtNamePrefix.Text = GetNextPrefixSuggestion(cfg.LastPrefix, Array.Empty<string>());
                    RecalculatePreview();
                }

                string onboardMsg = result.onboardNotices.Count > 0
                    ? "\r\n\r\n[THÔNG BÁO CARD ONBOARD] (Bạn hãy tự đổi tên nếu cần):\r\n" + string.Join("\r\n", result.onboardNotices)
                    : "";

                MessageBox.Show(
                    "Hoàn tất! Thành công: " + result.okCount + " card | Lỗi: " + result.errCount + " card.\r\nFile sao lưu: " + backupFile + onboardMsg,
                    "Kết quả cấu hình",
                    MessageBoxButtons.OK,
                    result.errCount == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                WriteLog("LỖI TỔNG THỂ: " + ex.Message, "ERROR");
                MessageBox.Show("Có lỗi xảy ra: " + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _btnApply.Enabled = true;
                _btnApply.Text = "ÁP DỤNG";
            }
        }

        private void RollbackLastBackup()
        {
            string latest = Path.Combine(_backupDir, "latest_backup.json");
            if (!File.Exists(latest))
            {
                MessageBox.Show("Chưa có file sao lưu nào để khôi phục.", "Rollback");
                return;
            }
            if (MessageBox.Show("Khôi phục lại cấu hình trước đó từ:\r\n" + latest + " ?", "Xác nhận Rollback", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            try
            {
                _net.RollbackFromFile(latest, WriteLog);
                RefreshAdapters();
                MessageBox.Show("Đã khôi phục cấu hình trước đó!", "Rollback thành công");
            }
            catch (Exception ex)
            {
                WriteLog("Lỗi Rollback: " + ex.Message, "ERROR");
                MessageBox.Show("Lỗi Rollback: " + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async System.Threading.Tasks.Task BlinkSelectedAdapterAsync()
        {
            if (_dgv.SelectedRows.Count == 0)
            {
                MessageBox.Show("Hãy chọn 1 card mạng để nháy cổng (Tắt -> Bật lại).", "Thông báo");
                return;
            }
            var it = _adapters[_dgv.SelectedRows[0].Index];
            if (MessageBox.Show("Tắt card '" + it.Name + "' (" + it.Description + ") trong 2 giây rồi bật lại để nhận biết đèn cổng switch?", "Nháy cổng mạng", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            try
            {
                WriteLog("Đang nháy cổng '" + it.Name + "'...", "INFO");
                await _net.BlinkAdapterAsync(it.InterfaceIndex);
                RefreshAdapters();
                WriteLog("Đã bật lại '" + it.Name + "'.", "OK");
            }
            catch (Exception ex)
            {
                WriteLog("Lỗi nháy cổng '" + it.Name + "': " + ex.Message, "ERROR");
            }
        }

        private void WriteLog(string message, string level)
        {
            if (_txtLog == null || _txtLog.IsDisposed) return;
            _txtLog.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "][" + level + "] " + message + "\r\n");
            _txtLog.SelectionStart = _txtLog.TextLength;
            _txtLog.ScrollToCaret();
        }

        private static int GetPrefixLengthFromCombo(string text)
        {
            var m = Regex.Match(text ?? "", @"/(\d{1,2})");
            return m.Success && int.TryParse(m.Groups[1].Value, out int p) ? p : 24;
        }

        private static string ResolveGatewayForRow(string role, string gw1, string gw2)
        {
            switch (role)
            {
                case "Gateway 1": return (gw1 ?? "").Trim();
                case "Gateway 2": return (gw2 ?? "").Trim();
                default: return "";
            }
        }

        private static string GetNextPrefixSuggestion(string lastPrefix, string[] existingNames)
        {
            string used = lastPrefix;
            if (string.IsNullOrWhiteSpace(used))
            {
                foreach (string n in existingNames)
                {
                    var m = Regex.Match(n ?? "", @"^([a-zA-Z]+)\d{1,3}(?:on)?$");
                    if (m.Success) { used = m.Groups[1].Value.ToLowerInvariant(); break; }
                }
            }
            if (string.IsNullOrWhiteSpace(used)) return PrefixCycle[0];
            int idx = Array.IndexOf(PrefixCycle, used.ToLowerInvariant());
            return idx >= 0 ? PrefixCycle[(idx + 1) % PrefixCycle.Length] : PrefixCycle[0];
        }

        #region UI Factory Helpers

        private GroupBox CreateCard(string title)
        {
            return new GroupBox
            {
                Text = title,
                Size = new Size(420, 260),
                Dock = DockStyle.Fill,
                Margin = new Padding(4, 2, 4, 2),
                BackColor = _clrCardBg,
                ForeColor = Color.FromArgb(20, 45, 85),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
        }

        private Label MakeLabel(string text, int x, int y, Font font) => new Label
        {
            Text = text,
            Font = font,
            ForeColor = Color.Black,
            Location = new Point(x, y),
            AutoSize = true
        };

        private Label MakeMutedLabel(string text, int x, int y, Font font) => new Label
        {
            Text = text,
            Font = font,
            ForeColor = _clrTextMuted,
            Location = new Point(x, y),
            AutoSize = true
        };

        private CheckBox MakeOptionCheck(string text, int y, bool isChecked, Font font) => new CheckBox
        {
            Text = text,
            Font = font,
            ForeColor = Color.Black,
            Checked = isChecked,
            Location = new Point(12, y),
            AutoSize = true
        };

        private Button MakeFlatButton(string text, int x, int y, int w, int h, Font font, bool isPrimary = false)
        {
            var btn = new Button
            {
                Text = text,
                Font = font,
                Location = new Point(x, y),
                Size = new Size(w, h),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            if (isPrimary)
            {
                btn.BackColor = _clrPrimary;
                btn.ForeColor = Color.White;
                btn.FlatAppearance.BorderSize = 0;
                btn.FlatAppearance.MouseOverBackColor = _clrPrimaryHover;
            }
            else
            {
                btn.BackColor = _clrBtnBg;
                btn.ForeColor = Color.FromArgb(30, 36, 44);
                btn.FlatAppearance.BorderColor = _clrBorder;
                btn.FlatAppearance.BorderSize = 1;
                btn.FlatAppearance.MouseOverBackColor = _clrBtnHover;
            }
            return btn;
        }

        #endregion
    }
}
