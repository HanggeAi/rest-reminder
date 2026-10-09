# 休息提醒 (RestReminder)

一个 Windows 桌面小工具：工作每隔 n 分钟提醒你休息，午饭 / 晚饭时间提醒你吃饭。

## 功能

- **工作计时**：点击「开始工作」后才开始计时，「暂停工作」停止计时，再次「开始工作」重新计时
- **实时倒计时**：主页显示距下次休息还有多久（每秒刷新）
- **定时休息提醒**：到点弹窗 + 声音提醒，间隔可在设置中调整（默认 45 分钟）
- **吃饭提醒**：午饭默认 11:30，晚饭默认 17:00，每天各提醒一次（与工作计时无关，按时刻触发）
- **声音提示**：弹出提醒时播放系统提示音（可在设置中关闭）
- 弹窗停留 **3 分钟**后自动关闭，也可点「知道了」或按 Esc 关闭
- **设置窗口**：所有可调项（休息间隔、饭点、提示音、开机自启）集中在「设置」中，修改立即生效并自动保存
- **开机自启**：设置中勾选即写入当前用户注册表（HKCU Run 键），无需管理员权限
- **下班**：点击后弹窗确认，确认即退出软件
- 关闭窗口即最小化到系统托盘，后台继续计时；托盘图标右键可打开设置或退出

## 运行

直接双击 `RestReminder.exe` 即可。

无需安装任何运行库——程序基于 Windows 自带的 .NET Framework 4.x，
Windows 7 SP1 及以上系统均内置，可直接拷贝到其他电脑运行。

## 从源码构建

先确保 `xiu.png`（程序图标原图）已转换为 `xiu.ico`（仓库已附带；如需重新生成可编译运行 `png2ico.cs`），然后双击 `build.bat`，或命令行执行：

```
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /win32icon:xiu.ico /resource:xiu.ico /out:RestReminder.exe /optimize+ /codepage:65001 /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll RestReminder.cs
```

不需要 Visual Studio，任何 Windows 电脑都能编译。
