using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace BatchIPServer
{
    public sealed class NetworkService
    {
        private const string CimScopePath = @"\\.\root\StandardCimv2";
        private const string CimV2ScopePath = @"\\.\root\CIMV2";

        private static readonly Regex VirtualPattern = new Regex(
            @"VMware|VirtualBox|Hyper-V|vEthernet|Loopback|Teredo|Bluetooth|TAP-|TUN|WireGuard|ZeroTier|Radmin|Hamachi|ISATAP|6to4|WAN Miniport|Microsoft Kernel|Virtual Adapter|Npcap",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex OnboardChipPattern = new Regex(
            @"I219|I211|I217|I218|I225|I226|82579|82574|Realtek PCIe GbE|Realtek Gaming|RTL8111|RTL8168|RTL8125|Killer E|Onboard|LOM",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex PowerKeywordPattern = new Regex(
            @"EEE|GreenEthernet|PowerSaving|ULPMode",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public AppConfig LoadConfig(string configPath)
        {
            var def = new AppConfig();
            if (!File.Exists(configPath)) return def;
            try
            {
                string raw = File.ReadAllText(configPath, Encoding.UTF8);
                var loaded = _json.Deserialize<AppConfig>(raw);
                if (loaded == null) return def;
                if (string.IsNullOrEmpty(loaded.SubnetBase)) loaded.SubnetBase = def.SubnetBase;
                if (string.IsNullOrEmpty(loaded.SubnetMask)) loaded.SubnetMask = def.SubnetMask;
                if (loaded.StartOctet < 1 || loaded.StartOctet > 254) loaded.StartOctet = def.StartOctet;
                if (string.IsNullOrEmpty(loaded.Direction)) loaded.Direction = def.Direction;
                if (loaded.Gateway1 == null) loaded.Gateway1 = def.Gateway1;
                if (loaded.Gateway2 == null) loaded.Gateway2 = "";
                if (loaded.Dns1 == null) loaded.Dns1 = def.Dns1;
                if (loaded.Dns2 == null) loaded.Dns2 = def.Dns2;
                return loaded;
            }
            catch
            {
                return def;
            }
        }

        public void SaveConfig(string configPath, AppConfig cfg)
        {
            string json = _json.Serialize(cfg);
            File.WriteAllText(configPath, json, new UTF8Encoding(true));
        }

        public List<AdapterItem> LoadAdapters(bool includeVirtual)
        {
            var list = new List<AdapterItem>();

            var hwBusMap = LoadHardwareBusMap();
            var pnpMap = LoadPnpDeviceMap();
            var bindingMap = LoadBindingsMap();
            var nicInfoMap = LoadManagedIpMap();

            var scope = new ManagementScope(CimScopePath);
            scope.Connect();

            using (var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(
                "SELECT Name, InterfaceIndex, InterfaceGuid, InterfaceDescription, Speed, PermanentAddress, InterfaceOperationalStatus, MediaConnectState, Virtual FROM MSFT_NetAdapter")))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    using (obj)
                    {
                        string name = GetStr(obj, "Name");
                        string desc = GetStr(obj, "InterfaceDescription");
                        bool isVirtProp = GetBool(obj, "Virtual");
                        bool isVirt = isVirtProp || VirtualPattern.IsMatch(desc) || VirtualPattern.IsMatch(name);
                        if (!includeVirtual && isVirt) continue;

                        int ifIndex = GetInt(obj, "InterfaceIndex", 0);
                        if (ifIndex <= 0) continue;

                        string guid = GetStr(obj, "InterfaceGuid");
                        long speed = GetLong(obj, "Speed", 0);
                        uint opStatus = GetUInt(obj, "InterfaceOperationalStatus", 2);
                        uint mediaState = GetUInt(obj, "MediaConnectState", 2);
                        string statusText = FormatOperationalStatus(opStatus, mediaState);

                        string mac = FormatMac(GetStr(obj, "PermanentAddress"));
                        int busNum = hwBusMap.TryGetValue(name, out int b) ? b : 999;
                        string pnpId = pnpMap.TryGetValue(ifIndex, out string pnp) ? pnp : "";

                        bool isOnboard = DetectIsOnboard(desc, busNum, pnpId);

                        var currentIPs = new List<string>();
                        var currentIPTexts = new List<string>();
                        int currentPrefix = 24;
                        var currentGWs = new List<string>();
                        var currentDNS = new List<string>();

                        if (nicInfoMap.TryGetValue(ifIndex, out NicIpSnapshot snap))
                        {
                            currentIPs.AddRange(snap.IPs);
                            currentIPTexts.AddRange(snap.IPTexts);
                            currentPrefix = snap.FirstPrefix;
                            currentGWs.AddRange(snap.Gateways);
                            currentDNS.AddRange(snap.DnsServers);
                            if (string.IsNullOrEmpty(mac) && !string.IsNullOrEmpty(snap.Mac))
                                mac = snap.Mac;
                        }
                        else
                        {
                            // Fallback đọc qua CIM MSFT_NetIPAddress / Route / DNS khi card Disconnected
                            QueryDisconnectedIpState(scope, ifIndex, currentIPs, currentIPTexts, ref currentPrefix, currentGWs, currentDNS);
                        }

                        int nbOpt = GetNetbiosSetting(ifIndex, guid);
                        bool ipv6On = GetBindingEnabled(bindingMap, name, "ms_tcpip6", true);
                        bool fileShareOn = GetBindingEnabled(bindingMap, name, "ms_server", true);
                        bool qosOn = GetBindingEnabled(bindingMap, name, "ms_pacer", true);

                        list.Add(new AdapterItem
                        {
                            Selected = statusText == "Up",
                            InterfaceIndex = ifIndex,
                            InterfaceGuid = guid,
                            Name = name,
                            Description = desc,
                            Status = statusText,
                            SpeedBps = speed,
                            LinkSpeedText = FormatLinkSpeed(speed),
                            MacAddress = mac,
                            IsOnboard = isOnboard,
                            IsVirtual = isVirt,
                            CurrentIPs = currentIPs,
                            CurrentPrefix = currentPrefix,
                            CurrentIPText = currentIPTexts.Count > 0 ? string.Join(", ", currentIPTexts) : "DHCP / Chưa đặt",
                            CurrentGWs = currentGWs,
                            CurrentDNS = currentDNS,
                            NetbiosOption = nbOpt,
                            IPv6Enabled = ipv6On,
                            FileShareEnabled = fileShareOn,
                            QoSEnabled = qosOn,
                            BusNumber = busNum
                        });
                    }
                }
            }

            return list
                .OrderBy(a => a.Status == "Up" ? 0 : 1)
                .ThenByDescending(a => a.SpeedBps)
                .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public string SaveBackup(string backupDir, IList<AdapterItem> targets)
        {
            if (!Directory.Exists(backupDir))
                Directory.CreateDirectory(backupDir);

            var snap = targets.Select(it => new AdapterBackupItem
            {
                InterfaceIndex = it.InterfaceIndex,
                InterfaceGuid = it.InterfaceGuid,
                MacAddress = it.MacAddress,
                OriginalName = it.Name,
                IPs = new List<string>(it.CurrentIPs),
                PrefixLength = it.CurrentPrefix > 0 ? it.CurrentPrefix : 24,
                Gateways = new List<string>(it.CurrentGWs),
                DNS = new List<string>(it.CurrentDNS),
                NetbiosOption = it.NetbiosOption,
                IPv6Enabled = it.IPv6Enabled,
                FileShareEnabled = it.FileShareEnabled,
                QoSEnabled = it.QoSEnabled
            }).ToList();

            string json = _json.Serialize(snap);
            string file = Path.Combine(backupDir, "backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json");
            string latest = Path.Combine(backupDir, "latest_backup.json");
            File.WriteAllText(file, json, new UTF8Encoding(true));
            File.WriteAllText(latest, json, new UTF8Encoding(true));
            return file;
        }

        public async Task<List<string>> CheckIpConflictsAsync(IList<AdapterItem> selected, Action<string, string> log)
        {
            var conflicts = new List<string>();
            var localIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                        localIps.Add(ua.Address.ToString());
                }
            }

            foreach (var it in selected)
            {
                string ip = (it.NewIP ?? "").Trim();
                if (!IsValidIPv4(ip))
                {
                    conflicts.Add(it.Name + ": IP không hợp lệ (" + ip + ")");
                    continue;
                }
                if (localIps.Contains(ip))
                {
                    log("IP " + ip + " đang nằm trên chính máy này -> Bỏ qua cảnh báo trùng.", "INFO");
                    continue;
                }

                using (var ping = new Ping())
                {
                    try
                    {
                        var reply = await ping.SendPingAsync(ip, 250).ConfigureAwait(false);
                        if (reply.Status == IPStatus.Success)
                        {
                            conflicts.Add(ip + " (dự định cho " + it.Name + ") ĐÃ CÓ THIẾT BỊ PHẢN HỒI (" + reply.RoundtripTime + "ms)");
                            log("TRÙNG IP: " + ip + " đã có thiết bị khác sử dụng!", "WARN");
                        }
                        else
                        {
                            log("IP " + ip + " trống (OK).", "INFO");
                        }
                    }
                    catch (Exception ex)
                    {
                        log("Lỗi ping " + ip + ": " + ex.Message, "WARN");
                    }
                }
            }

            return conflicts;
        }

        public (int okCount, int errCount, List<string> onboardNotices) ApplyConfiguration(
            IList<AdapterItem> allAdapters,
            IList<AdapterItem> selected,
            ApplyOptions options,
            Action<string, string> log)
        {
            var scope = new ManagementScope(CimScopePath);
            scope.Connect();

            // Phase 1: Đổi sang tên tạm nếu có Rename
            if (options.RenameEnabled)
            {
                var unselectedNames = new HashSet<string>(
                    allAdapters.Where(a => !a.Selected).Select(a => (a.Name ?? "").Trim().ToLowerInvariant()));

                foreach (var it in selected)
                {
                    string target = (it.NewName ?? "").Trim();
                    if (unselectedNames.Contains(target.ToLowerInvariant()))
                        throw new InvalidOperationException("Tên '" + target + "' đang bị card không được chọn sử dụng. Hãy đổi ký tự tiền tố khác!");
                }

                foreach (var it in selected)
                {
                    string tmpName = "__tmp_nic_" + it.InterfaceIndex;
                    string currName = GetAdapterNameByIndex(scope, it.InterfaceIndex);
                    if (!string.IsNullOrEmpty(currName) && currName != tmpName)
                        RenameAdapter(scope, it.InterfaceIndex, currName, tmpName);
                }
            }

            int okCount = 0;
            int errCount = 0;
            var onboardNotices = new List<string>();

            // Phase 2: Gán IP, Gateway, DNS & Tối ưu
            foreach (var it in selected)
            {
                int idx = it.InterfaceIndex;
                try
                {
                    string currName = GetAdapterNameByIndex(scope, idx);
                    if (string.IsNullOrEmpty(currName))
                        throw new InvalidOperationException("Không tìm thấy card mạng #" + idx);

                    SetDhcpMode(scope, idx, false);

                    if (options.ClearOldIpAndGw)
                        ClearExistingIpAndRoutes(scope, idx);

                    string gw = string.IsNullOrWhiteSpace(it.NewGateway) ? null : it.NewGateway.Trim();
                    CreateNewIPv4Address(scope, idx, currName, it.NewIP.Trim(), (byte)it.PrefixLength, gw);

                    if (int.TryParse((it.Metric ?? "").Trim(), out int metricVal) && metricVal > 0)
                        SetInterfaceMetric(scope, idx, false, (uint)metricVal);
                    else
                        SetInterfaceMetric(scope, idx, true, 0);

                    if (options.SetDns)
                        SetDnsServers(scope, idx, currName, options.DnsServers);

                    if (options.DisableNetbios)
                    {
                        bool nbOk = SetNetbiosDisabled(idx, it.InterfaceGuid, true);
                        log("  -> Đã tắt NetBIOS trên card #" + idx + " (CIM=" + nbOk + ")", "INFO");
                    }

                    if (options.DisableFileSharing)
                        SetAdapterBinding(scope, currName, "ms_server", false, "File and Printer Sharing", log);

                    if (options.DisableQoS)
                        SetAdapterBinding(scope, currName, "ms_pacer", false, "QoS Packet Scheduler", log);

                    if (options.DisableIPv6)
                        SetAdapterBinding(scope, currName, "ms_tcpip6", false, "IPv6", log);

                    if (options.DisablePowerSave)
                        DisablePowerManagement(scope, currName);

                    string finalDisplayName = currName;
                    if (options.RenameEnabled)
                    {
                        string targetName = it.NewName.Trim();
                        if (!string.Equals(currName, targetName, StringComparison.Ordinal))
                            RenameAdapter(scope, idx, currName, targetName);
                        finalDisplayName = targetName;
                    }
                    else
                    {
                        finalDisplayName = it.Name;
                    }

                    if (it.IsOnboard)
                        onboardNotices.Add(" - Card Onboard: " + finalDisplayName + " | IP: " + it.NewIP + " | " + it.Description);

                    log("OK: [" + finalDisplayName + "] -> " + it.NewIP + "/" + it.PrefixLength + " | Gateway: " + (gw ?? "Không"), "OK");
                    okCount++;
                }
                catch (Exception ex)
                {
                    errCount++;
                    log("LỖI card '" + it.Name + "' (#" + idx + "): " + ex.Message, "ERROR");
                }
            }

            return (okCount, errCount, onboardNotices);
        }

        public void RollbackFromFile(string backupFilePath, Action<string, string> log)
        {
            string raw = File.ReadAllText(backupFilePath, Encoding.UTF8);
            var items = _json.Deserialize<List<AdapterBackupItem>>(raw);
            if (items == null || items.Count == 0)
                throw new InvalidOperationException("File sao lưu rỗng, không có dữ liệu để khôi phục.");

            var scope = new ManagementScope(CimScopePath);
            scope.Connect();

            foreach (var b in items)
            {
                string curr = GetAdapterNameByIndex(scope, b.InterfaceIndex);
                if (!string.IsNullOrEmpty(curr) && curr != b.OriginalName)
                    RenameAdapter(scope, b.InterfaceIndex, curr, "__rb_tmp_" + b.InterfaceIndex);
            }

            foreach (var b in items)
            {
                int idx = b.InterfaceIndex;
                string curr = GetAdapterNameByIndex(scope, idx);
                if (string.IsNullOrEmpty(curr)) continue;

                ClearExistingIpAndRoutes(scope, idx);

                var ipList = (b.IPs ?? new List<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                var gwList = (b.Gateways ?? new List<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                var dnsList = (b.DNS ?? new List<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

                if (ipList.Count > 0)
                {
                    SetDhcpMode(scope, idx, false);
                    byte pLen = (byte)(b.PrefixLength >= 1 && b.PrefixLength <= 32 ? b.PrefixLength : 24);
                    string gw = gwList.Count > 0 ? gwList[0] : null;
                    CreateNewIPv4Address(scope, idx, curr, ipList[0], pLen, gw);
                }
                else
                {
                    SetDhcpMode(scope, idx, true);
                }

                SetDnsServers(scope, idx, curr, dnsList);
                SetNetbiosDisabled(idx, b.InterfaceGuid, b.NetbiosOption == 2);

                SetAdapterBinding(scope, curr, "ms_tcpip6", b.IPv6Enabled, "", log);
                SetAdapterBinding(scope, curr, "ms_server", b.FileShareEnabled, "", log);
                SetAdapterBinding(scope, curr, "ms_pacer", b.QoSEnabled, "", log);

                if (!string.IsNullOrEmpty(b.OriginalName) && curr != b.OriginalName)
                    RenameAdapter(scope, idx, curr, b.OriginalName);

                log("Rollback xong card #" + idx + " -> " + b.OriginalName, "OK");
            }
        }

        public async Task BlinkAdapterAsync(int interfaceIndex)
        {
            var scope = new ManagementScope(CimScopePath);
            scope.Connect();
            InvokeAdapterMethod(scope, interfaceIndex, "Disable");
            await Task.Delay(2000).ConfigureAwait(false);
            InvokeAdapterMethod(scope, interfaceIndex, "Enable");
            await Task.Delay(2000).ConfigureAwait(false);
        }

        public static bool IsValidIPv4(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return false;
            string trimmed = ip.Trim();
            if (!Regex.IsMatch(trimmed, @"^(\d{1,3}\.){3}\d{1,3}$")) return false;
            return IPAddress.TryParse(trimmed, out IPAddress addr) && addr.AddressFamily == AddressFamily.InterNetwork;
        }

        public static string NormalizeSubnetPrefix(string inputBase)
        {
            if (string.IsNullOrWhiteSpace(inputBase)) return null;
            string[] parts = inputBase.Trim().TrimEnd('.').Split('.');
            if (parts.Length != 3) return null;
            foreach (string p in parts)
            {
                if (!int.TryParse(p, out int n) || n < 0 || n > 255) return null;
            }
            return string.Join(".", parts);
        }

        #region Private CIM / WMI Helpers

        private Dictionary<string, int> LoadHardwareBusMap()
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var scope = new ManagementScope(CimScopePath);
                scope.Connect();
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT Name, BusNumber FROM MSFT_NetAdapterHardwareInfoSettingData")))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        using (o)
                        {
                            string name = GetStr(o, "Name");
                            int bus = GetInt(o, "BusNumber", 999);
                            if (!string.IsNullOrEmpty(name)) map[name] = bus;
                        }
                    }
                }
            }
            catch { }
            return map;
        }

        private Dictionary<int, string> LoadPnpDeviceMap()
        {
            var map = new Dictionary<int, string>();
            try
            {
                var scope = new ManagementScope(CimV2ScopePath);
                scope.Connect();
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT InterfaceIndex, PNPDeviceID FROM Win32_NetworkAdapter WHERE InterfaceIndex IS NOT NULL")))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        using (o)
                        {
                            int idx = GetInt(o, "InterfaceIndex", -1);
                            string pnp = GetStr(o, "PNPDeviceID");
                            if (idx > 0 && !string.IsNullOrEmpty(pnp)) map[idx] = pnp;
                        }
                    }
                }
            }
            catch { }
            return map;
        }

        private Dictionary<string, bool> LoadBindingsMap()
        {
            var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var scope = new ManagementScope(CimScopePath);
                scope.Connect();
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT Name, ComponentID, Enabled FROM MSFT_NetAdapterBindingSettingData")))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        using (o)
                        {
                            string name = GetStr(o, "Name");
                            string comp = GetStr(o, "ComponentID");
                            bool enabled = GetBool(o, "Enabled");
                            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(comp))
                                map[name + "|" + comp] = enabled;
                        }
                    }
                }
            }
            catch { }
            return map;
        }

        private sealed class NicIpSnapshot
        {
            public List<string> IPs { get; } = new List<string>();
            public List<string> IPTexts { get; } = new List<string>();
            public int FirstPrefix { get; set; } = 24;
            public List<string> Gateways { get; } = new List<string>();
            public List<string> DnsServers { get; } = new List<string>();
            public string Mac { get; set; } = "";
        }

        private Dictionary<int, NicIpSnapshot> LoadManagedIpMap()
        {
            var map = new Dictionary<int, NicIpSnapshot>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    var ipProps = ni.GetIPProperties();
                    var ipv4Props = ipProps.GetIPv4Properties();
                    if (ipv4Props == null) continue;

                    int idx = ipv4Props.Index;
                    var snap = new NicIpSnapshot();
                    snap.Mac = FormatMac(ni.GetPhysicalAddress().ToString());

                    foreach (var ua in ipProps.UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string ipStr = ua.Address.ToString();
                        if (ipStr.StartsWith("169.254.", StringComparison.Ordinal) || ipStr == "127.0.0.1") continue;

                        int prefix = ua.PrefixLength > 0 ? ua.PrefixLength : MaskToPrefixLength(ua.IPv4Mask);
                        if (snap.IPs.Count == 0) snap.FirstPrefix = prefix;
                        snap.IPs.Add(ipStr);
                        snap.IPTexts.Add(ipStr + "/" + prefix);
                    }

                    foreach (var gw in ipProps.GatewayAddresses)
                    {
                        if (gw.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            string gStr = gw.Address.ToString();
                            if (!snap.Gateways.Contains(gStr)) snap.Gateways.Add(gStr);
                        }
                    }

                    foreach (var dns in ipProps.DnsAddresses)
                    {
                        if (dns.AddressFamily == AddressFamily.InterNetwork)
                        {
                            string dStr = dns.ToString();
                            if (!snap.DnsServers.Contains(dStr)) snap.DnsServers.Add(dStr);
                        }
                    }

                    map[idx] = snap;
                }
            }
            catch { }
            return map;
        }

        private void QueryDisconnectedIpState(
            ManagementScope scope,
            int ifIndex,
            List<string> ips,
            List<string> ipTexts,
            ref int firstPrefix,
            List<string> gws,
            List<string> dns)
        {
            try
            {
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery(
                    "SELECT IPAddress, PrefixLength FROM MSFT_NetIPAddress WHERE InterfaceIndex=" + ifIndex + " AND AddressFamily=2")))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        using (o)
                        {
                            string ip = GetStr(o, "IPAddress");
                            if (string.IsNullOrEmpty(ip) || ip.StartsWith("169.254.", StringComparison.Ordinal) || ip == "127.0.0.1") continue;
                            int p = GetInt(o, "PrefixLength", 24);
                            if (ips.Count == 0) firstPrefix = p;
                            ips.Add(ip);
                            ipTexts.Add(ip + "/" + p);
                        }
                    }
                }
            }
            catch { }

            try
            {
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery(
                    "SELECT NextHop FROM MSFT_NetRoute WHERE InterfaceIndex=" + ifIndex + " AND AddressFamily=2 AND DestinationPrefix='0.0.0.0/0'")))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        using (o)
                        {
                            string nh = GetStr(o, "NextHop");
                            if (!string.IsNullOrEmpty(nh) && !gws.Contains(nh)) gws.Add(nh);
                        }
                    }
                }
            }
            catch { }

            try
            {
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery(
                    "SELECT ServerAddresses FROM MSFT_DNSClientServerAddress WHERE InterfaceIndex=" + ifIndex + " AND AddressFamily=2")))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        using (o)
                        {
                            if (o["ServerAddresses"] is string[] arr)
                            {
                                foreach (string d in arr)
                                {
                                    if (!string.IsNullOrEmpty(d) && !dns.Contains(d)) dns.Add(d);
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private bool DetectIsOnboard(string desc, int busNumber, string pnpId)
        {
            if (OnboardChipPattern.IsMatch(desc)) return true;
            if (busNumber == 0) return true;
            if (!string.IsNullOrEmpty(pnpId) && pnpId.StartsWith("ACPI\\", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private bool GetBindingEnabled(Dictionary<string, bool> map, string name, string compId, bool fallback)
        {
            return map.TryGetValue(name + "|" + compId, out bool val) ? val : fallback;
        }

        private int GetNetbiosSetting(int ifIndex, string guid)
        {
            try
            {
                var scope = new ManagementScope(CimV2ScopePath);
                scope.Connect();
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery(
                    "SELECT TcpipNetbiosOptions FROM Win32_NetworkAdapterConfiguration WHERE InterfaceIndex=" + ifIndex)))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        using (o)
                        {
                            if (o["TcpipNetbiosOptions"] != null)
                                return Convert.ToInt32(o["TcpipNetbiosOptions"]);
                        }
                    }
                }
            }
            catch { }

            if (!string.IsNullOrWhiteSpace(guid))
            {
                try
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\NetBT\Parameters\Interfaces\Tcpip_" + guid))
                    {
                        if (key != null)
                        {
                            object val = key.GetValue("NetbiosOptions");
                            if (val != null) return Convert.ToInt32(val);
                        }
                    }
                }
                catch { }
            }
            return 0;
        }

        private bool SetNetbiosDisabled(int ifIndex, string guid, bool disable)
        {
            uint target = disable ? 2u : 0u;
            bool ok = false;
            try
            {
                var scope = new ManagementScope(CimV2ScopePath);
                scope.Connect();
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery(
                    "SELECT * FROM Win32_NetworkAdapterConfiguration WHERE InterfaceIndex=" + ifIndex)))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        using (o)
                        {
                            var inParams = o.GetMethodParameters("SetTcpipNetbios");
                            inParams["TcpipNetbiosOptions"] = target;
                            var outParams = o.InvokeMethod("SetTcpipNetbios", inParams, null);
                            uint ret = outParams != null && outParams["ReturnValue"] != null ? Convert.ToUInt32(outParams["ReturnValue"]) : 99;
                            if (ret == 0 || ret == 1) ok = true;
                        }
                    }
                }
            }
            catch { }

            if (!string.IsNullOrWhiteSpace(guid))
            {
                try
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\NetBT\Parameters\Interfaces\Tcpip_" + guid, true))
                    {
                        if (key != null)
                            key.SetValue("NetbiosOptions", (int)target, RegistryValueKind.DWord);
                    }
                }
                catch { }
            }
            return ok;
        }

        private string GetAdapterNameByIndex(ManagementScope scope, int ifIndex)
        {
            using (var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT Name FROM MSFT_NetAdapter WHERE InterfaceIndex=" + ifIndex)))
            {
                foreach (ManagementObject o in s.Get())
                {
                    using (o)
                    {
                        return GetStr(o, "Name");
                    }
                }
            }
            return null;
        }

        private void RenameAdapter(ManagementScope scope, int ifIndex, string currName, string newName)
        {
            try
            {
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM MSFT_NetAdapter WHERE InterfaceIndex=" + ifIndex)))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        using (o)
                        {
                            var inParams = o.GetMethodParameters("Rename");
                            inParams["NewName"] = newName;
                            o.InvokeMethod("Rename", inParams, null);
                            return;
                        }
                    }
                }
            }
            catch
            {
                RunNetsh("interface set interface name=\"" + currName + "\" newname=\"" + newName + "\"");
            }
        }

        private void InvokeAdapterMethod(ManagementScope scope, int ifIndex, string methodName)
        {
            using (var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM MSFT_NetAdapter WHERE InterfaceIndex=" + ifIndex)))
            {
                foreach (ManagementObject o in s.Get())
                {
                    using (o)
                    {
                        o.InvokeMethod(methodName, null);
                        return;
                    }
                }
            }
        }

        private void SetDhcpMode(ManagementScope scope, int ifIndex, bool enable)
        {
            try
            {
                ushort dhcpVal = enable ? (ushort)1 : (ushort)2;
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery(
                    "SELECT * FROM MSFT_NetIPInterface WHERE InterfaceIndex=" + ifIndex + " AND AddressFamily=2")))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        using (o)
                        {
                            o["Dhcp"] = dhcpVal;
                            o.Put();
                        }
                    }
                }
            }
            catch { }
        }

        private void SetInterfaceMetric(ManagementScope scope, int ifIndex, bool automatic, uint metric)
        {
            try
            {
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery(
                    "SELECT * FROM MSFT_NetIPInterface WHERE InterfaceIndex=" + ifIndex + " AND AddressFamily=2")))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        using (o)
                        {
                            o["AutomaticMetric"] = automatic ? (ushort)1 : (ushort)2;
                            if (!automatic && metric > 0)
                                o["InterfaceMetric"] = metric;
                            o.Put();
                        }
                    }
                }
            }
            catch { }
        }

        private void ClearExistingIpAndRoutes(ManagementScope scope, int ifIndex)
        {
            try
            {
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery(
                    "SELECT * FROM MSFT_NetIPAddress WHERE InterfaceIndex=" + ifIndex + " AND AddressFamily=2")))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        using (o)
                        {
                            string ip = GetStr(o, "IPAddress");
                            if (!ip.StartsWith("169.254.", StringComparison.Ordinal))
                            {
                                try { o.Delete(); } catch { }
                            }
                        }
                    }
                }
            }
            catch { }

            try
            {
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery(
                    "SELECT * FROM MSFT_NetRoute WHERE InterfaceIndex=" + ifIndex + " AND AddressFamily=2 AND DestinationPrefix='0.0.0.0/0'")))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        using (o)
                        {
                            try { o.Delete(); } catch { }
                        }
                    }
                }
            }
            catch { }
        }

        private void CreateNewIPv4Address(ManagementScope scope, int ifIndex, string currName, string ip, byte prefixLen, string gateway)
        {
            try
            {
                using (var cls = new ManagementClass(scope, new ManagementPath("MSFT_NetIPAddress"), null))
                {
                    var inParams = cls.GetMethodParameters("Create");
                    inParams["InterfaceIndex"] = (uint)ifIndex;
                    inParams["IPAddress"] = ip;
                    inParams["PrefixLength"] = prefixLen;
                    inParams["AddressFamily"] = (ushort)2;
                    if (!string.IsNullOrWhiteSpace(gateway))
                        inParams["DefaultGateway"] = gateway;

                    cls.InvokeMethod("Create", inParams, null);
                }
            }
            catch
            {
                string mask = PrefixLengthToMask(prefixLen);
                string args = "interface ipv4 set address name=\"" + currName + "\" static " + ip + " " + mask;
                if (!string.IsNullOrWhiteSpace(gateway))
                    args += " " + gateway + " 1";
                RunNetsh(args);
            }
        }

        private void SetDnsServers(ManagementScope scope, int ifIndex, string currName, IList<string> dnsServers)
        {
            try
            {
                bool updated = false;
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery(
                    "SELECT * FROM MSFT_DNSClientServerAddress WHERE InterfaceIndex=" + ifIndex + " AND AddressFamily=2")))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        using (o)
                        {
                            o["ServerAddresses"] = dnsServers != null && dnsServers.Count > 0 ? dnsServers.ToArray() : new string[0];
                            o.Put();
                            updated = true;
                        }
                    }
                }
                if (updated) return;
            }
            catch { }

            if (dnsServers == null || dnsServers.Count == 0)
            {
                RunNetsh("interface ipv4 set dnsservers name=\"" + currName + "\" source=dhcp");
            }
            else
            {
                RunNetsh("interface ipv4 set dnsservers name=\"" + currName + "\" static " + dnsServers[0] + " primary validate=no");
                for (int i = 1; i < dnsServers.Count; i++)
                    RunNetsh("interface ipv4 add dnsservers name=\"" + currName + "\" " + dnsServers[i] + " index=" + (i + 1) + " validate=no");
            }
        }

        private void SetAdapterBinding(ManagementScope scope, string adapterName, string componentId, bool enabled, string label, Action<string, string> log)
        {
            try
            {
                string safeName = adapterName.Replace("'", "''");
                string safeComp = componentId.Replace("'", "''");
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery(
                    "SELECT * FROM MSFT_NetAdapterBindingSettingData WHERE Name='" + safeName + "' AND ComponentID='" + safeComp + "'")))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        using (o)
                        {
                            o.InvokeMethod(enabled ? "Enable" : "Disable", null);
                            if (!string.IsNullOrEmpty(label))
                                log("  -> Đã " + (enabled ? "bật " : "tắt ") + label + " (" + componentId + ")", "INFO");
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrEmpty(label))
                    log("  -> Không thể đổi " + label + " (" + componentId + "): " + ex.Message, "WARN");
            }
        }

        private void DisablePowerManagement(ManagementScope scope, string adapterName)
        {
            string safeName = adapterName.Replace("'", "''");
            try
            {
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery(
                    "SELECT * FROM MSFT_NetAdapterPowerManagementSettingData WHERE Name='" + safeName + "'")))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        using (o)
                        {
                            if (HasProp(o, "AllowComputerToTurnOffDevice")) o["AllowComputerToTurnOffDevice"] = (ushort)1; // Disabled
                            if (HasProp(o, "WakeOnMagicPacket")) o["WakeOnMagicPacket"] = (ushort)1;
                            if (HasProp(o, "WakeOnPattern")) o["WakeOnPattern"] = (ushort)1;
                            o.Put();
                        }
                    }
                }
            }
            catch { }

            try
            {
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery(
                    "SELECT * FROM MSFT_NetAdapterAdvancedPropertySettingData WHERE Name='" + safeName + "'")))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        using (o)
                        {
                            string kw = GetStr(o, "RegistryKeyword");
                            if (PowerKeywordPattern.IsMatch(kw))
                            {
                                o["RegistryValue"] = new string[] { "0" };
                                o.Put();
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private static void RunNetsh(string arguments)
        {
            var psi = new ProcessStartInfo("netsh.exe", arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var proc = Process.Start(psi))
            {
                proc.WaitForExit(8000);
                if (proc.ExitCode != 0)
                {
                    string err = proc.StandardError.ReadToEnd();
                    string outTxt = proc.StandardOutput.ReadToEnd();
                    throw new InvalidOperationException("netsh lỗi (" + proc.ExitCode + "): " + (string.IsNullOrWhiteSpace(err) ? outTxt : err).Trim());
                }
            }
        }

        private static string FormatOperationalStatus(uint opStatus, uint mediaState)
        {
            if (opStatus == 1) return "Up";
            if (opStatus == 5) return "Not Present";
            if (mediaState == 2) return "Disconnected";
            return "Down";
        }

        private static string FormatLinkSpeed(long bps)
        {
            if (bps <= 0) return "0 bps";
            if (bps >= 1000000000L)
            {
                double g = bps / 1000000000.0;
                return (Math.Abs(g - Math.Round(g)) < 0.05 ? Math.Round(g).ToString("0") : g.ToString("0.#")) + " Gbps";
            }
            if (bps >= 1000000L)
                return Math.Round(bps / 1000000.0).ToString("0") + " Mbps";
            return Math.Round(bps / 1000.0).ToString("0") + " Kbps";
        }

        private static string FormatMac(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string clean = raw.Replace(":", "").Replace("-", "").Trim().ToUpperInvariant();
            if (clean.Length != 12) return raw;
            return string.Join("-", Enumerable.Range(0, 6).Select(i => clean.Substring(i * 2, 2)));
        }

        private static int MaskToPrefixLength(IPAddress mask)
        {
            if (mask == null) return 24;
            byte[] bytes = mask.GetAddressBytes();
            int bits = 0;
            foreach (byte b in bytes)
            {
                for (int i = 7; i >= 0; i--)
                {
                    if ((b & (1 << i)) != 0) bits++;
                }
            }
            return bits > 0 ? bits : 24;
        }

        private static string PrefixLengthToMask(int prefixLength)
        {
            uint mask = prefixLength == 0 ? 0u : uint.MaxValue << (32 - prefixLength);
            byte[] bytes = BitConverter.GetBytes(mask);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            return new IPAddress(bytes).ToString();
        }

        private static bool HasProp(ManagementBaseObject obj, string name)
        {
            foreach (PropertyData p in obj.Properties)
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string GetStr(ManagementBaseObject obj, string prop) => obj[prop] != null ? obj[prop].ToString() : "";
        private static bool GetBool(ManagementBaseObject obj, string prop) => obj[prop] != null && Convert.ToBoolean(obj[prop]);
        private static int GetInt(ManagementBaseObject obj, string prop, int def) => obj[prop] != null ? Convert.ToInt32(obj[prop]) : def;
        private static uint GetUInt(ManagementBaseObject obj, string prop, uint def) => obj[prop] != null ? Convert.ToUInt32(obj[prop]) : def;
        private static long GetLong(ManagementBaseObject obj, string prop, long def) => obj[prop] != null ? Convert.ToInt64(obj[prop]) : def;

        #endregion
    }
}
