using System;
using System.Collections.Generic;
using Voltide.Core;
using Voltide.Interop;
using Voltide.Providers;

internal static class KeyboardTests
{
    private static int checks;

    private static void Assert(bool ok, string name)
    {
        if (!ok) throw new Exception(name);
        checks++;
    }

    private static byte[] Frame(byte percent)
    {
        byte[] frame = new byte[20];
        frame[0] = 0x13; frame[1] = 0x4A; frame[2] = 1;
        frame[4] = 2; frame[5] = percent; frame[6] = 1;
        Seal(frame);
        return frame;
    }

    private static void Seal(byte[] frame)
    {
        int sum = 0;
        for (int i = 0; i < 19; i++) sum += frame[i];
        frame[19] = (byte)sum;
    }

    private static void Main()
    {
        int percent;
        bool? charging;
        foreach (byte value in new byte[] { 0, 82, 100 })
            Assert(MchoseProvider.TryParseBattery(Frame(value), out percent, out charging) && percent == value, "valid " + value);
        Assert(Frame(82)[19] == 0xB3, "GX87 captured checksum");
        Assert(MchoseProvider.TryParseBattery(Frame(82), out percent, out charging) && charging == false,
            "captured status 01 means fullStatus 1 and chargeStatus 0");
        byte[] chargeFrame = Frame(82); chargeFrame[6] = 0x11; Seal(chargeFrame);
        Assert(MchoseProvider.TryParseBattery(chargeFrame, out percent, out charging) && charging == true && percent == 82,
            "charging uses high nibble without changing percentage");
        chargeFrame[6] = 0x21; Seal(chargeFrame);
        Assert(MchoseProvider.TryParseBattery(chargeFrame, out percent, out charging) && !charging.HasValue && percent == 82,
            "unknown charging state preserves valid percentage");
        Assert(!MchoseProvider.TryParseBattery(null, out percent, out charging), "null");
        Assert(!MchoseProvider.TryParseBattery(new byte[6], out percent, out charging), "short frame");
        Assert(!MchoseProvider.TryParseBattery(Frame(101), out percent, out charging), "do not clamp invalid percentage");
        byte[] bad = Frame(82); bad[19] ^= 1;
        Assert(!MchoseProvider.TryParseBattery(bad, out percent, out charging), "checksum");
        bad = Frame(82); bad[1] = 0x44; Seal(bad);
        Assert(!MchoseProvider.TryParseBattery(bad, out percent, out charging), "different command");
        bad = Frame(82); bad[4] = 0; Seal(bad);
        Assert(!MchoseProvider.TryParseBattery(bad, out percent, out charging), "query echo");
        bad = Frame(82); bad[3] = 1; Seal(bad);
        Assert(!MchoseProvider.TryParseBattery(bad, out percent, out charging), "wrong fragment");

        HidDeviceInfo receiver = new HidDeviceInfo();
        receiver.VendorId = 0x3554; receiver.ProductId = 0xFA09;
        receiver.UsagePage = 0xFF02; receiver.InputReportLength = receiver.OutputReportLength = 20;
        receiver.ProductName = "2.4G Wireless Receiver"; receiver.Path = "test-receiver";
        MchoseProvider provider = new MchoseProvider();
        List<DeviceInfo> found = provider.Discover(new List<HidDeviceInfo> { receiver });
        Assert(found.Count == 1 && found[0].Kind == DeviceKind.Keyboard, "keyboard discovery");
        Assert(found[0].Model == "2.4G Keyboard", "shared PID does not identify K99 or GX87");
        Dictionary<string, string> names = DeviceNames.Parse(new string[] {
            "# local model", "", "invalid", "3554:FA09=", "ZZZZ:FA09=invalid", "3554:fa09 = GX87"
        });
        Assert(names.Count == 1, "ignore invalid aliases");
        DeviceNames.Apply(found[0], names);
        Assert(found[0].Name == "MCHOSE GX87" && found[0].Model == "GX87" && found[0].ShortModel == "GX87", "GX87 name");
        Assert(found[0].HidPath == "test-receiver" && found[0].ProviderName == provider.Name, "alias preserves transport");
        DeviceInfo other = new DeviceInfo(); other.VendorId = 0x3837; other.ProductId = 0x1014; other.Model = "K7 V2 Ultra+";
        DeviceNames.Apply(other, names);
        Assert(other.Model == "K7 V2 Ultra+", "unmapped model unchanged");
        receiver.ProductName = "MCHOSE K99 v2";
        Assert(provider.Discover(new List<HidDeviceInfo> { receiver })[0].Model == "K99 v2", "product string retained");
        receiver.InputReportLength = 8;
        Assert(provider.Discover(new List<HidDeviceInfo> { receiver }).Count == 0, "exclude normal input collection");
        Console.WriteLine("Keyboard and device names: " + checks + " checks passed.");
    }
}
