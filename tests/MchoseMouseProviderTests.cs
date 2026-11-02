using System;
using System.Collections.Generic;
using Voltide.Core;
using Voltide.Interop;
using Voltide.Providers;

internal static class MchoseMouseProviderTests
{
    private static int _failures;

    private static int Main()
    {
        Run("request contains only the read-only 0x0900 query", TestRequest);
        Run("accepts valid 0% and 100% replies", TestBatteryBoundaries);
        Run("recognizes the captured 41% charging transition", TestLiveChargingTransition);
        Run("rejects invalid values, checksum, and short frames", TestInvalidReplies);
        Run("ignores other commands", TestWrongCommand);
        Run("discovers only the K7 V2 Ultra+ MI02 collection", TestDiscoveryFilter);

        Console.WriteLine(_failures == 0 ? "All MCHOSE mouse tests passed." : _failures + " MCHOSE mouse test(s) failed.");
        return _failures == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine("PASS " + name);
        }
        catch (Exception ex)
        {
            _failures++;
            Console.WriteLine("FAIL " + name + ": " + ex.Message);
        }
    }

    private static void TestRequest()
    {
        byte[] request = MchoseMouseProtocol.CreateBatteryRequest();
        Assert(request.Length == 64, "request length");
        byte[] prefix = new byte[] { 0x4D, 0x01, 0x01, 0x00, 0x00, 0x09, 0x00, 0x00, 0x08 };
        for (int i = 0; i < prefix.Length; i++) Assert(request[i] == prefix[i], "request byte " + i);
        for (int i = prefix.Length; i < request.Length; i++) Assert(request[i] == 0, "request padding " + i);
    }

    private static void TestBatteryBoundaries()
    {
        AssertBattery(Reply(0, 0), 0, false);
        AssertBattery(Reply(100, 1), 100, true);
    }

    private static void TestLiveChargingTransition()
    {
        byte[] uncharged = Capture();
        byte[] charging = CaptureCharging();
        Assert(uncharged[19] == 0x00 && charging[19] == 0x01, "captured chargeStatus transition");
        Assert(uncharged[24] == 0xA2 && charging[24] == 0xA3, "captured checksum changes with chargeStatus");
        AssertBattery(uncharged, 41, false);
        AssertBattery(charging, 41, true);
    }

    private static void TestInvalidReplies()
    {
        AssertInvalid(Reply(101, 0), "percentage above 100");
        AssertInvalid(Reply(50, 2), "unknown charging value");
        AssertInvalid(Reply(50, 0, 1), "unsupported connection state");

        byte[] shortFrame = Capture();
        Array.Resize(ref shortFrame, 24);
        AssertInvalid(shortFrame, "short report");

        byte[] badChecksum = Capture();
        badChecksum[24] ^= 0x01;
        AssertInvalid(badChecksum, "bad checksum");

        byte[] badProduct = Capture();
        badProduct[10] = 0x09;
        RecalculateChecksum(badProduct);
        AssertInvalid(badProduct, "wrong mouse product id");

        byte[] badPadding = Capture();
        badPadding[63] = 0x01;
        AssertInvalid(badPadding, "nonzero padding");

        byte[] noLink = Reply(41, 0, 0);
        int percent;
        bool charging;
        bool chargingKnown;
        Assert(MchoseMouseProtocol.ParseResponse(noLink, out percent, out charging, out chargingKnown) == MchoseMouseReplyKind.Unavailable, "zero connection state is unavailable");
    }

    private static void TestWrongCommand()
    {
        byte[] report = Capture();
        report[5] = 0x08;
        int percent;
        bool charging;
        bool chargingKnown;
        Assert(MchoseMouseProtocol.ParseResponse(report, out percent, out charging, out chargingKnown) == MchoseMouseReplyKind.Unrelated, "wrong command must not be parsed as battery");
    }

    private static void TestDiscoveryFilter()
    {
        List<HidDeviceInfo> catalog = new List<HidDeviceInfo>();
        catalog.Add(Hid(0x3837, 0x1014, 0xFF01, 0x0001, 64, 64, 0, @"\??\hid#vid_3837&pid_1014&mi_02&col02#receiver"));
        catalog.Add(Hid(0x3837, 0x1014, 0xFF01, 0x0001, 64, 64, 64, @"\??\hid#vid_3837&pid_1014&mi_02&col02#receiver"));
        catalog.Add(Hid(0x3837, 0x1014, 0xFF0B, 0x00C4, 61, 61, 61, @"\??\hid#vid_3837&pid_1014&mi_02&col01#keyboard"));
        catalog.Add(Hid(0x3837, 0x1014, 0xFF01, 0x0001, 64, 64, 64, @"\??\hid#vid_3837&pid_1014&mi_01&col01#keyboard"));
        catalog.Add(Hid(0x3837, 0x1014, 0xFF01, 0x0001, 63, 64, 64, @"\??\hid#vid_3837&pid_1014&mi_02&col02#bad-input"));
        catalog.Add(Hid(0x3837, 0x1015, 0xFF01, 0x0001, 64, 64, 64, @"\??\hid#vid_3837&pid_1015&mi_02&col02#other-product"));
        catalog.Add(Hid(0x3554, 0xFA09, 0xFF02, 0x0001, 20, 20, 20, @"\??\hid#vid_3554&pid_fa09&mi_02&col01#keyboard"));

        List<DeviceInfo> found = new MchoseMouseProvider().Discover(catalog);
        Assert(found.Count == 1, "one matching receiver collection");
        Assert(found[0].Kind == DeviceKind.Mouse, "device kind");
        Assert(found[0].Name == "MCHOSE K7 V2 Ultra+", "device name");
        Assert(found[0].ProductId == 0x1014, "receiver product id");
    }

    private static byte[] Capture()
    {
        byte[] report = new byte[64];
        byte[] capturedPrefix = new byte[] {
            0x4D, 0x01, 0x01, 0x10, 0x00, 0x09, 0x00, 0x00,
            0x37, 0x38, 0x26, 0x40, 0x04, 0x00, 0x00, 0x00,
            0x00, 0x10, 0x02, 0x00, 0x29, 0x00, 0x08, 0xE4, 0xA2
        };
        Array.Copy(capturedPrefix, report, capturedPrefix.Length);
        return report;
    }

    private static byte[] CaptureCharging()
    {
        byte[] report = new byte[64];
        byte[] capturedPrefix = new byte[] {
            0x4D, 0x01, 0x01, 0x10, 0x00, 0x09, 0x00, 0x00,
            0x37, 0x38, 0x26, 0x40, 0x04, 0x00, 0x00, 0x00,
            0x00, 0x10, 0x02, 0x01, 0x29, 0x00, 0x08, 0xE4, 0xA3
        };
        Array.Copy(capturedPrefix, report, capturedPrefix.Length);
        return report;
    }

    private static byte[] Reply(byte percent, byte charge)
    {
        return Reply(percent, charge, 2);
    }

    private static byte[] Reply(byte percent, byte charge, byte connection)
    {
        byte[] report = Capture();
        report[18] = connection;
        report[19] = charge;
        report[20] = percent;
        RecalculateChecksum(report);
        return report;
    }

    private static void RecalculateChecksum(byte[] report)
    {
        int checksum = 0;
        for (int i = 2; i <= 23; i++) checksum ^= report[i];
        report[24] = (byte)checksum;
    }

    private static void AssertBattery(byte[] report, int expectedPercent, bool expectedCharging)
    {
        int percent;
        bool charging;
        bool chargingKnown;
        MchoseMouseReplyKind result = MchoseMouseProtocol.ParseResponse(report, out percent, out charging, out chargingKnown);
        Assert(result == MchoseMouseReplyKind.Battery, "battery reply status");
        Assert(percent == expectedPercent, "battery percentage");
        Assert(charging == expectedCharging, "charging flag");
        Assert(chargingKnown, "validated charging status must be known");
    }

    private static void AssertInvalid(byte[] report, string name)
    {
        int percent;
        bool charging;
        bool chargingKnown;
        Assert(MchoseMouseProtocol.ParseResponse(report, out percent, out charging, out chargingKnown) == MchoseMouseReplyKind.Invalid, name);
        Assert(percent == 0 && !charging && !chargingKnown, name + " must not return a reading");
    }

    private static HidDeviceInfo Hid(ushort vid, ushort pid, ushort page, ushort usage, int input, int output, int feature, string path)
    {
        HidDeviceInfo hid = new HidDeviceInfo();
        hid.VendorId = vid;
        hid.ProductId = pid;
        hid.UsagePage = page;
        hid.Usage = usage;
        hid.InputReportLength = input;
        hid.OutputReportLength = output;
        hid.FeatureReportLength = feature;
        hid.Path = path;
        return hid;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
