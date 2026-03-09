# NeuCharBoxEdge ESP32 SDK

English | [简体中文](README.zh-CN.md)

ESP32 firmware SDK for connecting ESP32 devices to NeuCharBox (NCB) and the NeuChar cloud platform.

## 📋 Overview

NeuCharBoxEdge ESP32 SDK is a firmware development kit designed specifically for ESP32 microcontrollers. It provides a complete set of features including device provisioning, dual-channel cloud/local communication, an MCP (Model Context Protocol) tool framework, and OTA over-the-air firmware upgrades. With just a few lines of code, you can expose your ESP32 device's capabilities as AI Tools for intelligent control by large language models.

## ✨ Key Features

### 🔌 Dual-Channel Communication
- **Cloud Channel (NcCloudTransport)**: Connects to the NeuChar cloud service via SignalR over WebSocket for remote control
- **Local Channel (NcLocalTransport)**: Connects to a NeuCharBox (NCB) on the local network, pushing real-time data at 100ms intervals

### 🛠️ MCP Tool Framework (NcMcp)
- Register device capabilities using a **Tool / Action** model
- Capability descriptions are automatically serialized into three standard formats:
  - Cloud UI format (`DevicePoolFunctionInterfaces`)
  - OpenAI Function Calling format
  - MCP JSON-RPC format (`tools/list` / `tools/call`)
- Supports `isRealData` flag to mark high-frequency real-time data Actions

### 📡 Provisioning Management (NcProvisioning)
- **Bluetooth Provisioning**: Receives encrypted WiFi credentials via Bluetooth Serial
- **AP Portal Provisioning**: Long-press the BOOT button to enter AP mode with a built-in HTML configuration page
- **WiFi History**: Automatically saves up to 10 WiFi entries in NVS
- **MCP HTTP Server**: Built-in `GET /edgemcp/sse` and `POST /edgemcp/messages` endpoints

### 🔄 OTA Firmware Upgrade (NcOTA)
- Queries NCB for firmware version every 60 seconds
- Streams firmware download directly to Flash with up to 3 retries
- Automatically updates the NVS version number and restarts upon success

### 🔐 Security Features (NcCrypto)
- RSA-SHA256 signing powered by mbedtls
- RSA OAEP (SHA256) decryption
- Automatic PEM format repair (supports PKCS#1 / PKCS#8)
- FreeRTOS mutex for multi-task safety

## 📦 Project Structure

```
ESP32/
├── libraries/
│   └── NeuCharEdgeSDK/           # SDK core library
│       ├── NeuCharEdge.h         # Top-level singleton entry (NcEdge)
│       ├── NeuCharEdge.cpp       # NcEdge singleton instantiation
│       ├── NcTransport.h         # WebSocket transport layer (Cloud + Local)
│       ├── NcMcp.h               # MCP tool framework (Tool / Action registration)
│       ├── NcProvisioning.h      # Provisioning manager (Bluetooth / AP / MCP HTTP)
│       ├── NcProvisioning.cpp    # Provisioning manager implementation
│       ├── NcOTA.h               # OTA firmware upgrade
│       ├── NcCrypto.h            # RSA encryption/signing declarations
│       └── NcCrypto.cpp          # RSA encryption/signing implementation
│
└── EdgeTube/                     # Example project: 4-digit tube display control
    └── EdgeTube.ino
```

## 🚀 Quick Start

### Requirements

- Arduino IDE 2.x or PlatformIO
- ESP32 Arduino Core (`espressif/arduino-esp32`)
- Dependencies:
  - `WebSocketsClient` (arduinoWebSockets)
  - `ArduinoJson` >= 6.x
  - mbedtls (built into ESP32 Arduino Core)

### Install the SDK

Copy the `libraries/NeuCharEdgeSDK/` folder into your Arduino `libraries` directory.

### Minimal Example

```cpp
#include <NeuCharEdge.h>

// Define a custom Tool (inherit from NcMcpTool)
class MyTool : public NcMcpTool {
public:
    MyTool() : NcMcpTool("MyDeviceName") {
        registerAction("GetTemperature", "Read current temperature", "",
            [](JsonObject params) -> String {
                return "{\"value\": 25.6}";
            }, true);   // isRealData=true: push as high-frequency real-time data
    }
};

MyTool myTool;

void setup() {
    NcEdge.registerTool(myTool);
    NcEdge.begin(
        "MyDevice",          // Device name
        "your-did",          // Device ID
        "your-uid",          // User ID
        "-----BEGIN RSA PRIVATE KEY-----\n...",  // RSA private key
        0,                   // BOOT button GPIO (0 = disable AP provisioning)
        true                 // Enable cloud connection
    );
}

void loop() {
    NcEdge.run();
}
```

### Advanced API

| Method | Description |
|---|---|
| `NcEdge.begin(name, did, uid, key, pin, cloud)` | One-call initialization (recommended) |
| `NcEdge.run()` | Call in `loop()` to drive all background logic |
| `NcEdge.registerTool(tool)` | Register an MCP Tool |
| `NcEdge.getCloud()` | Access the cloud transport object |
| `NcEdge.getLocal()` | Access the local transport object |

## 🔧 Custom MCP Tool

### Registering an Action

```cpp
registerAction(
    "ActionName",        // Action name
    "Action description",// Description (used by AI to understand the action)
    paramSchema,         // Parameter schema (JSON string, can be empty)
    callback,            // Execution callback
    false                // isRealData: true = push as high-frequency real-time data
);
```

### Parameter Schema Formats

```cpp
// Simple format (param name: type string)
String schema = R"({"number":"string","mode":"string"})";

// Extended format (with descriptions)
String schema = R"({
    "number": {"type": "string", "description": "Number to display, range -999~9999"},
    "mode":   {"type": "string", "description": "Display mode"}
})";
```

## ⚙️ Configurable Constants

Override defaults with `#define` before `#include <NeuCharEdge.h>`:

| Constant | Default | Description |
|---|---|---|
| `NC_CLOUD_HOST` | `"www.neuchar.com"` | Cloud server hostname |
| `NC_CLOUD_PORT` | `80` | Cloud server port |
| `NC_CLOUD_SSL` | `false` | Enable SSL for cloud connection |
| `NC_CLOUD_AUTO_FALLBACK_SSL` | `true` | Auto-fallback to wss:443 |
| `NC_FIRMWARE_VERSION` | `"1.0.0"` | Firmware version (used for OTA comparison) |
| `NC_WS_RECONNECT_INTERVAL_MS` | `5000` | WebSocket reconnect interval (ms) |
| `NC_WS_HEARTBEAT_INTERVAL_MS` | `10000` | Heartbeat interval (ms) |
| `NC_WS_HEARTBEAT_TIMEOUT_MS` | `3000` | Heartbeat timeout (ms) |
| `NC_WS_KEEPALIVE_INTERVAL_MS` | `10000` | Keep-Alive send interval (ms) |
| `NC_LOCAL_START_DEFER_TIMEOUT_MS` | `8000` | Max time for Local to wait for Cloud handshake (ms) |

## 🗄️ NVS Storage

The SDK uses ESP32 NVS (Non-Volatile Storage) to persist the following data:

| Namespace | Keys | Description |
|---|---|---|
| `nc_identity` | `did` / `uid` / `pkey` | Device identity (written on first flash, retained after OTA) |
| `nc_config_v2` | `ssid` / `password` / `ncb_host` | WiFi credentials + NCB host IP |
| `nc_wifi_hist` | `h{i}_s` / `h{i}_p` | WiFi history (up to 10 entries) |
| `nc_ota` | `fw_ver` | Current firmware version |

> **Identity priority:** NVS stored value > value passed in code > empty. On first flash, the `did/uid/pkey` values in your sketch are written to NVS. After an OTA upgrade, NVS values are preserved — the new firmware does not need hardcoded credentials.

## 📱 Example Project: EdgeTube Display Control

`EdgeTube/EdgeTube.ino` demonstrates a complete SDK integration, implementing an AI-controlled TM1637 4-digit tube display.

### Features

- **Display Number**: Receives AI commands to display integers in the range -999 ~ 9999
- **Clear Display**: Clears the tube display
- **Real-time Data**: Pushes the current display value to the local channel every 100ms

### Pin Configuration

```cpp
#define TUBE_CLK_PIN  18   // TM1637 clock pin
#define TUBE_DIO_PIN  19   // TM1637 data pin
#define BOOT_BTN       0   // BOOT button (long-press 2s to enter AP provisioning)
```

### Registered MCP Actions

| Action | Parameters | Description |
|---|---|---|
| `DisplayNumber` | `number` (string) | Display a number on the tube |
| `Clear` | none | Clear the tube display |
| `GetCurrentDisplay` | none | Get the current display value (real-time push) |

## 🤝 Contributing

Issues and Pull Requests are welcome!

## 📄 License

This project is licensed under the [Apache-2.0](https://www.apache.org/licenses/LICENSE-2.0) License.

## 📧 Contact

- Official Website: https://www.neuchar.com
- Organization: Senparc

**⭐ If this project helps you, please give us a Star!**
