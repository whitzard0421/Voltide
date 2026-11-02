using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Voltide.Core
{
    /// <summary>为共用 USB ID 的接收器提供便携式型号覆盖，仅改变显示名称。</summary>
    internal static class DeviceNames
    {
        internal static Dictionary<string, string> Load()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "device-names.ini");
            try
            {
                if (File.Exists(path)) return Parse(File.ReadAllLines(path, Encoding.UTF8));
            }
            catch (IOException ex) { Trace.TraceWarning("Device names: " + ex.Message); }
            catch (UnauthorizedAccessException ex) { Trace.TraceWarning("Device names: " + ex.Message); }
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>格式为 VID:PID=型号；空行和 # 注释忽略，后出现的有效值覆盖前值。</summary>
        internal static Dictionary<string, string> Parse(string[] lines)
        {
            Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in lines)
            {
                string text = line.Trim();
                if (text.Length == 0 || text.StartsWith("#")) continue;
                int equals = text.IndexOf('=');
                if (equals < 0) continue;
                string key = text.Substring(0, equals).Trim();
                string model = text.Substring(equals + 1).Trim();
                ushort vid, pid;
                if (key.Length != 9 || key[4] != ':' || model.Length == 0) continue;
                if (!ushort.TryParse(key.Substring(0, 4), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out vid)) continue;
                if (!ushort.TryParse(key.Substring(5, 4), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out pid)) continue;
                names[key] = model;
            }
            return names;
        }

        internal static void Apply(DeviceInfo device, Dictionary<string, string> names)
        {
            string model;
            string key = device.VendorId.ToString("X4") + ":" + device.ProductId.ToString("X4");
            if (!names.TryGetValue(key, out model)) return;
            device.Model = model;
            device.ShortModel = model;
            device.Name = string.IsNullOrEmpty(device.Brand) ? model : device.Brand + " " + model;
        }
    }
}
