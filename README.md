# OPPO Pods For Windows

Windows 桌面端 OPPO 耳机蓝牙控制器，支持 Enco Free4 / X3 / Air5 / Air2 Pro 系列。

基于 [Leaf-lsgtky/OppoPods](https://github.com/Leaf-lsgtky/OppoPods) 逆向的 OPPO 私有 RFCOMM 协议，参考 [1812z/OppoPods](https://github.com/1812z/OppoPods) 的功能矩阵实现。

## 功能

- 电量显示（左耳 / 右耳 / 充电盒 + 充电状态）
- 佩戴检测（入盒 / 佩戴 / 摘下）
- 降噪控制（关闭 / 降噪 / 自适应 / 通透），降噪支持智能 / 轻度 / 中度 / 深度子模式
- 空间音效开关（Free4 / Air5）
- 空间音频三模式：关闭 / 固定 / 头部追踪（X3）
- 游戏模式（标准 / 兼容两种实现）
- 双设备连接开关
- 大师调音 EQ（5 种预设，按型号适配）
- 系统托盘常驻，左键单击切换显隐，右键弹出功能菜单
- 关闭到托盘 / 开机自启
- 托盘悬浮提示实时电量
- 连接时弹出 Win11 风格电量提示
- 自动重连
- 暗色主题，跟随系统强调色

## 系统要求

- Windows 10 / 11
- .NET 9.0 Desktop Runtime（[下载](https://dotnet.microsoft.com/zh-cn/download/dotnet/9.0)）
- 蓝牙适配器 + 已配对的 OPPO 耳机

## 快速开始

### 直接运行（无需编译）

下载 [Releases](https://github.com/Zhao-Yi9027/OPPO-Pods-Desktop/releases) 中的 `OppoPodsWPF.exe`，双击运行。

### 从源码编译

```bash
git clone https://github.com/Zhao-Yi9027/OPPO-Pods-Desktop.git
cd OPPO-Pods-Desktop
dotnet run
```

### 发布单文件

```bash
dotnet publish -c Release -r win-x64 -o publish
```

产出 `publish/OppoPodsWPF.exe` 约 170MB（自包含 .NET 运行时）。

## 设备支持

| 型号 | 降噪 | 自适应 | 空间音效 | 空间音频 | 双设备 | 大师调音 |
|:---|:---:|:---:|:---:|:---:|:---:|:---:|
| Enco Free4 | ✅ | ✅ | ✅ | — | ✅ | 5 种 |
| Enco X3 | ✅ | — | — | ✅ | ✅ | 5 种 |
| Enco Air5 | ✅ | — | ✅ | — | — | 5 种 |
| Enco Air2 Pro | ✅ | — | — | — | — | 5 种 |

其他名称包含 "OPPO" 的蓝牙设备可自动连接，使用通用功能集。

## 项目结构

```
OPPO-Pods-Desktop/
├── OppoPodsWPF.csproj    # .NET 9 WPF + WinForms 项目
├── App.xaml/.cs          # 应用入口，主题设置
├── MainWindow.xaml/.cs   # 主界面 + 托盘 + 设置
├── ToastWindow.xaml/.cs  # 连接电量提示弹窗
├── OppoProtocol.cs       # OPPO RFCOMM 协议定义
├── RfcommService.cs      # Winsock2 蓝牙连接与轮询
├── DeviceCapabilities.cs # 设备型号识别与功能矩阵
├── PodState.cs           # 状态数据模型
└── Assets/               # 图标 & 图片素材
```

## 致谢

- [Leaf-lsgtky/OppoPods](https://github.com/Leaf-lsgtky/OppoPods) — OPPO 耳机私有协议逆向
- [1812z/OppoPods](https://github.com/1812z/OppoPods) — 功能实现参考
- [lepoco/wpfui](https://github.com/lepoco/wpfui) — Windows 11 Fluent Design UI 框架

## License

GPL-3.0
