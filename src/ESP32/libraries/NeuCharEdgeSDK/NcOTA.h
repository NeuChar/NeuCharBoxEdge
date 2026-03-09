#ifndef NC_OTA_H
#define NC_OTA_H

#include <Arduino.h>
#include <HTTPClient.h>
#include <Update.h>
#include <ArduinoJson.h>
#include <Preferences.h>
#include "NcTransport.h"
#include "NcProvisioning.h"

class NcOTA {
private:
    NcLocalTransport* _localTransport;
    ProvisioningManager* _provisioning;
    String _did;
    String _uid;
    String _currentVersion;
    unsigned long _lastCheck = 0;
    const unsigned long CHECK_INTERVAL = 60000; // 1 minute

public:
    NcOTA() : _localTransport(nullptr), _provisioning(nullptr) {}

    void begin(NcLocalTransport* localTransport, ProvisioningManager* provisioning, String did, String uid, String defaultVersion) {
        _localTransport = localTransport;
        _provisioning = provisioning;
        _did = did;
        _uid = uid;

        Preferences prefs;
        prefs.begin("nc_ota", true);
        _currentVersion = prefs.getString("fw_ver", defaultVersion);
        prefs.end();

        Serial.printf("[OTA] Current Version: %s\n", _currentVersion.c_str());
        _lastCheck = millis() - (CHECK_INTERVAL - 5000); 
    }

    void loop() {
        if (millis() - _lastCheck >= CHECK_INTERVAL) {
            _lastCheck = millis();
            checkUpdate();
        }
    }

    void checkUpdate() {
        if (!_localTransport || !_localTransport->isConnected() || !_provisioning) return;

        String ncbIp = _provisioning->getHost();
        if (ncbIp.length() == 0) return;

        if (_localTransport->isTokenExpiring()) {
            _localTransport->requestToken();
            return; 
        }

        String downloadUrl = "";
        String latestVersion = "";
        bool updateFound = false;

        // 使用局部作用域确保 HTTPClient 和 JsonDocument 在 performUpdate 前被释放
        {
            Serial.printf("[Hardware] Free Heap: %d B\n", ESP.getFreeHeap());
            String token = _localTransport->getToken();
            HTTPClient http;
            String url = "http://" + ncbIp + ":5000/api/Senparc.Xncf.NeuCharBoxCenter/CenterAppService/Xncf.NeuCharBoxCenter_CenterAppService.GetOTAInfo";
            url += "?FirmwareType=backend&DID=" + _did + "&UID=" + _uid + "&AppKey=" + _did + "&AppSecret=" + _uid;

            Serial.printf("[OTA] Checking for updates...\n");
            http.begin(url);
            http.addHeader("Authorization", "Bearer " + token);
            http.setTimeout(10000);

            int httpCode = http.GET();
            if (httpCode == HTTP_CODE_OK) {
                String payload = http.getString();
                DynamicJsonDocument doc(2048);
                DeserializationError error = deserializeJson(doc, payload);

                if (!error && doc["success"] == true) {
                    latestVersion = doc["data"]["firmwareVersion"] | "";
                    if (latestVersion.length() > 0 && latestVersion != _currentVersion) {
                        String packagePath = doc["data"]["firmwarePackage"] | "";
                        downloadUrl = packagePath.startsWith("http") ? packagePath : ("http://" + ncbIp + ":5000" + (packagePath.startsWith("/") ? "" : "/") + packagePath);
                        updateFound = true;
                    } else {
                        Serial.println("[OTA] Already up to date.");
                    }
                }
            } else {
                Serial.printf("[OTA] Check failed, code: %d\n", httpCode);
            }
            http.end();
        }

        if (updateFound) {
            performUpdate(downloadUrl, latestVersion);
        }
    }

    void performUpdate(String url, String newVersion) {
        if (_localTransport) _localTransport->setPaused(true);
        
        // 核心优化 1：强制执行一次垃圾回收建议（ESP32 无显式 GC，但可以确保之前的对象已析构）
        yield();
        delay(100); 

        Serial.printf("[OTA] Found new version: %s. Starting update...\n", newVersion.c_str());
        Serial.printf("[OTA] Memory before update: Free Heap: %d B, Max Block: %d B\n", ESP.getFreeHeap(), ESP.getMaxAllocHeap());

        const int maxAttempts = 3;
        bool success = false;

        for (int attempt = 1; attempt <= maxAttempts && !success; attempt++) {
            Serial.printf("[OTA] Attempt %d/%d...\n", attempt, maxAttempts);

            HTTPClient http;
            http.setReuse(false); // 每次尝试重新建立连接，避免旧连接状态干扰
            http.begin(url);
            http.setTimeout(60000); // 1分钟超时

            int httpCode = http.GET();
            if (httpCode == HTTP_CODE_OK) {
                int contentLength = http.getSize();
                if (contentLength > 0) {
                    Serial.printf("[OTA] Binary size: %d bytes\n", contentLength);
                    
                    if (Update.begin(contentLength, U_FLASH)) {
                        WiFiClient* stream = http.getStreamPtr();
                        
                        // 核心优化 2：使用 Update.writeStream 直接从网络流写入，这是最快的方式
                        // 它内部优化了缓冲和写入时机，且减少了我们的内存申请压力
                        size_t written = Update.writeStream(*stream);
                        
                        if (written == contentLength) {
                            if (Update.end(true)) {
                                success = true;
                                Serial.println("[OTA] Update successful!");
                                
                                Preferences prefs;
                                prefs.begin("nc_ota", false);
                                prefs.putString("fw_ver", newVersion);
                                prefs.end();

                                Serial.println("[OTA] Rebooting...");
                                delay(1000);
                                ESP.restart();
                            } else {
                                Serial.printf("[OTA] Update.end() failed: %d\n", Update.getError());
                            }
                        } else {
                            Serial.printf("[OTA] Written: %d/%d. Timeout or connection lost.\n", written, contentLength);
                        }
                    } else {
                        Serial.printf("[OTA] Update.begin failed: %d\n", Update.getError());
                    }
                } else {
                    Serial.println("[OTA] Invalid content length.");
                }
            } else {
                Serial.printf("[OTA] Download failed, code: %d\n", httpCode);
            }
            http.end();

            if (!success) {
                Update.abort();
                Serial.println("[OTA] Attempt failed. Clearing state...");
                delay(2000);
            }
        }

        if (!success && _localTransport) _localTransport->setPaused(false);
    }
};

#endif
