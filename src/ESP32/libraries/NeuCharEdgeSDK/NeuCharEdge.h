#ifndef NEUCHAR_EDGE_H
#define NEUCHAR_EDGE_H

#include "NcTransport.h"
#include "NcMcp.h"
#include "NcProvisioning.h"
#include "NcOTA.h"

// ==========================================
// 全局配置参数 (用户可修改)
// ==========================================
// 默认云端服务器地址
#ifndef NC_CLOUD_HOST
#define NC_CLOUD_HOST "www.neuchar.com"
#endif

// 默认云端服务器端口 (80为非SSL，443为SSL)
#ifndef NC_CLOUD_PORT
#define NC_CLOUD_PORT 80
#endif

// 默认是否启用 SSL (HTTPS/WSS)
#ifndef NC_CLOUD_SSL
#define NC_CLOUD_SSL false
#endif

// 当默认 ws:80 连续失败时，自动回退到 wss:443 再尝试一次
#ifndef NC_CLOUD_AUTO_FALLBACK_SSL
#define NC_CLOUD_AUTO_FALLBACK_SSL true
#endif

// Cloud 首次连接等待超时（超过后尝试 SSL 回退）
#ifndef NC_CLOUD_FALLBACK_WAIT_MS
#define NC_CLOUD_FALLBACK_WAIT_MS 8000
#endif

// 固件版本号 (用于 OTA)
#ifndef NC_FIRMWARE_VERSION
#define NC_FIRMWARE_VERSION "1.0.0"
#endif

// Local 延迟启动最长等待时长（等待 Cloud 连接完成）
#ifndef NC_LOCAL_START_DEFER_TIMEOUT_MS
#define NC_LOCAL_START_DEFER_TIMEOUT_MS 8000
#endif

// 开机后每间隔多久检查一次 Local 连接（仅当 ENABLE_CLOUD 时）
#ifndef NC_CLOUD_WIFI_CHECK_INTERVAL_MS
#define NC_CLOUD_WIFI_CHECK_INTERVAL_MS 10000
#endif
// 连续多少次检查到 Local 未连接后才请求云端接口
#ifndef NC_CLOUD_WIFI_CONSECUTIVE_THRESHOLD
#define NC_CLOUD_WIFI_CONSECUTIVE_THRESHOLD 3
#endif

class NeuCharEdgeClass {
private:
    NcCloudTransport _cloudTransport;
    NcLocalTransport _localTransport;
    NcOTA _ota;
    std::vector<NcMcpTool*> _tools;
    ProvisioningManager _provisioning;

    bool _sdkStarted = false;
    bool _enableCloud = true;
    bool _localStartPending = false;
    bool _cloudFallbackTried = false;
    bool _cloudConnectedOnce = false;
    unsigned long _sdkStartAt = 0;
    String _localHost;
    String _localPath;
    String _deviceName;
    String _did;
    String _uid;
    String _privateKey;

public:
    NeuCharEdgeClass() {
        _cloudTransport.setTools(_tools);
        _localTransport.setTools(_tools);
    }

    // --------------------------------------------------------
    // 高级 API：让用户只关注 Tool 开发，底层接管配网与通信
    // --------------------------------------------------------

    void begin(String deviceName, String did, String uid, String privateKey, int bootPin = 0, bool enableCloud = true) {
        _deviceName = deviceName;
        _enableCloud = enableCloud;

        // 身份信息优先级：
        //   1. NVS 中已有值 → 直接使用（OTA 后保持设备独立身份）
        //   2. NVS 为空 且 代码传入非空值 → 写入 NVS 并使用（首次烧录初始化）
        //   3. NVS 为空 且 代码传入也为空 → 维持空值（OTA bin 场景，不覆盖）
        Preferences _prefs;
        _prefs.begin("nc_identity", false);
        String nvsDid  = _prefs.getString("did",  "");
        String nvsUid  = _prefs.getString("uid",  "");
        String nvsPkey = _prefs.getString("pkey", "");

        if (nvsDid.length() > 0 && nvsUid.length() > 0 && nvsPkey.length() > 0) {
            // NVS 已有身份，直接使用
            _did = nvsDid;
            _uid = nvsUid;
            _privateKey = nvsPkey;
            Serial.println("[NcEdge] Identity loaded from NVS.");
        } else if (did.length() > 0 && uid.length() > 0 && privateKey.length() > 0) {
            // NVS 为空且代码有值：首次烧录，写入 NVS
            _prefs.putString("did",  did);
            _prefs.putString("uid",  uid);
            _prefs.putString("pkey", privateKey);
            _did = did;
            _uid = uid;
            _privateKey = privateKey;
            Serial.println("[NcEdge] Identity saved to NVS from hardcoded values.");
        } else {
            // NVS 为空且代码也为空（OTA bin）：保持空值，设备无法连接直到手动配置身份
            _did = did;
            _uid = uid;
            _privateKey = privateKey;
            Serial.println("[NcEdge] WARNING: No identity found in NVS or code. Device identity is empty.");
        }
        _prefs.end();

        Serial.printf("[NcEdge] DID: %s\n", _did.c_str());

        _provisioning.setTools(_tools);
        _provisioning.configure(_did, _uid, _deviceName, _privateKey, bootPin, NC_CLOUD_HOST);
        _provisioning.begin();

        // 预初始化 RSA
        initRsaService(_privateKey.c_str());
    }

    void run() {
        // 每 5 分钟打印一次硬件健康报告（放在 run 开头确保只要在运行就能打印）
        static unsigned long lastHardwareReport = 0;
        if (millis() - lastHardwareReport >= 300000) {
            lastHardwareReport = millis();
            Serial.println("---------- [Hardware Health Report] ----------");
            Serial.printf("  Free Heap: %d B\n", ESP.getFreeHeap());
            Serial.printf("  Min Free Heap: %d B\n", ESP.getMinFreeHeap());
            Serial.printf("  Max Alloc Block: %d B\n", ESP.getMaxAllocHeap());
            Serial.printf("  Chip Revision: %d\n", ESP.getChipRevision());
            Serial.printf("  CPU Frequency: %d MHz\n", ESP.getCpuFreqMHz());
            Serial.println("----------------------------------------------");
        }

        _provisioning.loop();

        if (!_sdkStarted && _provisioning.isConnected()) {
            static bool wifiLogDone = false;
            if (!wifiLogDone) {
                Serial.print("WiFi Connected! IP: ");
                Serial.println(WiFi.localIP());
                Serial.println("Waiting 5s for network stability...");
                wifiLogDone = true;
            }

            static unsigned long connectedTime = millis();
            if (millis() - connectedTime > 5000) {
                _sdkStarted = true;
                _sdkStartAt = millis();
                _cloudFallbackTried = false;
                _cloudConnectedOnce = false;

                // 启动时打印设备信息
                Preferences _verPrefs;
                _verPrefs.begin("nc_ota", true);
                String _fwVer = _verPrefs.getString("fw_ver", NC_FIRMWARE_VERSION);
                _verPrefs.end();

                Serial.println("========== [NeuChar Edge SDK 启动信息] ==========");
                Serial.printf("  WiFi SSID:    %s\n", WiFi.SSID().c_str());
                Serial.printf("  IP Address:  %s\n", WiFi.localIP().toString().c_str());
                Serial.printf("  DID:         %s\n", _did.c_str());
                Serial.printf("  UID:         %s\n", _uid.c_str());
                Serial.printf("  Device Name: %s\n", _deviceName.c_str());
                Serial.printf("  Firmware:    %s\n", _fwVer.c_str());
                Serial.printf("  ENABLE_CLOUD: %s\n", _enableCloud ? "true" : "false");
                Serial.printf("  Identity Src: %s\n", _did.length() > 0 ? "NVS" : "Empty (no identity)");
                Serial.println("==================================================");
                Serial.println(">>> Starting NeuChar Edge SDK...");
                
                if (_enableCloud) {
                    Serial.printf("[Cloud] Connecting to %s (Port %d)...\n", NC_CLOUD_HOST, NC_CLOUD_PORT);
                    
                    // 判断是否使用SSL
                    bool useSSL = NC_CLOUD_SSL;
                    
                    // 连接云端：必须强制发送 Origin Header (最后一个参数 true)
                    beginCloud(NC_CLOUD_HOST, NC_CLOUD_PORT, _did, _uid, _privateKey, useSSL, true);
                } else {
                    Serial.println("[Cloud] Cloud connection disabled.");
                }
                
                String host = _provisioning.getHost();
                if (host.length() > 0) {
                    Serial.printf("[Local] Connecting to NCB Host: %s:5000...\n", host.c_str());
                    // 构造 Local URL
                    String path = "/edgedatahub?did=" + _did + "&uid=" + _uid + "&deciveName=" + _deviceName + "&edgePort=5000";

                    // Local 连接可能在目标地址错误时阻塞，先让 Cloud 完成握手再启动 Local。
                    _localHost = host;
                    _localPath = path;
                    _localStartPending = true;
                    if (_enableCloud) {
                        Serial.println("[Local] Deferred until Cloud is connected (or 20s timeout).");
                    }
                } else {
                    Serial.println("[Local] No NCB Host configured, skipping local connection.");
                }
            }
        }

        if (_sdkStarted) {
            if (_enableCloud && _cloudTransport.isConnected()) {
                _cloudConnectedOnce = true;
            }

            if (_enableCloud && NC_CLOUD_AUTO_FALLBACK_SSL && !_cloudConnectedOnce && !_cloudFallbackTried) {
                // 若默认 ws:80 在一段时间内未连上，则自动切换到 wss:443 再试一次。
                if (!NC_CLOUD_SSL && NC_CLOUD_PORT == 80 && (millis() - _sdkStartAt > NC_CLOUD_FALLBACK_WAIT_MS)) {
                    _cloudFallbackTried = true;
                    Serial.println("[Cloud] Fallback: switching to WSS 443...");
                    beginCloud(NC_CLOUD_HOST, 443, _did, _uid, _privateKey, true, true);
                }
            }

            if (_localStartPending) {
                bool canStartLocal = !_enableCloud || _cloudTransport.isConnected() || (millis() - _sdkStartAt > NC_LOCAL_START_DEFER_TIMEOUT_MS);
                if (canStartLocal) {
                    Serial.println("[Local] Starting local connection now...");
                    // 连接本地：不发送 Origin Header (最后一个参数 false) 以避免 CORS 问题
                    beginLocal(_localHost, 5000, _localPath, _did, _uid, "", false, false, 0);
                    _localStartPending = false;
                    
                    // 初始化 OTA
                    _ota.begin(&_localTransport, &_provisioning, _did, _uid, NC_FIRMWARE_VERSION);
                }
            }

            // ENABLE_CLOUD 时：每 10 秒检查一次 Local；连续 3 次未连接才请求云端建议并尝试切换
            if (_enableCloud) {
                static unsigned long lastCloudWifiCheckAt = 0;
                static int consecutiveLocalNotConnected = 0;
                unsigned long now = millis();
                if (now - lastCloudWifiCheckAt >= NC_CLOUD_WIFI_CHECK_INTERVAL_MS) {
                    lastCloudWifiCheckAt = now;
                    if (_localTransport.isConnected()) {
                        consecutiveLocalNotConnected = 0;
                    } else {
                        consecutiveLocalNotConnected++;
                        if (consecutiveLocalNotConnected >= NC_CLOUD_WIFI_CONSECUTIVE_THRESHOLD) {
                            consecutiveLocalNotConnected = 0;
                            Serial.println("[CloudWifi] Local not connected (3x), fetching suggested WiFi from cloud...");
                            // if (_cloudTransport.isConnected()) {
                            //     _cloudTransport.disconnect();
                            //     for (int i = 0; i < 40; i++) {
                            //         _cloudTransport.loop();
                            //         delay(50);
                            //     }
                            // }
                            if (_provisioning.trySwitchToCloudSuggestedWifi()) {
                                delay(500);
                                ESP.restart();
                            }
                        }
                    }
                }
            }
            loop();
        }
    }

    // --------------------------------------------------------
    // 基础 API
    // --------------------------------------------------------

    // 重载方法以适配不同的参数组合
    void beginCloud(String host, String did, String uid, String secret) {
        beginCloud(host, 443, did, uid, secret, true, true);
    }

    // 更新后的 beginCloud，增加 useOrigin 参数
    void beginCloud(String host, int port, String did, String uid, String secret, bool isSSL = true, bool useOrigin = true) {
        String url = "/DeviceHub?DID=" + did + "&UID=" + uid;
        // useOrigin 由调用方决定，非 SSL 场景也允许显式传 true。
        _cloudTransport.begin(host, port, url, did, uid, secret, isSSL, useOrigin);
    }

    // 更新后的 beginLocal，增加 useOrigin 参数和 connectDelay 参数
    void beginLocal(String host, int port, String url, String did, String uid, String secret, bool isSSL = false, bool useOrigin = false, unsigned long connectDelay = 0) {
        _localTransport.begin(host, port, url, did, uid, secret, isSSL, useOrigin, "", connectDelay);
    }

    void loop() {
        if (_enableCloud) {
            _cloudTransport.loop();
            _cloudTransport.keepAlive();
        }
        _localTransport.loop();
        _localTransport.keepAlive();
        _ota.loop();
    }

    void registerTool(NcMcpTool& tool) {
        _tools.push_back(&tool);
    }

    NcCloudTransport& getCloud() { return _cloudTransport; }
    NcLocalTransport& getLocal() { return _localTransport; }
};

extern NeuCharEdgeClass NcEdge;

#endif
