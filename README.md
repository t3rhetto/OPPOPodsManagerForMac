# OPPO Pods Manager for macOS

[中文](README.md) | [English](README_EN.md)

---

macOS 版本的 OPPO / OnePlus / realme 蓝牙耳机管理工具。

## 关于本项目

本项目移植自 [Zhaoyi-ya/OppoPodsManager](https://github.com/Zhaoyi-ya/OppoPodsManager)，仅做了 macOS 平台适配，**原功能不变**。

感谢原作者 [@Zhaoyi-ya](https://github.com/Zhaoyi-ya) 的出色工作。

## 功能

- 查看耳机电量（左耳/右耳/充电盒）
- 切换降噪模式（关闭/降噪/通透/自适应）
- 音效调节（大师调音 EQ）
- 空间音效
- 游戏模式
- 双设备连接管理
- 支持 OPPO / OnePlus / realme 共 137 款设备

## 下载

前往 [Releases](https://github.com/t3rhetto/OppoPodsManager/releases) 下载最新版本：

| 架构 | 文件 | 适用设备 |
|------|------|----------|
| ARM64 | `OppoPodsManager-macOS-arm64.zip` | M1/M2/M3/M4 Mac |
| x64 | `OppoPodsManager-macOS-x64.zip` | Intel Mac |

## 使用方式

1. 下载对应架构的 zip 文件
2. 解压得到 `OppoPodsManager.app`
3. 双击运行，或拖到 `/Applications/` 文件夹

## 系统要求

- macOS 12.0 或更高版本
- 蓝牙适配器
- 已在 macOS 蓝牙设置中配对的 OPPO / OnePlus / realme 耳机

## 注意事项

- 首次运行需要在 **系统设置 → 隐私与安全** 中允许
- 需要蓝牙权限才能连接耳机
- 耳机需先在 macOS 蓝牙设置中完成配对
- 程序只连接已配对的设备，不负责配对流程

## 从源码构建

```bash
# 克隆仓库
git clone https://github.com/t3rhetto/OppoPodsManager.git
cd OppoPodsManager

# 编译 ARM64 版本
./build-macos.sh

# 或编译 x64 版本
./build-macos-x64.sh
```

## 致谢

- [Zhaoyi-ya/OppoPodsManager](https://github.com/Zhaoyi-ya/OppoPodsManager) — 原始项目
- [Leaf-lsgtky/OppoPods](https://github.com/Leaf-lsgtky/OppoPods) — OPPO 耳机私有协议逆向
- [1812z/OppoPods](https://github.com/1812z/OppoPods) — 功能实现参考
- [AvaloniaUI/Avalonia](https://github.com/AvaloniaUI/Avalonia) — 跨平台 UI 框架
- [kikipoulet/SukiUI](https://github.com/kikipoulet/SukiUI) — UI 组件库

## 开源协议

[GPL-3.0](LICENSE)
