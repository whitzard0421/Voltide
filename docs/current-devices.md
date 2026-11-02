# K7 V2 Ultra+ 与 GX87

运行 `Voltide.exe --show` 即可打开面板。请将 `device-names.ini` 留在 exe 旁边，
它把当前 `3554:FA09` 接收器的型号设为 GX87。若已运行其他版本，先从旧版托盘菜单退出。

| 设备 | 接收器 | 电量通道 | 实机读数 |
|---|---|---|---|
| MCHOSE K7 V2 Ultra+ | `3837:1014` | MI02 / `FF01:0001`，64 字节输出报告 | 41% |
| MCHOSE GX87 | `3554:FA09` | `FF02:0002`，20 字节输出报告 | 82% |

读数记录于 2026-11-02。GX87 接收器保留在用户的 USB Hub 上，经 2.4G 连接读取。
鼠标和键盘均只执行电量或设备信息查询，不写入按键、灯光、DPI 或固件配置。

## GX87 的电量回复

查询通过 `HidD_SetOutputReport` 发送，回复从中断输入端点读取：

```text
查询：13 4A 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 5E
回复：13 4A 01 00 02 52 01 00 00 00 00 00 00 00 00 00 00 00 00 B3
```

以下偏移均从 0 开始：`[5]=0x52`，即 82%；`[19]` 是前 19 字节之和的低 8 位。
解析同时检查报告 ID、命令、分包字段、数据长度和 0–100 的有效范围。
查询回显不算作电量回复，无效数据不会被截成 100%。

官网解析器使用小端位域：`[6]` 的低四位为 `fullStatus`，高四位为 `chargeStatus`。
本机 `[6]=0x01` 解码为 `fullStatus=1`、`chargeStatus=0`，不是正在充电；
`chargeStatus=1` 表示正在充电。实现仅识别充电状态 0/1，其他值保留电量并显示未知。
`fullStatus` 不参与电量换算。此次尚未实测插拔充电线时的状态切换。

官网脚本也使用 `getBattery` → `13 4A` 查询。其首页支持原始百分比和十档显示；
十档映射将 75–84 显示为 80，因此官网截图中的 80% 与本机原始 82% 符合这一规则。
本分支保留接收器返回的原始百分比，不套用十档映射。

公开脚本依据（2026-11-02 读取）：

- [键盘协议与 getBattery 解析](https://www.mchose.com.cn/assets/purify.es-oHexKX_U.js)：
  `dJe`、`getBattery`、`getBatteryWirelessParser`；无线命令为 `13 4A`，电量后为状态位域。
- [首页电量显示](https://www.mchose.com.cn/assets/index-D9ufMCB0.js)：
  `x3` 对 `batteryPrecision === 1` 使用原值，否则查分档映射。
- [电量分档表](https://www.mchose.com.cn/assets/theme-BwiC_T0j.js)：
  `Sye` / `BATTERY_RANGE_TO_LEVEL` 将 `75-84` 映射为 `80`。

GX87 空闲后可能不应答。此次在按键唤醒后，连续 11 次查询收到相同的有效电量帧。
超时后界面保留上次电量并变灰；按键唤醒后点击刷新可重新查询。

`3554:FA09` 并非 GX87 独占 ID。设备没有提供具体产品名称时显示 `2.4G Keyboard`，
附带的型号配置只适用于已确认的 GX87，不用 USB ID 推断所有接收器的型号。

## K7 V2 Ultra+ 的电量回复

接收器同时暴露 61 字节和 64 字节的厂商集合。本适配器使用已实测的
64 字节 `FF01:0001` 通道，经 `WriteFile` 发送 `0x0900` 设备信息查询。

```text
查询前缀：4D 01 01 00 00 09 00 00 08
回复前缀：4D 01 01 10 00 09 00 00 37 38 26 40 04 00 00 00 00 10 02 00 29 00 08 E4 A2
```

两帧均补零至 64 字节。回复 `[20]=0x29`，即 41%；`[24]=0xA2` 为 `[2..23]` 的异或校验。
回复中的鼠标本体 ID 为 `3837:4026`，与 USB 接收器 ID 不同。
解析检查协议头、命令、有效负载长度、身份和电量范围。鼠标的充电状态暂未确认，界面显示「充电状态未知」。

此实现依据公开协议事实独立编写，未复制参考项目的驱动实现：

- [MCHOSE Tray 协议记录](https://github.com/mitsimi/mchose-tray/blob/main/PROTOCOL.md)
- [Offline Mchose 协议记录](https://github.com/Rapou7/offline-mchose/blob/master/REVERSE_ENGINEERING_MCHOSE.md)

## 构建与验证

```powershell
.\tests\Test-Keyboard.ps1
.\tests\Test-MchoseMouse.ps1
.\build.ps1
.\Voltide-Test.exe --test
.\pack.ps1 -Target .\dist
```

协议测试包含实机报文、0%/100%、无效电量、短帧、错误命令、错误校验、设备发现与型号覆盖。
HID 异步读取固定缓冲区，并在超时取消后等待 I/O 结束，避免驱动继续使用已释放的内存。
已在本机执行 30 次读取超时/取消及 GC 循环，再完成两个设备的实际读取。

源码仓库不附带上游发布包中的字体。本构建使用系统字体回退，运行依赖仍是 Windows 自带的 .NET Framework 4.8。
