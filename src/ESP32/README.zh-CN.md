# NeuCharBoxEdge ESP32 SDK

[English](README.md) | 简体中文

ESP32 固件 SDK，用于将 ESP32 设备接入 NeuChar 边缘盒子（NeuCharBox）及云端平台。

## 📋 项目简介

NeuCharBoxEdge ESP32 SDK 是专为 ESP32 微控制器设计的固件开发套件，提供了完整的设备配网、云端/本地双通道通信、MCP（Model Context Protocol）工具框架、OTA 在线升级等功能。只需几行代码，即可将 ESP32 设备的能力以 AI Tool 的形式暴露给大语言模型进行智能控制。

## ✨ 主要特性

### 🔌 双通道通信
- **云端通道（NcCloudTransport）**：通过 SignalR over WebSocket 连接 NeuChar 云服务，实现远程控制
- **本地通道（NcLocalTransport）**：连接局域网内的 NeuCharBox（NCB）设备，100ms 高频推送实时数据

### 🛠️ MCP 工具框架（NcMcp）
- 以 **Tool / Action** 模型注册设备能力
- 能力描述自动序列化为三种标准格式：
  - 云端 UI 格式（`DevicePoolFunctionInterfaces`）
  - OpenAI Function Calling 格式
  - MCP JSON-RPC 格式（`tools/list` / `tools/call`）
- 支持 `isRealData` 标记高频实时数据 Action

### 📡 配网管理（NcProvisioning）
- **蓝牙配网**：开机后 3 秒自动启动蓝牙，WiFi 连接成功后自动关闭以释放内存；**双击 BOOT 键**可重启设备并保持蓝牙活跃，用于重新配网
- **AP 门户配网**：**长按 BOOT 键 2 秒**进入 AP 模式，内置 HTML 配网页面，支持 DNS 劫持（Captive Portal）
- **WiFi 历史记录**：NVS 自动保存最多 10 条 WiFi 历史
- **MCP HTTP 服务端**：内置 `GET /edgemcp/sse` + `POST /edgemcp/messages` 接口

### 🔄 OTA 在线升级（NcOTA）
- 每 60 秒向 NCB 查询固件版本
- 流式下载并写入 Flash，支持最多 3 次重试
- 升级成功后自动更新 NVS 版本号并重启

### 🔐 安全特性（NcCrypto）
- 基于 mbedtls 的 RSA-SHA256 签名
- RSA OAEP（SHA256）解密
- 自动修复 PEM 格式（支持 PKCS#1 / PKCS#8）
- FreeRTOS 互斥锁保障多任务安全

## 📦 项目结构

```
ESP32/
├── libraries/
│   └── NeuCharEdgeSDK/           # SDK 核心库
│       ├── NeuCharEdge.h         # 顶层单例入口（NcEdge）
│       ├── NeuCharEdge.cpp       # NcEdge 单例实例化
│       ├── NcTransport.h         # WebSocket 传输层（Cloud + Local）
│       ├── NcMcp.h               # MCP 工具框架（Tool / Action 注册）
│       ├── NcProvisioning.h      # 配网管理（蓝牙 / AP / MCP HTTP）
│       ├── NcProvisioning.cpp    # 配网管理实现
│       ├── NcOTA.h               # OTA 在线升级
│       ├── NcCrypto.h            # RSA 加密/签名声明
│       └── NcCrypto.cpp          # RSA 加密/签名实现
│
└── EdgeTube/                     # 示例项目：四位数字管控制
    └── EdgeTube.ino
```

## 🚀 快速开始

### 开发环境要求

- Arduino IDE 2.x 或 PlatformIO
- ESP32 Arduino Core（`espressif/arduino-esp32`）
- 依赖库：
  - `WebSocketsClient`（arduinoWebSockets）
  - `ArduinoJson` >= 6.x
  - mbedtls（ESP32 Arduino Core 内置）

### 安装 SDK

将 `libraries/NeuCharEdgeSDK/` 文件夹复制到 Arduino 的 `libraries` 目录下。

### Arduino IDE 编译与烧录

#### 安装 ESP32 开发板支持

在 Arduino IDE 的「开发板管理器」中搜索 `esp32`，安装 **Espressif Systems** 提供的 `esp32` 包，版本选择 **2.0.17**。

#### 开发板配置

打开 Arduino IDE，选择菜单 **工具**，按照下表进行配置：

| 配置项 | 推荐值 |
|---|---|
| 开发板 | `ESP32 Dev Module` |
| CPU Frequency | `240MHz (WiFi/BT)` |
| Core Debug Level | `None` |
| Erase All Flash Before Sketch Upload | `Disabled` |
| Events Run On | `Core 1` |
| Flash Frequency | `80MHz` |
| Flash Mode | `QIO` |
| Flash Size | `4MB (32Mb)` |
| JTAG Adapter | `Disabled` |
| Arduino Runs On | `Core 1` |
| Partition Scheme | `Minimal SPIFFS (1.9MB APP with OTA/190KB SPIFFS)` |
| PSRAM | `Disabled` |
| Upload Speed | `921600` |
| 端口 | 设备实际连接的串口（如 `COM8`） |

> **Partition Scheme** 建议选择 `Minimal SPIFFS (1.9MB APP with OTA/190KB SPIFFS)`，为 OTA 升级预留足够的分区空间。
>
> **清除 NVS 分区**：若需要清除设备中已保存的 WiFi 凭据、设备身份等 NVS 数据（例如重新配网或更换设备身份），烧录时将 `Erase All Flash Before Sketch Upload` 改为 `Enabled`，烧录完成后恢复为 `Disabled` 即可。

#### 串口监视器

波特率设置为 **115200**，用于查看设备运行日志与调试输出。

### 极简接入示例

```cpp
#include <NeuCharEdge.h>

// 自定义 Tool（继承 NcMcpTool）
class MyTool : public NcMcpTool {
public:
    MyTool() : NcMcpTool("MyDeviceName") {
        registerAction("GetTemperature", "读取温度", "",
            [](JsonObject params) -> String {
                return "{\"value\": 25.6}";
            }, true);   // isRealData=true：高频实时推送
    }
};

MyTool myTool;

void setup() {
    NcEdge.registerTool(myTool);
    NcEdge.begin(
        "MyDevice",          // 设备名称
        "your-did",          // 设备 ID（Device ID）
        "your-uid",          // 用户 ID
        "-----BEGIN RSA PRIVATE KEY-----\n...",  // RSA 私钥
        0,                   // BOOT 按键 GPIO（0 = 禁用 AP 配网）
        true                 // 是否连接云端
    );
}

void loop() {
    NcEdge.run();
}
```

### 高级 API

| 方法 | 说明 |
|---|---|
| `NcEdge.begin(name, did, uid, key, pin, cloud)` | 一键初始化（推荐） |
| `NcEdge.run()` | 放入 `loop()`，驱动所有后台逻辑 |
| `NcEdge.registerTool(tool)` | 注册 MCP Tool |
| `NcEdge.getCloud()` | 获取云端传输对象 |
| `NcEdge.getLocal()` | 获取本地传输对象 |

## 🔧 自定义 MCP Tool

### 注册 Action

```cpp
registerAction(
    "ActionName",        // Action 名称
    "动作描述",           // 描述（供 AI 理解）
    paramSchema,         // 参数 Schema（JSON 字符串，可为空）
    callback,            // 执行回调
    false                // isRealData：true 表示高频实时推送
);
```

### 参数 Schema 格式

```cpp
// 简单格式（参数名: 类型字符串）
String schema = R"({"number":"string","mode":"string"})";

// 扩展格式（含描述）
String schema = R"({
    "number": {"type": "string", "description": "要显示的数字，范围 -999~9999"},
    "mode":   {"type": "string", "description": "显示模式"}
})";
```

## ⚙️ 可配置常量

在 `#include <NeuCharEdge.h>` 之前通过 `#define` 覆盖默认值：

| 常量 | 默认值 | 说明 |
|---|---|---|
| `NC_CLOUD_HOST` | `"www.neuchar.com"` | 云端服务器地址 |
| `NC_CLOUD_PORT` | `80` | 云端端口 |
| `NC_CLOUD_SSL` | `false` | 是否启用 SSL |
| `NC_CLOUD_AUTO_FALLBACK_SSL` | `true` | 自动回退 wss:443 |
| `NC_FIRMWARE_VERSION` | `"1.0.0"` | 固件版本（用于 OTA 比较） |
| `NC_WS_RECONNECT_INTERVAL_MS` | `5000` | WebSocket 重连间隔（ms） |
| `NC_WS_HEARTBEAT_INTERVAL_MS` | `10000` | 心跳间隔（ms） |
| `NC_WS_HEARTBEAT_TIMEOUT_MS` | `3000` | 心跳超时（ms） |
| `NC_WS_KEEPALIVE_INTERVAL_MS` | `10000` | Keep-Alive 发送间隔（ms） |
| `NC_LOCAL_START_DEFER_TIMEOUT_MS` | `8000` | Local 等待 Cloud 握手的最长时间（ms） |

## 🗄️ NVS 存储说明

SDK 使用 ESP32 的 NVS（Non-Volatile Storage）持久化以下数据：

| 命名空间 | 键 | 说明 |
|---|---|---|
| `nc_identity` | `did` / `uid` / `pkey` | 设备身份信息（首次烧录写入，OTA 后保持） |
| `nc_config_v2` | `ssid` / `password` / `ncb_host` | WiFi 凭据 + NCB 主机 IP |
| `nc_wifi_hist` | `h{i}_s` / `h{i}_p` | WiFi 历史记录（最多 10 条） |
| `nc_ota` | `fw_ver` | 当前固件版本号 |

> 身份信息优先级：NVS 已存值 > 代码传入值 > 空值。首次烧录时将代码中的 `did/uid/pkey` 写入 NVS；OTA 升级后 NVS 值保持不变，新固件无需硬编码凭据。

## 📱 示例项目：EdgeTube 数字管控制

`EdgeTube/EdgeTube.ino` 展示了完整的 SDK 接入流程，实现了一个由 AI 控制的 TM1637 四位数字管。

### 功能说明

- **显示数字**：接收 AI 指令，在数字管上显示 -999 ~ 9999 范围的整数
- **清空显示**：清除数字管内容
- **实时数据**：每 100ms 向本地通道推送当前显示值

### 引脚配置

```cpp
#define TUBE_CLK_PIN  18   // TM1637 时钟引脚
#define TUBE_DIO_PIN  19   // TM1637 数据引脚
#define BOOT_BTN       0   // BOOT 按键（双击重启进入蓝牙配网；长按 2s 进入 AP 配网）
```

### BOOT 按键操作说明

| 操作 | 触发条件 | 行为 |
|---|---|---|
| 无操作 | 正常开机 | 3 秒后自动启动蓝牙；WiFi 连接成功后自动关闭蓝牙以释放内存 |
| 双击 | 500ms 内连续按下两次 | 重启设备并保持蓝牙持续活跃，等待蓝牙配网（重新设置 WiFi / NCB 主机） |
| 长按 2 秒 | 持续按住 ≥ 2s | 进入 AP 模式，设备热点名为 `NCBEdge_{DID后6位}`，浏览器访问 `192.168.4.1` 配网 |

### 注册的 MCP Actions

| Action | 参数 | 说明 |
|---|---|---|
| `DisplayNumber` | `number`（字符串） | 在数字管上显示数字 |
| `Clear` | 无 | 清空数字管 |
| `GetCurrentDisplay` | 无 | 获取当前显示值（实时推送） |

## 🤝 贡献

欢迎提交 Issue 和 Pull Request！

## 📄 许可证

本项目采用 [Apache-2.0](https://www.apache.org/licenses/LICENSE-2.0) 许可证。

## 📧 联系方式

- 官方网站：https://www.neuchar.com
- 组织：Senparc

**⭐ 如果这个项目对你有帮助，请给我们一个 Star！**
