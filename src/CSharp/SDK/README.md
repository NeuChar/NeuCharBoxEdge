# NeuCharBoxEdge SDK

English | [简体中文](README.zh-CN.md)

Edge device SDK for NeuCharBox edge device development and management.

## 📋 Overview

NeuCharBoxEdge SDK is a .NET 8.0-based edge device development kit that provides comprehensive edge device management, OTA (Over-The-Air) updates, Bluetooth communication, WiFi configuration, and more. Built on the NeuCharFramework (NCF) architecture, this SDK adopts a modular design for easy extensibility.

## ✨ Key Features

### 🔄 EdgeOTA - OTA Update Module
- Supports online updates for both Backend and Frontend firmware
- Automatic version update detection
- Process management and auto-restart
- Detailed logging

### 📱 Edge Device Management
- **Bluetooth Communication**: Device discovery, connection, and data transmission
- **WiFi Configuration**: Configure WiFi connections via Bluetooth
- **Network Management**: WiFi scanning and connection management
- **Device Authentication**: RSA-based encrypted communication and device authentication
- **Keep-Alive**: Regular communication with central devices
- **SignalR Real-time Communication**: Real-time data interaction with the cloud
- **MCP Protocol Support**: Model Context Protocol integration

### 🔐 Security Features
- RSA public/private key encryption
- Digital signature verification
- Secure device pairing process
- Token authentication mechanism

## 📦 Project Structure

```
SDK/
├── EdgeOTA/                              # OTA Update Module
│   ├── Entity/                           # Entity Classes
│   │   ├── OTAConfig.cs                  # OTA Configuration
│   │   └── OTAEdgeConfig.cs              # Edge Device OTA Configuration
│   ├── Request/                          # Request Classes
│   │   └── OTARequest.cs                 # OTA Request Definition
│   ├── Response/                         # Response Classes
│   │   ├── OTABaseResponse.cs            # Base Response
│   │   ├── OTAResponse.cs                # OTA Response
│   │   └── CheckForUpdateResponse.cs     # Update Check Response
│   ├── OTAHelper.cs                      # OTA Helper Utilities
│   └── Program.cs                        # OTA Program Entry
│
├── Senparc.Xncf.NeuCharBoxEdgeSimp/      # Main Edge Device SDK Module
│   ├── Domain/                           # Domain Layer
│   │   ├── Attributes/                   # Attribute Definitions
│   │   │   └── EdgeDataPushAttribute.cs  # Edge Data Push Attribute
│   │   ├── BackgroundServices/           # Background Services
│   │   │   ├── EdgeBackgroundService.cs         # Edge Device Background Service
│   │   │   ├── EdgeOTABackgroundService.cs      # OTA Check Background Service
│   │   │   ├── BluetoothBackgroundService.cs    # Bluetooth Management Service
│   │   │   └── WifiBackgroundService.cs         # WiFi Management Service
│   │   ├── Models/                       # Data Models
│   │   │   ├── DatabaseModel/            # Database Models
│   │   │   ├── MultipleDatabase/         # Multiple Database Support
│   │   │   ├── Objects/                  # Object Models
│   │   │   └── SenderReceiverSet.cs      # Sender/Receiver Configuration
│   │   ├── Migrations/                   # Database Migrations
│   │   │   ├── MySql/                    # MySQL Migrations
│   │   │   ├── SqlServer/                # SQL Server Migrations
│   │   │   ├── PostgreSQL/               # PostgreSQL Migrations
│   │   │   ├── Sqlite/                   # SQLite Migrations
│   │   │   └── Oracle/                   # Oracle Migrations
│   │   └── Services/                     # Domain Services
│   │       ├── Auth/                     # Authentication Services
│   │       ├── Bluetooth/                # Bluetooth Services
│   │       └── Crypto/                   # Encryption Services
│   │           └── CryptoService.cs      # Encryption Service Implementation
│   ├── Helper/                           # Helper Utilities
│   │   ├── CertHepler.cs                 # Certificate and Encryption Helper
│   │   ├── HttpClientHelper.cs           # HTTP Client Helper
│   │   └── IpHelper.cs                   # IP Address Helper
│   ├── OHS/                              # OHS Protocol Layer
│   │   ├── Local/                        # Local Communication Protocol
│   │   │   └── PL/                       # Protocol Layer Definitions
│   │   │       ├── BluetoothMsg.cs       # Bluetooth Messages
│   │   │       └── KeepAliveRequest.cs   # Keep-Alive Request
│   │   └── Remote/                       # Remote Communication Protocol
│   ├── ACL/                              # ACL Access Control Layer
│   ├── Areas/                            # MVC Areas
│   │   └── Admin/                        # Admin Dashboard
│   │       ├── Controllers/              # Controllers
│   │       └── Pages/                    # Razor Pages
│   ├── App_Data/                         # Application Data
│   │   └── Database/                     # Database Configuration
│   │       └── SenparcConfig.config      # Senparc Configuration
│   ├── CenterDefinition.cs               # Central Configuration Definition
│   ├── ProgramExtensions.cs              # Program Extensions
│   ├── Register.cs                       # Module Registration
│   ├── Register.Area.cs                  # Area Registration
│   ├── Register.Database.cs              # Database Registration
│   └── Register.Thread.cs                # Thread Registration
│
├── Examples/                             # Example Projects
│   └── EdgeLed/                          # LED Control Example
│       ├── Controllers/                  # API Controllers
│       │   └── EdgeLedController.cs      # LED Controller
│       ├── Services/                     # Business Services
│       │   └── TM1637DisplayService.cs   # TM1637 Display Service
│       ├── App_Data/                     # Application Data
│       ├── appsettings.json              # Application Configuration
│       ├── Program.cs                    # Program Entry
│       └── Register.cs                   # Module Registration
│
├── NeuCharBoxEdge.sln                    # Solution File
└── README.md                             # Project Documentation
```

## 🚀 Quick Start

### Requirements

- .NET 8.0 SDK or higher
- Supported OS: Windows / Linux / macOS

## 📱 Example Projects

### EdgeLed Example

The `Examples/EdgeLed` project demonstrates how to create an edge device application using the SDK. This application:

- Integrates NeuCharBoxEdge SDK
- Implements LED display control (TM1637)
- Provides RESTful API endpoints
- Supports Bluetooth and WiFi communication
- Supports OTA updates

### Key Features

- **LED Control**: Control TM1637 digital display via API
- **Device Communication**: Communicate with central devices via Bluetooth
- **Data Push**: Automatically push data using `EdgeDataPush` attribute

## 🤝 Contributing

Issues and Pull Requests are welcome!

## 📄 License

This project is licensed under the [Apache-2.0](https://www.apache.org/licenses/LICENSE-2.0) License.

## 📧 Contact

- Official Website: https://www.neuchar.com
- Organization: Senparc

## 🔗 Related Links

- [NeuCharFramework (NCF)](https://github.com/NeuCharFramework/NCF)
- [Senparc.CO2NET](https://github.com/Senparc/Senparc.CO2NET)

---

**⭐ If this project helps you, please give us a Star!**
