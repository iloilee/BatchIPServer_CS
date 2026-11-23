using System;
using System.Collections.Generic;

namespace BatchIPServer
{
    public sealed class AdapterItem
    {
        public bool Selected { get; set; }
        public int InterfaceIndex { get; set; }
        public string InterfaceGuid { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Status { get; set; } = "";
        public long SpeedBps { get; set; }
        public string LinkSpeedText { get; set; } = "-";
        public string MacAddress { get; set; } = "";
        public bool IsOnboard { get; set; }
        public bool IsVirtual { get; set; }
        public List<string> CurrentIPs { get; set; } = new List<string>();
        public int CurrentPrefix { get; set; } = 24;
        public string CurrentIPText { get; set; } = "DHCP / Chưa đặt";
        public List<string> CurrentGWs { get; set; } = new List<string>();
        public List<string> CurrentDNS { get; set; } = new List<string>();
        public int NetbiosOption { get; set; }
        public bool IPv6Enabled { get; set; } = true;
        public bool FileShareEnabled { get; set; } = true;
        public bool QoSEnabled { get; set; } = true;
        public int BusNumber { get; set; } = 999;

        // Các trường xem trước (Preview)
        public string NewName { get; set; } = "";
        public string NewIP { get; set; } = "";
        public int PrefixLength { get; set; } = 24;
        public string GwRole { get; set; } = "Gateway 1";
        public string NewGateway { get; set; } = "";
        public string Metric { get; set; } = "Tự động";
        public string NewDNS { get; set; } = "";
    }

    public sealed class AppConfig
    {
        public string LastPrefix { get; set; } = "";
        public string SubnetBase { get; set; } = "192.168.1";
        public string SubnetMask { get; set; } = "255.255.255.0 (/24)";
        public int StartOctet { get; set; } = 200;
        public string Direction { get; set; } = "Up";
        public string Gateway1 { get; set; } = "192.168.1.1";
        public string Gateway2 { get; set; } = "";
        public string Dns1 { get; set; } = "8.8.8.8";
        public string Dns2 { get; set; } = "8.8.4.4";
    }

    public sealed class AdapterBackupItem
    {
        public int InterfaceIndex { get; set; }
        public string InterfaceGuid { get; set; } = "";
        public string MacAddress { get; set; } = "";
        public string OriginalName { get; set; } = "";
        public List<string> IPs { get; set; } = new List<string>();
        public int PrefixLength { get; set; } = 24;
        public List<string> Gateways { get; set; } = new List<string>();
        public List<string> DNS { get; set; } = new List<string>();
        public int NetbiosOption { get; set; }
        public bool IPv6Enabled { get; set; } = true;
        public bool FileShareEnabled { get; set; } = true;
        public bool QoSEnabled { get; set; } = true;
    }

    public sealed class ApplyOptions
    {
        public bool RenameEnabled { get; set; }
        public bool SetDns { get; set; }
        public List<string> DnsServers { get; set; } = new List<string>();
        public bool ClearOldIpAndGw { get; set; }
        public bool DisableNetbios { get; set; }
        public bool DisableFileSharing { get; set; }
        public bool DisableQoS { get; set; }
        public bool DisableIPv6 { get; set; }
        public bool DisablePowerSave { get; set; }
    }
}
