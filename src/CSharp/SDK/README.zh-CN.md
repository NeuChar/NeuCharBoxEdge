# NeuCharBoxEdge SDK

[English](README.md) | 简体中文

边缘设备 SDK，用于 NeuCharBox 边缘设备的开发和管理。

## 📋 项目简介

NeuCharBoxEdge SDK 是一个基于 .NET 8.0 的边缘设备开发套件，提供了完整的边缘设备管理、OTA（Over-The-Air）更新、蓝牙通信、WiFi 配置等功能。本 SDK 基于 NeuCharFramework (NCF) 架构开发，采用模块化设计，易于扩展。

## ✨ 主要特性

### 🔄 EdgeOTA - OTA 更新模块
- 支持后端固件（Backend）和前端固件（Frontend）的在线更新
- 自动检测版本更新
- 进程管理和自动重启
- 详细的日志记录

### 📱 边缘设备管理
- **蓝牙通信**：支持蓝牙设备发现、连接和数据传输
- **WiFi 配置**：通过蓝牙配置 WiFi 连接
- **网络管理**：WiFi 扫描、连接管理
- **设备认证**：基于 RSA 的加密通信和设备认证
- **心跳保活**：与中心设备的定期通信
- **SignalR 实时通信**：与云端的实时数据交互
- **MCP 协议支持**：Model Context Protocol 集成

### 🔐 安全特性
- RSA 公钥/私钥加密
- 数字签名验证
- 安全的设备配对流程
- Token 认证机制

## 📦 项目结构

```
SDK/
├── EdgeOTA/                              # OTA 更新模块
│   ├── Entity/                           # 实体类
│   │   ├── OTAConfig.cs                  # OTA 配置
│   │   └── OTAEdgeConfig.cs              # 边缘设备 OTA 配置
│   ├── Request/                          # 请求类
│   │   └── OTARequest.cs                 # OTA 请求定义
│   ├── Response/                         # 响应类
│   │   ├── OTABaseResponse.cs            # 基础响应
│   │   ├── OTAResponse.cs                # OTA 响应
│   │   └── CheckForUpdateResponse.cs     # 更新检查响应
│   ├── OTAHelper.cs                      # OTA 辅助工具类
│   └── Program.cs                        # OTA 程序入口
│
├── Senparc.Xncf.NeuCharBoxEdgeSimp/      # 边缘设备 SDK 主模块
│   ├── Domain/                           # 领域层
│   │   ├── Attributes/                   # 特性定义
│   │   │   └── EdgeDataPushAttribute.cs  # 边缘数据推送特性
│   │   ├── BackgroundServices/           # 后台服务
│   │   │   ├── EdgeBackgroundService.cs         # 边缘设备后台服务
│   │   │   ├── EdgeOTABackgroundService.cs      # OTA 检查后台服务
│   │   │   ├── BluetoothBackgroundService.cs    # 蓝牙管理服务
│   │   │   └── WifiBackgroundService.cs         # WiFi 管理服务
│   │   ├── Models/                       # 数据模型
│   │   │   ├── DatabaseModel/            # 数据库模型
│   │   │   ├── MultipleDatabase/         # 多数据库支持
│   │   │   ├── Objects/                  # 对象模型
│   │   │   └── SenderReceiverSet.cs      # 收发配置
│   │   ├── Migrations/                   # 数据库迁移
│   │   │   ├── MySql/                    # MySQL 迁移
│   │   │   ├── SqlServer/                # SQL Server 迁移
│   │   │   ├── PostgreSQL/               # PostgreSQL 迁移
│   │   │   ├── Sqlite/                   # SQLite 迁移
│   │   │   └── Oracle/                   # Oracle 迁移
│   │   └── Services/                     # 领域服务
│   │       ├── Auth/                     # 认证服务
│   │       ├── Bluetooth/                # 蓝牙服务
│   │       └── Crypto/                   # 加密服务
│   │           └── CryptoService.cs      # 加密服务实现
│   ├── Helper/                           # 辅助工具类
│   │   ├── CertHepler.cs                 # 证书和加密辅助
│   │   ├── HttpClientHelper.cs           # HTTP 客户端辅助
│   │   └── IpHelper.cs                   # IP 地址辅助
│   ├── OHS/                              # OHS 协议层
│   │   ├── Local/                        # 本地通信协议
│   │   │   └── PL/                       # 协议层定义
│   │   │       ├── BluetoothMsg.cs       # 蓝牙消息
│   │   │       └── KeepAliveRequest.cs   # 心跳请求
│   │   └── Remote/                       # 远程通信协议
│   ├── ACL/                              # ACL 访问控制层
│   ├── Areas/                            # MVC 区域
│   │   └── Admin/                        # 管理后台
│   │       ├── Controllers/              # 控制器
│   │       └── Pages/                    # Razor 页面
│   ├── App_Data/                         # 应用数据
│   │   └── Database/                     # 数据库配置
│   │       └── SenparcConfig.config      # Senparc 配置
│   ├── CenterDefinition.cs               # 中心配置定义
│   ├── ProgramExtensions.cs              # 程序扩展
│   ├── Register.cs                       # 模块注册
│   ├── Register.Area.cs                  # 区域注册
│   ├── Register.Database.cs              # 数据库注册
│   └── Register.Thread.cs                # 线程注册
│
├── Examples/                             # 示例项目
│   └── EdgeLed/                          # LED 控制示例
│       ├── Controllers/                  # API 控制器
│       │   └── EdgeLedController.cs      # LED 控制器
│       ├── Services/                     # 业务服务
│       │   └── TM1637DisplayService.cs   # TM1637 显示服务
│       ├── App_Data/                     # 应用数据
│       ├── appsettings.json              # 应用配置
│       ├── Program.cs                    # 程序入口
│       └── Register.cs                   # 模块注册
│
├── NeuCharBoxEdge.sln                    # 解决方案文件
└── README.md                             # 项目文档
```

## 🚀 快速开始

### 开发环境要求

- .NET 8.0 SDK 或更高版本
- 支持的操作系统：Windows / Linux / macOS

## 📱 示例项目

### EdgeLed 示例

`Examples/EdgeLed` 项目展示了如何使用 SDK 创建一个边缘设备应用，该应用：

- 集成了 NeuCharBoxEdge SDK
- 实现了 LED 显示控制（TM1637）
- 提供了 RESTful API 接口
- 支持蓝牙和 WiFi 通信
- 支持 OTA 更新

### 主要功能

- **LED 控制**：通过 API 控制 TM1637 数码管显示
- **设备通信**：通过蓝牙与中心设备通信
- **数据推送**：使用 `EdgeDataPush` 特性自动推送数据

## 🤝 贡献

欢迎提交 Issue 和 Pull Request！

## 📄 许可证

本项目采用 [Apache-2.0](https://www.apache.org/licenses/LICENSE-2.0) 许可证。

## 📧 联系方式

- 官方网站：https://www.neuchar.com
- 组织：Senparc

## 🔗 相关链接

- [NeuCharFramework (NCF)](https://github.com/NeuCharFramework/NCF)
- [Senparc.CO2NET](https://github.com/Senparc/Senparc.CO2NET)

---

**⭐ 如果这个项目对你有帮助，请给我们一个 Star！**

