#ifndef NC_PROVISIONING_H
#define NC_PROVISIONING_H

#include <WiFi.h>
#include <WebServer.h>
#include <DNSServer.h>
#include <Preferences.h>
#include <ArduinoJson.h>
#include <BluetoothSerial.h>
#include "NcCrypto.h"
#include "NcMcp.h"

class ProvisioningManager {
private:
    WebServer server;      // 5000 端口：API
    WebServer apServer;    // 80 端口：AP 配网
    DNSServer dnsServer;
    Preferences preferences;
    BluetoothSerial SerialBT;

    bool _apActive = false;
    bool _btActive = false;
    bool _btDelayedStart = false;
    bool _btConnectionJustEstablished = false;
    unsigned long _startTime = 0;
    
    // Config
    String _did;
    String _uid;
    String _deviceName;
    String _privateKey;
    int _bootPin;
    String _cloudHost;

    static const unsigned long BT_START_DELAY_MS = 3000;
    static const unsigned long BOOT_PRESS_MS = 2000;
    unsigned long _bootPressStart = 0;
    bool _bootTriggered = false;
    String _apWifiOptions;

    std::vector<NcMcpTool*>* _toolsRef = nullptr;

    static String escapeHtml(const String& s);

    void startApPortalServer();
    void startApiServer();
    void startBluetooth();
    void handleBluetooth();
    void startApMode();
    void saveConfig(String ssid, String pass, String host);

    // WiFi 历史记录（最多 10 条，按时间 index0=最新）
    static const int WIFI_HISTORY_MAX = 10;
    void saveWifiHistory(const String& ssid, const String& pass);
    String getPasswordBySsid(const String& ssid);

    // MCP Handlers
    void handleMcpSse();
    void handleMcpMessages();

public:
    ProvisioningManager();
    void configure(String did, String uid, String deviceName, String privateKey, int bootPin = 0, String cloudHost = "www.neuchar.com");
    void setTools(std::vector<NcMcpTool*>& tools);
    void begin();
    void loop();
    bool isConnected();
    String getHost();
    // 云端建议网络：当 Local 未连接时拉取 SSID+NCBIP，若历史中有该 SSID 则切换并返回 true
    bool trySwitchToCloudSuggestedWifi();
};

#endif
