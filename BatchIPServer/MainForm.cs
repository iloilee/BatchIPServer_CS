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
        private float _dpiScale = 1.0f;

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
        private Panel _pnlOnboard;
        private Label _lblSummary, _lblOnboardAlert;
        private SplitContainer _splitMain;
        private DataGridView _dgv;

        public MainForm()
        {
            InitializeComponent();
            using (var g = this.CreateGraphics())
            {
                _dpiScale = Math.Max(1.0f, g.DpiX / 96.0f);
            }

            var workArea = Screen.FromControl(this).WorkingArea;
            int targetW = Math.Min(D(1320), workArea.Width - 24);
            int targetH = Math.Min(D(790), workArea.Height - 24);
            this.Size = new Size(targetW, targetH);
            this.MinimumSize = new Size(Math.Min(D(1120), workArea.Width - 24), Math.Min(D(660), workArea.Height - 24));

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

        private int D(int px) => (int)Math.Round(px * _dpiScale);

        private void BuildUi()
        {
            this.Font = new Font("Segoe UI", 9F);
            this.BackColor = _clrBgMain;
            var regularFont = new Font("Segoe UI", 9F, FontStyle.Regular);

            // --- Top 3-Column Container (Auto-scaling height for DPI) ---
            var pnlTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = D(286),
                Padding = new Padding(D(10), D(8), D(10), D(4))
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

            // =========================================================================
            // Card 1: Dải IP & Sinh số thứ tự (Dùng TableLayoutPanel để không bao giờ đè chữ)
            // =========================================================================
            _gbIp = CreateCard("1. Dải IP && Sinh số thứ tự");
            tlpTop.Controls.Add(_gbIp, 0, 0);

            var tlpIp = MakeCardGrid(6);
            tlpIp.ColumnCount = 2;
            tlpIp.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tlpIp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int r = 0; r < 6; r++) tlpIp.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _gbIp.Controls.Add(tlpIp);

            // Row 0: Preset nhanh
            tlpIp.Controls.Add(MakeCellLabel("Preset nhanh:", regularFont), 0, 0);
            var pnlSubBtns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 2, 0, 4), Height = D(30) };
            pnlSubBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48F));
            pnlSubBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52F));
            var btnSub1 = MakeGridButton("192.168.1.", regularFont);
            var btnSub254 = MakeGridButton("192.168.254.", regularFont);
            btnSub1.Margin = new Padding(0, 0, D(4), 0);
            btnSub254.Margin = new Padding(D(2), 0, 0, 0);
            pnlSubBtns.Controls.Add(btnSub1, 0, 0);
            pnlSubBtns.Controls.Add(btnSub254, 1, 0);
            tlpIp.Controls.Add(pnlSubBtns, 1, 0);

            // Row 1: 3 octet đầu + Subnet Mask
            tlpIp.Controls.Add(MakeCellLabel("3 octet đầu:", regularFont), 0, 1);
            var pnlSubInputs = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 2, 0, 4), Height = D(28) };
            pnlSubInputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
            pnlSubInputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
            _txtSubnetBase = new TextBox { Text = _savedCfg.SubnetBase, Font = regularFont, Dock = DockStyle.Fill, Margin = new Padding(0, 1, D(4), 0) };
            _cmbSubnetMask = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = regularFont, Dock = DockStyle.Fill, Margin = new Padding(D(2), 1, 0, 0) };
            _cmbSubnetMask.Items.AddRange(new object[] { "255.255.255.0 (/24)", "255.255.0.0 (/16)", "255.255.255.128 (/25)", "255.255.255.192 (/26)" });
            _cmbSubnetMask.SelectedItem = _savedCfg.SubnetMask;
            if (_cmbSubnetMask.SelectedIndex < 0) _cmbSubnetMask.SelectedIndex = 0;
            pnlSubInputs.Controls.Add(_txtSubnetBase, 0, 0);
            pnlSubInputs.Controls.Add(_cmbSubnetMask, 1, 0);
            tlpIp.Controls.Add(pnlSubInputs, 1, 1);

            // Row 2: IP bắt đầu (.x) + Tăng/Giảm
            tlpIp.Controls.Add(MakeCellLabel("IP bắt đầu (.x):", regularFont), 0, 2);
            var flwStart = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 4) };
            _numStartOctet = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 254,
                Value = Math.Max(1, Math.Min(254, _savedCfg.StartOctet)),
                Font = regularFont,
                Width = D(62),
                Margin = new Padding(0, 1, D(8), 0)
            };
            _rbAsc = new RadioButton { Text = "Tăng (+1)", Font = regularFont, ForeColor = Color.Black, AutoSize = true, Margin = new Padding(0, 3, D(8), 0) };
            _rbDesc = new RadioButton { Text = "Giảm (-1)", Font = regularFont, ForeColor = Color.Black, AutoSize = true, Margin = new Padding(0, 3, 0, 0) };
            if (_savedCfg.Direction == "Down") _rbDesc.Checked = true; else _rbAsc.Checked = true;
            flwStart.Controls.AddRange(new Control[] { _numStartOctet, _rbAsc, _rbDesc });
            tlpIp.Controls.Add(flwStart, 1, 2);

            // Row 3: Preset .200 / .253 (chiếm 2 cột)
            var pnlSeqBtns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 4, 0, 6), Height = D(30) };
            pnlSeqBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            pnlSeqBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            var btnSeq200 = MakeGridButton("Preset: .200, .201, .202...", regularFont);
            var btnSeq253 = MakeGridButton("Preset: .253, .252, .251...", regularFont);
            btnSeq200.Margin = new Padding(0, 0, D(4), 0);
            btnSeq253.Margin = new Padding(D(2), 0, 0, 0);
            pnlSeqBtns.Controls.Add(btnSeq200, 0, 0);
            pnlSeqBtns.Controls.Add(btnSeq253, 1, 0);
            tlpIp.Controls.Add(pnlSeqBtns, 0, 3);
            tlpIp.SetColumnSpan(pnlSeqBtns, 2);

            // Row 4: Đổi tên card theo IP
            _chkRename = new CheckBox
            {
                Text = "Đổi tên card theo IP (VD: z200, z201, z202...)",
                Font = regularFont,
                ForeColor = Color.Black,
                Checked = true,
                AutoSize = true,
                Margin = new Padding(0, D(6), 0, D(4))
            };
            tlpIp.Controls.Add(_chkRename, 0, 4);
            tlpIp.SetColumnSpan(_chkRename, 2);

            // Row 5: Ký tự tiền tố + Xoay ký tự
            tlpIp.Controls.Add(MakeCellLabel("Ký tự tiền tố:", regularFont), 0, 5);
            var flwPrefix = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 0) };
            _txtNamePrefix = new TextBox { Font = regularFont, Width = D(58), Margin = new Padding(0, 2, D(6), 0) };
            _btnNextPrefix = MakeFlatButton("Xoay ký tự", 0, 0, D(98), D(27), regularFont);
            _btnNextPrefix.Margin = new Padding(0);
            flwPrefix.Controls.AddRange(new Control[] { _txtNamePrefix, _btnNextPrefix });
            tlpIp.Controls.Add(flwPrefix, 1, 5);

            // =========================================================================
            // Card 2: Gateway & DNS
            // =========================================================================
            _gbNet = CreateCard("2. Gateway (1 hoặc 2 Gateway) && DNS");
            tlpTop.Controls.Add(_gbNet, 1, 0);

            var tlpNet = MakeCardGrid(6);
            tlpNet.ColumnCount = 2;
            tlpNet.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tlpNet.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int r = 0; r < 6; r++) tlpNet.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _gbNet.Controls.Add(tlpNet);

            // Row 0: Chế độ Gateway
            tlpNet.Controls.Add(MakeCellLabel("Chế độ:", regularFont), 0, 0);
            _cmbGwMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = regularFont, Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 4) };
            _cmbGwMode.Items.AddRange(new object[]
            {
                "Chung 1 Gateway (Tất cả dùng Gateway 1)",
                "Chỉ card đầu dùng Gateway 1 (Còn lại trống)",
                "2 Gateway - Chia nửa (Đầu Gateway 1 / Sau Gateway 2)",
                "2 Gateway - Xen kẽ (Lẻ Gateway 1 / Chẵn Gateway 2)",
                "Tùy chỉnh từng card trên bảng"
            });
            _cmbGwMode.SelectedIndex = 0;
            tlpNet.Controls.Add(_cmbGwMode, 1, 0);

            // Row 1: Gateway 1
            tlpNet.Controls.Add(MakeCellLabel("Gateway 1:", regularFont), 0, 1);
            var pnlGw1 = MakeTwoColRow(52F, 48F, D(28));
            _txtGw1 = new TextBox { Text = _savedCfg.Gateway1, Font = regularFont, Dock = DockStyle.Fill, Margin = new Padding(0, 2, D(6), 0) };
            pnlGw1.Controls.Add(_txtGw1, 0, 0);
            pnlGw1.Controls.Add(MakeInlineMuted("(Gateway chính)", regularFont), 1, 0);
            tlpNet.Controls.Add(pnlGw1, 1, 1);

            // Row 2: Gateway 2
            tlpNet.Controls.Add(MakeCellLabel("Gateway 2:", regularFont), 0, 2);
            var pnlGw2 = MakeTwoColRow(52F, 48F, D(28));
            _txtGw2 = new TextBox { Text = _savedCfg.Gateway2, Font = regularFont, Dock = DockStyle.Fill, Margin = new Padding(0, 2, D(6), 0) };
            pnlGw2.Controls.Add(_txtGw2, 0, 0);
            pnlGw2.Controls.Add(MakeInlineMuted("(Trống nếu chỉ 1 GW)", regularFont), 1, 0);
            tlpNet.Controls.Add(pnlGw2, 1, 2);

            // Row 3: Checkbox DNS
            _chkSetDns = new CheckBox
            {
                Text = "Cấu hình DNS cho các card được chọn",
                Font = regularFont,
                ForeColor = Color.Black,
                Checked = true,
                AutoSize = true,
                Margin = new Padding(0, D(8), 0, D(4))
            };
            tlpNet.Controls.Add(_chkSetDns, 0, 3);
            tlpNet.SetColumnSpan(_chkSetDns, 2);

            // Row 4: DNS Chính + Google DNS
            tlpNet.Controls.Add(MakeCellLabel("DNS Chính:", regularFont), 0, 4);
            var pnlDns1 = MakeTwoColRow(52F, 48F, D(29));
            _txtDns1 = new TextBox { Text = _savedCfg.Dns1, Font = regularFont, Dock = DockStyle.Fill, Margin = new Padding(0, 2, D(6), 0) };
            _btnResetDns = MakeGridButton("Google DNS", regularFont);
            _btnResetDns.Margin = new Padding(0, 0, 0, D(2));
            pnlDns1.Controls.Add(_txtDns1, 0, 0);
            pnlDns1.Controls.Add(_btnResetDns, 1, 0);
            tlpNet.Controls.Add(pnlDns1, 1, 4);

            // Row 5: DNS Phụ
            tlpNet.Controls.Add(MakeCellLabel("DNS Phụ:", regularFont), 0, 5);
            var pnlDns2 = MakeTwoColRow(52F, 48F, D(28));
            _txtDns2 = new TextBox { Text = _savedCfg.Dns2, Font = regularFont, Dock = DockStyle.Fill, Margin = new Padding(0, 2, D(6), 0) };
            pnlDns2.Controls.Add(_txtDns2, 0, 0);
            tlpNet.Controls.Add(pnlDns2, 1, 5);

            // =========================================================================
            // Card 3: Tối ưu Server & Thao tác
            // =========================================================================
            _gbOpt = CreateCard("3. Tối ưu Server && Thao tác");
            tlpTop.Controls.Add(_gbOpt, 2, 0);

            var tlpOpt = MakeCardGrid(9);
            tlpOpt.ColumnCount = 1;
            tlpOpt.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int r = 0; r < 7; r++) tlpOpt.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tlpOpt.RowStyles.Add(new RowStyle(SizeType.Absolute, D(34)));
            tlpOpt.RowStyles.Add(new RowStyle(SizeType.Absolute, D(38)));
            _gbOpt.Controls.Add(tlpOpt);

            _chkClearOld = MakeFlowOptionCheck("Xóa sạch IP && Gateway cũ trước khi gán", true, regularFont);
            _chkDisableNetbios = MakeFlowOptionCheck("Tắt NetBIOS over TCP/IP", true, regularFont);
            _chkDisableFileShare = MakeFlowOptionCheck("Tắt File and Printer Sharing for Microsoft Networks", true, regularFont);
            _chkDisableQoS = MakeFlowOptionCheck("Tắt QoS Packet Scheduler", true, regularFont);
            _chkDisableIPv6 = MakeFlowOptionCheck("Tắt IPv6 (Internet Protocol Version 6)", true, regularFont);
            _chkDisablePowerSave = MakeFlowOptionCheck("Tắt tiết kiệm điện NIC (Power Saving / EEE)", false, regularFont);
            _chkShowVirtual = MakeFlowOptionCheck("Hiện cả card ảo (VMware, Hyper-V, VPN...)", false, regularFont);

            tlpOpt.Controls.Add(_chkClearOld, 0, 0);
            tlpOpt.Controls.Add(_chkDisableNetbios, 0, 1);
            tlpOpt.Controls.Add(_chkDisableFileShare, 0, 2);
            tlpOpt.Controls.Add(_chkDisableQoS, 0, 3);
            tlpOpt.Controls.Add(_chkDisableIPv6, 0, 4);
            tlpOpt.Controls.Add(_chkDisablePowerSave, 0, 5);
            tlpOpt.Controls.Add(_chkShowVirtual, 0, 6);

            var pnlActionRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, D(4), 0, D(4)) };
            pnlActionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32F));
            pnlActionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
            pnlActionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));

            _btnCheckIp = MakeGridButton("Check trùng IP", regularFont);
            _btnRollback = MakeGridButton("Khôi phục (Rollback)", regularFont);
            _btnRefresh = MakeGridButton("Tải lại NIC", regularFont);
            _btnCheckIp.Margin = new Padding(0, 0, D(4), 0);
            _btnRollback.Margin = new Padding(D(2), 0, D(2), 0);
            _btnRefresh.Margin = new Padding(D(4), 0, 0, 0);

            pnlActionRow.Controls.Add(_btnCheckIp, 0, 0);
            pnlActionRow.Controls.Add(_btnRollback, 1, 0);
            pnlActionRow.Controls.Add(_btnRefresh, 2, 0);
            tlpOpt.Controls.Add(pnlActionRow, 0, 7);

            _btnApply = MakeGridButton("ÁP DỤNG", new Font("Segoe UI", 10F, FontStyle.Bold), true);
            _btnApply.Margin = new Padding(0, D(2), 0, 0);
            tlpOpt.Controls.Add(_btnApply, 0, 8);

            // =========================================================================
            // Middle Toolbar (FlowLayout + Right Summary Label)
            // =========================================================================
            var pnlToolbar = new Panel
            {
                Dock = DockStyle.Top,
                Height = D(40),
                Padding = new Padding(D(12), D(4), D(14), D(4))
            };

            var tlpToolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            tlpToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tlpToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            pnlToolbar.Controls.Add(tlpToolbar);

            var flwToolbarBtns = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            var btnSelectAll = MakeAutoToolbarButton("Chọn tất cả", regularFont);
            var btnSelectUp = MakeAutoToolbarButton("Chỉ chọn card Up", regularFont);
            var btnUnselectAll = MakeAutoToolbarButton("Bỏ chọn hết", regularFont);
            var btnMoveUp = MakeAutoToolbarButton("Lên thứ tự", regularFont);
            var btnMoveDown = MakeAutoToolbarButton("Xuống thứ tự", regularFont);
            var btnBlink = MakeAutoToolbarButton("Nháy cổng NIC", regularFont);
            var btnOpenNcpa = MakeAutoToolbarButton("Mở Network Connections", regularFont);
            flwToolbarBtns.Controls.AddRange(new Control[]
            {
                btnSelectAll, btnSelectUp, btnUnselectAll, btnMoveUp, btnMoveDown, btnBlink, btnOpenNcpa
            });
            tlpToolbar.Controls.Add(flwToolbarBtns, 0, 0);

            _lblSummary = new Label
            {
                Text = "Đã chọn: 0 / 0 card",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = _clrPrimary,
                TextAlign = ContentAlignment.MiddleRight,
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(D(8), 0, 0, 0)
            };
            tlpToolbar.Controls.Add(_lblSummary, 1, 0);

            // --- Onboard Notification Banner ---
            _pnlOnboard = new Panel
            {
                Dock = DockStyle.Top,
                Height = D(34),
                Padding = new Padding(D(16), D(6), D(16), D(4)),
                BackColor = Color.FromArgb(255, 244, 206)
            };
            _lblOnboardAlert = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(133, 77, 14),
                Text = "Đang quét card Onboard..."
            };
            _pnlOnboard.Controls.Add(_lblOnboardAlert);

            // --- Workspace SplitContainer ---
            var pnlWorkspace = new Panel { Dock = DockStyle.Fill, Padding = new Padding(D(14), D(6), D(14), D(10)) };
            _splitMain = new SplitContainer
            {
                Size = new Size(D(1260), D(420)),
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = D(6),
                Panel1MinSize = D(110),
                Panel2MinSize = D(85),
                SplitterDistance = D(250)
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
                ColumnHeadersHeight = D(34),
                ShowCellToolTips = true
            };
            _dgv.RowTemplate.Height = D(28);
            _dgv.DefaultCellStyle.SelectionBackColor = Color.FromArgb(214, 232, 252);
            _dgv.DefaultCellStyle.SelectionForeColor = Color.FromArgb(18, 28, 42);
            _dgv.ColumnHeadersDefaultCellStyle.BackColor = _clrHeaderBg;
            _dgv.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(25, 38, 56);
            _dgv.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);

            _dgv.Columns.Add(new DataGridViewCheckBoxColumn { Name = "colSelect", HeaderText = "Chọn", Width = D(50) });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "colCurrName", HeaderText = "Tên hiện tại", Width = D(95), ReadOnly = true });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "colType", HeaderText = "Loại card", Width = D(84), ReadOnly = true });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "colInfo", HeaderText = "Trạng thái · Tốc độ", Width = D(135), ReadOnly = true });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "colDesc", HeaderText = "Phần cứng NIC", MinimumWidth = D(170), ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "colCurrIp", HeaderText = "IP hiện tại", Width = D(138), ReadOnly = true });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "colNewName", HeaderText = "Tên mới", Width = D(82) });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "colNewIP", HeaderText = "IP mới", Width = D(118) });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "colPrefix", HeaderText = "Prefix", Width = D(56) });

            var colGwRole = new DataGridViewComboBoxColumn { Name = "colGwRole", HeaderText = "Chọn Gateway", Width = D(108) };
            colGwRole.Items.AddRange("Gateway 1", "Gateway 2", "Không");
            _dgv.Columns.Add(colGwRole);

            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "colNewGW", HeaderText = "Gateway mới", Width = D(114), ReadOnly = true });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "colMetric", HeaderText = "Metric", Width = D(72) });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "colNewDNS", HeaderText = "DNS mới", Width = D(116), ReadOnly = true });
            _splitMain.Panel1.Controls.Add(_dgv);

            // Log Header + Box
            var pnlLogBar = new Panel { Dock = DockStyle.Top, Height = D(30), BackColor = Color.FromArgb(34, 38, 46), Padding = new Padding(D(8), D(3), D(6), D(3)) };
            var tlpLogBar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
            tlpLogBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tlpLogBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tlpLogBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pnlLogBar.Controls.Add(tlpLogBar);

            tlpLogBar.Controls.Add(new Label
            {
                Text = "Nhật ký thao tác (Log)  —  Mẹo: Sửa trực tiếp [Tên mới], [IP mới], [Chọn Gateway], [Metric] trên bảng phía trên",
                ForeColor = Color.FromArgb(200, 210, 224),
                Font = new Font("Segoe UI", 8.5F),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Dock = DockStyle.Fill
            }, 0, 0);

            var btnOpenBackup = MakeAutoToolbarButton("Mở thư mục Backup", new Font("Segoe UI", 8F));
            var btnClearLog = MakeAutoToolbarButton("Xóa Log", new Font("Segoe UI", 8F));
            btnOpenBackup.Margin = new Padding(D(4), 0, D(4), 0);
            btnClearLog.Margin = new Padding(0);
            tlpLogBar.Controls.Add(btnOpenBackup, 1, 0);
            tlpLogBar.Controls.Add(btnClearLog, 2, 0);

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
            this.Controls.Add(pnlToolbar);
            this.Controls.Add(pnlTop);

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
                int desiredLogH = D(155);
                if (_splitMain.Height > _splitMain.Panel1MinSize + _splitMain.Panel2MinSize + D(12))
                    _splitMain.SplitterDistance = Math.Max(_splitMain.Panel1MinSize, _splitMain.Height - desiredLogH);

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
                Dock = DockStyle.Fill,
                Margin = new Padding(D(4), D(2), D(4), D(2)),
                Padding = new Padding(D(8), D(4), D(8), D(6)),
                BackColor = _clrCardBg,
                ForeColor = Color.FromArgb(20, 45, 85),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
        }

        private TableLayoutPanel MakeCardGrid(int rowCount)
        {
            return new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = rowCount,
                Padding = new Padding(D(2), D(4), D(2), D(2))
            };
        }

        private TableLayoutPanel MakeTwoColRow(float p1, float p2, int height)
        {
            var p = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 1, 0, D(3)), Height = height };
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, p1));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, p2));
            return p;
        }

        private Label MakeCellLabel(string text, Font font) => new Label
        {
            Text = text,
            Font = font,
            ForeColor = Color.Black,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, D(5), D(8), D(5))
        };

        private Label MakeInlineMuted(string text, Font font) => new Label
        {
            Text = text,
            Font = font,
            ForeColor = _clrTextMuted,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };

        private CheckBox MakeFlowOptionCheck(string text, bool isChecked, Font font) => new CheckBox
        {
            Text = text,
            Font = font,
            ForeColor = Color.Black,
            Checked = isChecked,
            AutoSize = true,
            Margin = new Padding(0, D(1), 0, D(2))
        };

        private Button MakeGridButton(string text, Font font, bool isPrimary = false)
        {
            var btn = new Button
            {
                Text = text,
                Font = font,
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Margin = new Padding(0)
            };
            ApplyButtonStyle(btn, isPrimary);
            return btn;
        }

        private Button MakeAutoToolbarButton(string text, Font font)
        {
            var btn = new Button
            {
                Text = text,
                Font = font,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(D(6), D(2), D(6), D(2)),
                MinimumSize = new Size(0, D(26)),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, D(6), 0)
            };
            ApplyButtonStyle(btn, false);
            return btn;
        }

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
            ApplyButtonStyle(btn, isPrimary);
            return btn;
        }

        private void ApplyButtonStyle(Button btn, bool isPrimary)
        {
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
        }

        #endregion
    }
}
