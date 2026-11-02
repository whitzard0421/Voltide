using System;
using System.Collections.Generic;
using System.Diagnostics;
using Voltide.Core;
using Voltide.Interop;

namespace Voltide.Providers
{
    /// <summary>
    /// MCHOSE (迈丛) 2.4G keyboards.
    ///
    /// Protocol reverse-engineered from M HUB's USB traffic (2026-10-19):
    ///   20-byte frame over HID report ID 0x13, sent with SET_REPORT,
    ///   answered on the interrupt-IN endpoint.
    ///
    ///   frame[0]      = 0x13 report id
    ///   frame[1]      = command
    ///   frame[2..3]   = 0x01 0x00
    ///   frame[4..18]  = payload
    ///   frame[19]     = checksum = sum(frame[0..18]) &amp; 0xFF
    ///
    ///   Battery query   : 0x4A
    ///   Battery reply   : 13 4A 01 00 02 64 01 ... C5
    ///                                ^^ ^^
    ///                                |  +-- percent (0x64 = 100)
    ///                                +----- payload length
    /// </summary>
    public class MchoseProvider : IDeviceProvider
    {
        private const ushort MchoseVid = 0x3554;
        private const byte ReportId = 0x13;
        private const byte CmdBattery = 0x4A;

        public string Name { get { return "MCHOSE HID"; } }

        public List<DeviceInfo> Discover(List<HidDeviceInfo> catalog)
        {
            List<DeviceInfo> found = new List<DeviceInfo>();
            
            Dictionary<string, HidDeviceInfo> candidates = new Dictionary<string, HidDeviceInfo>();
            foreach (HidDeviceInfo h in catalog)
            {
                if (h.VendorId != MchoseVid) continue;
                if (h.UsagePage != 0xFF02) continue;      // vendor collection
                if (h.InputReportLength != 20) continue;
                if (h.OutputReportLength != 20) continue;

                string key = h.VendorId.ToString("X4") + ":" + h.ProductId.ToString("X4");
                if (!candidates.ContainsKey(key)) candidates.Add(key, h);
            }

            foreach (KeyValuePair<string, HidDeviceInfo> kv in candidates)
            {
                HidDeviceInfo h = kv.Value;

                DeviceInfo d = new DeviceInfo();
                d.Id = "mchose-" + kv.Key;
                d.Brand = "MCHOSE";
                d.Model = ResolveModel(h);
                d.Name = "MCHOSE " + d.Model;
                d.Kind = DeviceKind.Keyboard;
                d.VendorId = h.VendorId;
                d.ProductId = h.ProductId;
                d.HidPath = h.Path;
                d.ProviderName = Name;
                found.Add(d);
            }
            return found;
        }

        public DeviceReading Read(DeviceInfo d)
        {
            DeviceReading r = new DeviceReading();
            r.UpdatedAt = DateTime.Now;
            r.Online = false;
            r.Level = BatteryLevel.Unknown;

            try
            {
                byte[] cmd = new byte[20];
                cmd[0] = ReportId;
                cmd[1] = CmdBattery;
                cmd[2] = 0x01;
                cmd[3] = 0x00;
                cmd[19] = Checksum(cmd);

                using (HidSession s = new HidSession(d.HidPath, 20, 20))
                {
                    s.Flush(30);

                    byte[] resp = null;

                    for (int attempt = 0; attempt < 3 && resp == null; attempt++)
                    {
                        if (attempt > 0) System.Threading.Thread.Sleep(120);

                        if (!s.SetOutputReport(cmd))
                        {
                            r.Error = "发送失败（设备被占用？）";
                            return r;
                        }

                        resp = WaitFor(s, ReportId, CmdBattery, 450);
                    }

                    if (resp == null)
                    {
                        r.Sleeping = true;
                        r.Error = "设备休眠";
                        return r;
                    }

                    r.Raw = Hex(resp);

                    int level;
                    bool? charging;
                    if (!TryParseBattery(resp, out level, out charging))
                    {
                        r.Error = "电量回复无效";
                        return r;
                    }

                    r.Percent = level;
                    r.Level = BatteryMath.FromPercent(level);
                    r.Online = true;
                    r.ChargingKnown = charging.HasValue;
                    r.Charging = charging.GetValueOrDefault();
                    r.Detail = "2.4G";
                }
            }
            catch (Exception ex)
            {
                r.Error = ex.Message;
            }

            return r;
        }

        private static byte Checksum(byte[] buf)
        {
            int sum = 0;
            for (int i = 0; i < buf.Length - 1; i++) sum += buf[i];
            return (byte)(sum & 0xFF);
        }

        /// <summary>只接受完整、校验正确的电量回复；回显查询帧不包含电量。</summary>
        internal static bool TryParseBattery(byte[] frame, out int percent, out bool? charging)
        {
            percent = 0;
            charging = null;
            if (frame == null || frame.Length != 20 || frame[0] != ReportId || frame[1] != CmdBattery)
                return false;
            if (frame[2] != 0x01 || frame[3] != 0 || frame[4] != 0x02 || frame[19] != Checksum(frame))
                return false;
            if (frame[5] > 100) return false;
            percent = frame[5];
            // 官网使用小端位域：低四位是 fullStatus，高四位才是 chargeStatus。
            // 仅解码已确认的 0/1；其他状态仍保留电量，并向界面报告充电状态未知。
            int chargeStatus = frame[6] >> 4;
            if (chargeStatus <= 1) charging = chargeStatus == 1;
            return true;
        }

        private static byte[] WaitFor(HidSession s, byte rid, byte cmd, int timeoutMs)
        {
            Stopwatch clock = Stopwatch.StartNew();
            byte[] invalid = null;
            while (clock.ElapsedMilliseconds < timeoutMs)
            {
                int remaining = timeoutMs - (int)clock.ElapsedMilliseconds;
                byte[] p = s.Read(Math.Max(1, remaining));
                if (p == null) break;
                if (p.Length < 2 || p[0] != rid || p[1] != cmd) continue;
                // 接收器可能先回显查询，继续等待真正携带两个数据字节的回复。
                if (p.Length == 20 && p[4] == 0 && p[19] == Checksum(p)) continue;
                int percent;
                bool? charging;
                if (TryParseBattery(p, out percent, out charging)) return p;
                invalid = p;
            }
            return invalid;
        }

        private static string Hex(byte[] b)
        {
            if (b == null) return "";
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < b.Length; i++)
            {
                sb.Append(b[i].ToString("X2"));
                sb.Append(' ');
            }
            return sb.ToString().Trim();
        }

        private static string ResolveModel(HidDeviceInfo device)
        {
            string product = device.ProductName;
            if (!string.IsNullOrWhiteSpace(product) &&
                !product.Equals("2.4G Wireless Receiver", StringComparison.OrdinalIgnoreCase))
            {
                if (product.StartsWith("MCHOSE ", StringComparison.OrdinalIgnoreCase))
                    product = product.Substring(7);
                return product.Trim();
            }
            // GX87、K99 v2 等共用 FA09，接收器 ID 无法唯一确定型号。
            return "2.4G Keyboard";
        }
    }
}
