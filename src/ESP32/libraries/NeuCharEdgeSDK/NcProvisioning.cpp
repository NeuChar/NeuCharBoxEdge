#include "NcProvisioning.h"
#include <HTTPClient.h>
#include <time.h>
#include "esp_bt.h"

ProvisioningManager::ProvisioningManager() : server(5000), apServer(80) {
    _bootPin = 0;
}

void ProvisioningManager::configure(String did, String uid, String deviceName, String privateKey, int bootPin, String cloudHost) {
    _did = did;
    _uid = uid;
    _deviceName = deviceName;
    _privateKey = privateKey;
    _bootPin = bootPin;
    _cloudHost = cloudHost.length() > 0 ? cloudHost : "www.neuchar.com";
    pinMode(_bootPin, INPUT_PULLUP);
}

void ProvisioningManager::setTools(std::vector<NcMcpTool*>& tools) {
    _toolsRef = &tools;
}

String ProvisioningManager::escapeHtml(const String& s) {
    String r = s;
    r.replace("&", "&amp;");
    r.replace("\"", "&quot;");
    r.replace("<", "&lt;");
    return r;
}

void ProvisioningManager::begin() {
    _startTime = millis();
    WiFi.mode(WIFI_STA); 
    delay(800); 

    preferences.begin("nc_config_v2", true);
    String ssid = preferences.getString("ssid", "");
    String password = preferences.getString("password", "");
    preferences.end();

    _btDelayedStart = true;

    if (ssid.length() > 0) {
        Serial.println("WiFi connecting in background...");
        WiFi.begin(ssid.c_str(), password.c_str());
    }
    startApiServer();
}

void ProvisioningManager::startBluetooth() {
    delay(300);
    String did = _did;
    String btName = "NCBEdge_" + (did.length() >= 6 ? did.substring(did.length() - 6) : did) + "_" + _deviceName; 
    if (SerialBT.begin(btName)) {
        _btActive = true;
        Serial.println("==========================================");
        Serial.printf("BT ACTIVE: %s\n", btName.c_str());
        Serial.println("==========================================");
    } else {
        Serial.println("==========================================");
        Serial.println("BT START FAILED! (Low Heap?)");
        Serial.printf("Free Heap: %u\n", ESP.getFreeHeap());
        Serial.println("==========================================");
    }
}

void ProvisioningManager::handleBluetooth() {
    if (!SerialBT.hasClient()) return;

    static char rxBuf[2048];
    static size_t rxLen = 0;

    while (SerialBT.available()) {
        char c = SerialBT.read();

        if (c == '\n' || c == '\r') {
            if (rxLen == 0) break;

            rxBuf[rxLen] = '\0';
            rxLen = 0;

            Serial.printf("BT Raw: %s\n", rxBuf);

            char* jsonBuf = (char*)malloc(2048);
            if (!jsonBuf) {
                Serial.println("BT ERROR: Malloc failed for jsonBuf");
                break;
            }

            size_t decodedLen = 0;
            int ret = mbedtls_base64_decode(
                (unsigned char*)jsonBuf,
                2048 - 1,
                &decodedLen,
                (const unsigned char*)rxBuf,
                strlen(rxBuf)
            );

            if (ret == 0 && decodedLen > 0) {
                jsonBuf[decodedLen] = '\0';
                Serial.printf("BT Decoded: %s\n", jsonBuf);
            } else {
                strncpy(jsonBuf, rxBuf, 2048 - 1);
                jsonBuf[2048 - 1] = '\0';
            }

            DynamicJsonDocument doc(4096);
            DeserializationError error = deserializeJson(doc, jsonBuf);
            free(jsonBuf);
            jsonBuf = NULL;

            if (error) {
                Serial.print("BT JSON parse failed: ");
                Serial.println(error.c_str());
                break;
            }

            String msgId = doc["MsgId"] | "";
            int type = doc["Type"] | 0;

            // HELLO
            if (type == 10000) {
                DynamicJsonDocument res(1024);
                res["MsgId"] = msgId;
                res["Time"] = doc["Time"];
                res["Type"] = 10000;
                res["Success"] = true;
                res["Message"] = "Success";
                res["Data"] = _did;

                String sign = rsaSign(_did, _privateKey.c_str());
                res["Sign"] = sign;

                char outBuf[1024];
                size_t outLen = serializeJson(res, outBuf, sizeof(outBuf));

                for (size_t i = 0; i < outLen && SerialBT.hasClient(); i += 128) {
                    SerialBT.write((uint8_t*)outBuf + i, min((size_t)128, outLen - i));
                }
                if (SerialBT.hasClient()) SerialBT.println();
                Serial.printf("BT Sent: %s\n", outBuf);
            }
            // WIFI
            else if (type == 10050) {
                const char* encrypted = doc["Data"] | "";
                String decrypted = rsaDecrypt(encrypted, _privateKey.c_str());

                if (decrypted.length() > 0) {
                    DynamicJsonDocument wifiDoc(4096);
                    if (!deserializeJson(wifiDoc, decrypted)) {
                        saveConfig(
                            wifiDoc["SSID"] | "",
                            wifiDoc["Password"] | "",
                            wifiDoc["NCBIP"] | ""
                        );

                        DynamicJsonDocument res(1024);
                        res["MsgId"] = msgId;
                        res["Time"] = doc["Time"];
                        res["Type"] = 10050;
                        res["Success"] = true;
                        res["Message"] = "Success";
                        res["Data"] = "SUCCESS";

                        String sign = rsaSign("SUCCESS", _privateKey.c_str());
                        res["Sign"] = sign;

                        char outBuf[1024];
                        size_t outLen = serializeJson(res, outBuf, sizeof(outBuf));

                        for (size_t i = 0; i < outLen && SerialBT.hasClient(); i += 128) {
                            SerialBT.write((uint8_t*)outBuf + i, min((size_t)128, outLen - i));
                        }
                        if (SerialBT.hasClient()) SerialBT.println();
                        Serial.printf("BT Sent: %s\n", outBuf);

                        delay(300);
                        ESP.restart();
                    }
                }
            }
            break;
        }

        if (rxLen < sizeof(rxBuf) - 1) {
            rxBuf[rxLen++] = c;
        }
    }
}

void ProvisioningManager::startApMode() {
    if (_apActive) return;
    if (_btActive) SerialBT.end();
    _apActive = true; _btActive = false;
    
    Serial.println("Scanning WiFi...");
    int n = WiFi.scanNetworks();
    _apWifiOptions = "";
    const int maxList = 15;
    for (int i = 0; i < n && i < maxList; i++) {
        String ssid = WiFi.SSID(i);
        if (ssid.length() == 0) continue;
        String esc = escapeHtml(ssid);
        _apWifiOptions += "<option value=\"" + esc + "\">" + esc + " (" + String(WiFi.RSSI(i)) + ")</option>";
    }
    if (_apWifiOptions.length() == 0) _apWifiOptions = "<option value=\"\">未发现 WiFi</option>";
    WiFi.scanDelete();
    
    WiFi.mode(WIFI_AP);
    String did = _did;
    String apName = "NCBEdge_" + (did.length() >= 6 ? did.substring(did.length() - 6) : did);
    WiFi.softAP(apName.c_str());
    dnsServer.start(53, "*", WiFi.softAPIP());
    startApPortalServer();
}

void ProvisioningManager::startApPortalServer() {
    apServer.on("/", HTTP_GET, [this]() {
        String html = String(
            "<!DOCTYPE html><html><head><meta charset=\"UTF-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">"
            "<title>ESP32 配网</title>"
            "<style>"
            ":root{color-scheme:light;font-family:-apple-system,BlinkMacSystemFont,\"Segoe UI\",Roboto,\"Helvetica Neue\",Arial,\"PingFang SC\",\"Microsoft YaHei\",sans-serif}"
            "*{box-sizing:border-box}"
            "body{margin:0;background:#f5f7fb;color:#1f2937;line-height:1.5}"
            ".wrap{max-width:560px;margin:0 auto;padding:16px}"
            ".card{background:#fff;border:1px solid #e5e7eb;border-radius:12px;padding:16px;box-shadow:0 2px 8px rgba(15,23,42,.06)}"
            "h1{margin:0 0 14px;font-size:20px}"
            "label{display:block;margin:10px 0 6px;font-size:14px;color:#374151}"
            "input,select,button{width:100%;font-size:16px;border-radius:8px;min-height:42px}"
            "input,select{padding:10px 12px;border:1px solid #d1d5db;background:#fff;color:#111827}"
            "input:focus,select:focus{outline:none;border-color:#3b82f6;box-shadow:0 0 0 3px rgba(59,130,246,.18)}"
            "button{margin-top:14px;border:0;background:#2563eb;color:#fff;font-weight:600;cursor:pointer;padding:10px 12px}"
            "button:active{transform:translateY(1px)}"
            "@media (min-width:768px){.wrap{padding:32px}.card{padding:22px}h1{font-size:22px}}"
            "</style></head><body><main class=\"wrap\"><section class=\"card\">"
            "<h1>NCB Edge 配网</h1>"
            "<form method=\"POST\" action=\"/api/wifi/save\">"
            "<label for=\"ssid\">WiFi</label><select id=\"ssid\" name=\"ssid\" required><option value=\"\">-- 请选择 --</option>"
        ) + _apWifiOptions +
            "</select>"
            "<label for=\"password\">密码</label><input id=\"password\" type=\"password\" name=\"password\" autocomplete=\"off\">"
            "<label for=\"ncb_host\">NCB 主机IP</label><input id=\"ncb_host\" type=\"text\" name=\"ncb_host\" value=\"\" placeholder=\"IP\">"
            "<button type=\"submit\">保存并重启</button></form></section></main></body></html>";
        apServer.send(200, "text/html; charset=utf-8", html);
    });
    apServer.on("/api/wifi/save", HTTP_POST, [this]() {
        String ssid = apServer.hasArg("ssid") ? apServer.arg("ssid") : "";
        String password = apServer.hasArg("password") ? apServer.arg("password") : "";
        String ncbHost = apServer.hasArg("ncb_host") ? apServer.arg("ncb_host") : "";
        if (ssid.length() > 0) {
            saveConfig(ssid, password, ncbHost);
            apServer.send(200, "text/html; charset=utf-8",
                "<!DOCTYPE html><html><head><meta charset=\"UTF-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"></head><body>"
                "<p>配置已保存，即将重启。</p></body></html>"
            );
            delay(500);
            ESP.restart();
        } else {
            apServer.send(400, "text/html; charset=utf-8",
                "<!DOCTYPE html><html><head><meta charset=\"UTF-8\"></head><body><p>SSID 不能为空。</p><a href=\"/\">返回</a></body></html>"
            );
        }
    });
    apServer.onNotFound([this]() {
        apServer.sendHeader("Location", "http://192.168.4.1/", true);
        apServer.send(302, "text/plain", "");
    });
    apServer.begin();
    Serial.println("AP Portal (80) started");
}

void ProvisioningManager::startApiServer() {
    server.on("/api/status", HTTP_GET, [this]() {
        server.send(200, "application/json", "{\"status\":\"ok\"}");
    });

    // MCP SSE (Minimal Implementation)
    server.on("/edgemcp/sse", HTTP_GET, [this]() {
        this->handleMcpSse();
    });

    // MCP Messages
    server.on("/edgemcp/messages", HTTP_POST, [this]() {
        this->handleMcpMessages();
    });

    server.onNotFound([this]() {
        String uri = server.uri();
        if (server.method() == HTTP_POST && uri.startsWith("/api/")) {
            String methodName = uri.substring(uri.lastIndexOf('/') + 1);
            DynamicJsonDocument doc(1024);
            if (server.hasArg("plain")) deserializeJson(doc, server.arg("plain"));
            
            String result = "";
            bool executed = false;
            if (_toolsRef) {
                for (auto tool : *_toolsRef) {
                    String toolName = tool->getName();
                    String fullMethodName = toolName + "-" + methodName;
                    if (tool->execute(fullMethodName, doc.as<JsonObject>(), result)) {
                        executed = true;
                        break;
                    }
                }
            }
            if (executed) {
                server.send(200, "application/json", "{\"Success\":true, \"Data\":\"" + result + "\"}");
                return;
            }
        }
        server.send(404, "text/plain", "Not Found");
    });
    server.begin();
}

void ProvisioningManager::handleMcpSse() {
    // 简单的 SSE 握手，告知客户端 Endpoint 地址
    server.sendHeader("Content-Type", "text/event-stream");
    server.sendHeader("Cache-Control", "no-cache");
    server.sendHeader("Connection", "keep-alive");
    server.sendHeader("Access-Control-Allow-Origin", "*");
    
    // 发送 endpoint 事件
    // 注意：标准 WebServer 处理完后会断开连接，但这通常足以触发客户端去 POST
    String endpointMsg = "event: endpoint\ndata: /edgemcp/messages\n\n";
    server.client().print(endpointMsg);
    
    // 稍微延迟保持连接，确保数据发出
    delay(100);
    // server.send(200); // 不要调用 send，我们已经手动 print 了
}

void ProvisioningManager::handleMcpMessages() {
    if (!server.hasArg("plain")) {
        server.send(400, "application/json", "{\"error\":\"Missing body\"}");
        return;
    }

    String body = server.arg("plain");
    DynamicJsonDocument reqDoc(4096);
    DeserializationError error = deserializeJson(reqDoc, body);

    if (error) {
        server.send(400, "application/json", "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32700,\"message\":\"Parse error\"},\"id\":null}");
        return;
    }

    String method = reqDoc["method"] | "";
    JsonVariant id = reqDoc["id"];
    
    DynamicJsonDocument resDoc(4096);
    resDoc["jsonrpc"] = "2.0";
    if (!id.isNull()) resDoc["id"] = id;

    if (method == "initialize") {
        JsonObject result = resDoc.createNestedObject("result");
        result["protocolVersion"] = "2024-11-05";
        
        JsonObject capabilities = result.createNestedObject("capabilities");
        capabilities.createNestedObject("tools"); // 支持 tools
        
        JsonObject serverInfo = result.createNestedObject("serverInfo");
        serverInfo["name"] = _deviceName;
        serverInfo["version"] = "1.0.0";
    }
    else if (method == "notifications/initialized") {
        // 无需响应
        server.send(200, "application/json", "");
        return;
    }
    else if (method == "tools/list") {
        JsonObject result = resDoc.createNestedObject("result");
        JsonArray toolsArr = result.createNestedArray("tools");
        
        if (_toolsRef) {
            for(auto tool : *_toolsRef) {
                tool->getMcpTools(toolsArr);
            }
        }
    }
    else if (method == "tools/call") {
        String toolName = reqDoc["params"]["name"] | "";
        JsonObject args = reqDoc["params"]["arguments"];
        
        String executionResult = "";
        bool executed = false;
        
        if (_toolsRef) {
            for (auto tool : *_toolsRef) {
                // MCP Tool Name 就是 registerAction 时的 name
                if (tool->execute(toolName, args, executionResult)) {
                    executed = true;
                    break;
                }
            }
        }

        if (executed) {
            JsonObject result = resDoc.createNestedObject("result");
            JsonArray content = result.createNestedArray("content");
            JsonObject textObj = content.createNestedObject();
            textObj["type"] = "text";
            textObj["text"] = executionResult;
        } else {
            JsonObject errObj = resDoc.createNestedObject("error");
            errObj["code"] = -32601;
            errObj["message"] = "Method not found";
        }
    }
    else if (method == "ping") {
        resDoc.createNestedObject("result");
    }
    else {
        JsonObject errObj = resDoc.createNestedObject("error");
        errObj["code"] = -32601;
        errObj["message"] = "Method not found";
    }

    String response;
    serializeJson(resDoc, response);
    server.sendHeader("Access-Control-Allow-Origin", "*");
    server.send(200, "application/json", response);
}

void ProvisioningManager::loop() {
    server.handleClient();

    if (!_apActive) {
        bool pressed = (digitalRead(_bootPin) == LOW);

        if (pressed) {
            if (_bootPressStart == 0) _bootPressStart = millis();
            else if (!_bootTriggered && (millis() - _bootPressStart >= BOOT_PRESS_MS)) {
                _bootTriggered = true;
                _clickCount = 0;
                Serial.println("BOOT long-press: entering AP mode");
                startApMode();
            }
        } else {
            if (_bootPressStart > 0 && !_bootTriggered) {
                _clickCount++;
                if (_clickCount == 1) _firstClickAt = millis();
            }
            _bootPressStart = 0;
            _bootTriggered = false;
        }

        if (_clickCount >= 2) {
            _clickCount = 0;
            if (!_btActive) {
                Serial.println("[BOOT] Double-click: requesting Bluetooth provisioning...");
                _btManualStartRequested = true;
            } else {
                Serial.println("[BOOT] Double-click: Bluetooth already active.");
            }
        } else if (_clickCount == 1 && millis() - _firstClickAt > DOUBLE_CLICK_WINDOW_MS) {
            _clickCount = 0;
        }
    }

    if (_btManualModeActive) {
        static uint32_t lastBtManualLog = 0;
        uint32_t now = millis();
        if ((int32_t)(now - lastBtManualLog) >= 30000) {
            lastBtManualLog = now;
            Serial.printf("[BT] Manual mode active. Free Heap: %d. Waiting for provisioning or restart...\n", ESP.getFreeHeap());
        }
    }

    if (_btDelayedStart && (millis() - _startTime >= BT_START_DELAY_MS)) {
        _btDelayedStart = false;
        startBluetooth();
    }
    
    if (_btActive) {
        bool hasClient = SerialBT.hasClient();
        static unsigned long btReadyTime = 0;

        if (hasClient && !_btConnectionJustEstablished) {
            btReadyTime = millis();
            _btConnectionJustEstablished = true;
        }

        if (_btConnectionJustEstablished) {
            if (millis() - btReadyTime < 250) {
                return;
            }
        }
        if (hasClient) handleBluetooth();
        
        static uint32_t lastP = 0;
        uint32_t now = millis();
        if ((int32_t)(now - lastP) >= 5000) {
            lastP = now;
            Serial.printf("BT: %s\n", SerialBT.hasClient() ? "Connected" : "Waiting...");
        }
    }
    
    if (_apActive) {
        apServer.handleClient();
        dnsServer.processNextRequest();
    }
}

void ProvisioningManager::saveConfig(String ssid, String pass, String host) {
    preferences.begin("nc_config_v2", false);
    preferences.putString("ssid", ssid);
    preferences.putString("password", pass);
    preferences.putString("ncb_host", host);
    preferences.end();
    saveWifiHistory(ssid, pass);
}

void ProvisioningManager::saveWifiHistory(const String& ssid, const String& pass) {
    if (ssid.length() == 0) return;
    preferences.begin("nc_wifi_hist", false);
    int n = preferences.getInt("hist_n", 0);
    if (n > WIFI_HISTORY_MAX) n = WIFI_HISTORY_MAX;

    String keySsid, keyPass;
    int foundIdx = -1;
    for (int i = 0; i < n; i++) {
        keySsid = "h" + String(i) + "_s";
        keyPass = "h" + String(i) + "_p";
        if (preferences.getString(keySsid.c_str(), "") == ssid) {
            foundIdx = i;
            break;
        }
    }

    if (foundIdx >= 0) {
        // 已存在：更新密码并移到最新（index 0）
        for (int i = foundIdx; i > 0; i--) {
            String s = "h" + String(i - 1) + "_s";
            String p = "h" + String(i - 1) + "_p";
            String ns = "h" + String(i) + "_s";
            String np = "h" + String(i) + "_p";
            preferences.putString(ns.c_str(), preferences.getString(s.c_str(), ""));
            preferences.putString(np.c_str(), preferences.getString(p.c_str(), ""));
        }
        preferences.putString("h0_s", ssid);
        preferences.putString("h0_p", pass);
    } else {
        // 新增：插入到 0，其余后移，超过 10 删除最旧（从高下标往低复制避免覆盖）
        if (n >= WIFI_HISTORY_MAX) n = WIFI_HISTORY_MAX - 1;
        for (int i = n; i >= 1; i--) {
            String s = "h" + String(i - 1) + "_s";
            String p = "h" + String(i - 1) + "_p";
            String ns = "h" + String(i) + "_s";
            String np = "h" + String(i) + "_p";
            preferences.putString(ns.c_str(), preferences.getString(s.c_str(), ""));
            preferences.putString(np.c_str(), preferences.getString(p.c_str(), ""));
        }
        preferences.putString("h0_s", ssid);
        preferences.putString("h0_p", pass);
        n++;
        preferences.putInt("hist_n", n);
    }
    preferences.end();
}

String ProvisioningManager::getPasswordBySsid(const String& ssid) {
    if (ssid.length() == 0) return "";
    preferences.begin("nc_wifi_hist", true);
    int n = preferences.getInt("hist_n", 0);
    if (n > WIFI_HISTORY_MAX) n = WIFI_HISTORY_MAX;
    String pwd;
    for (int i = 0; i < n; i++) {
        String keySsid = "h" + String(i) + "_s";
        String keyPass = "h" + String(i) + "_p";
        if (preferences.getString(keySsid.c_str(), "") == ssid) {
            pwd = preferences.getString(keyPass.c_str(), "");
            break;
        }
    }
    preferences.end();
    return pwd;
}

bool ProvisioningManager::trySwitchToCloudSuggestedWifi() {
    if (_cloudHost.length() == 0 || _did.length() == 0 || _uid.length() == 0) return false;

    // 必须使用有效的 Unix 时间，否则服务端会拒绝
    time_t now = time(nullptr);
    if (now < 1000000000) {  // 早于 2001-09 视为未同步
        configTime(0, 0, "pool.ntp.org", "time.nist.gov");
        for (int i = 0; i < 50; i++) {
            delay(100);
            now = time(nullptr);
            if (now >= 1000000000) break;
        }
    }
    if (now < 1000000000) {
        Serial.println("[CloudWifi] NTP not synced, skip GetNCBNetInfo");
        return false;
    }

    int64_t timeMs = ((int64_t)now) * 1000;
    
    // 为防 String 不支持 int64，手动转字符串
    char timeBuf[32];
    sprintf(timeBuf, "%lld", timeMs);
    String signStr = _did + _uid + String(timeBuf);
    
    Serial.printf("[CloudWifi] SignStr: %s\n", signStr.c_str());

    String signB64 = rsaSign(signStr, _privateKey.c_str());
    if (signB64.length() == 0) return false;

    DynamicJsonDocument reqDoc(512);
    reqDoc["DID"] = _did;
    reqDoc["UID"] = _uid;
    reqDoc["Time"] = timeMs;
    reqDoc["sign"] = signB64;
    String reqBody;
    serializeJson(reqDoc, reqBody);
    Serial.printf("[CloudWifi] ReqBody: %s\n", reqBody.c_str());

    delay(200);
    yield();

    const char* path = "/User/NcxBox/GetNCBNetInfo";
    int code = -1;
    String resBody;

    // ESP32 上 HTTPS 易失败，直接使用 HTTP 80
    WiFiClient client;
    HTTPClient http;
    if (http.begin(client, _cloudHost, 80, path, false)) {
        http.addHeader("Content-Type", "application/json");
        http.setConnectTimeout(10000);
        http.setTimeout(15000);
        code = http.POST(reqBody);
        resBody = code == 200 ? http.getString() : http.getString();
        if (code != 200)
            Serial.printf("[CloudWifi] GetNCBNetInfo HTTP %d, %s\n", code, resBody.c_str());
        http.end();
    } else {
        Serial.println("[CloudWifi] GetNCBNetInfo http.begin failed");
    }

    bool ok = false;
    if (code == 200 && resBody.length() > 0) {
        // 打印完整响应体，便于确认是否真的到了线上接口（真实接口通常为 {"Success":...,"Message":...,"Data":...}）
        if (resBody.length() <= 400)
            Serial.printf("[CloudWifi] Response(200): %s\n", resBody.c_str());
        else
            Serial.printf("[CloudWifi] Response(200) len=%d: %.400s...\n", resBody.length(), resBody.c_str());
        DynamicJsonDocument resDoc(1024);
        if (!deserializeJson(resDoc, resBody)) {
            // 兼容服务端返回小写 success/message/data
            bool success = resDoc["Success"] | resDoc["success"] | false;
            const char* dataEnc = resDoc["Data"] | resDoc["data"] | "";
            if (success && strlen(dataEnc) > 0) {
                String decrypted = rsaDecrypt(dataEnc, _privateKey.c_str());
                if (decrypted.length() > 0) {
                    DynamicJsonDocument wifiDoc(512);
                    if (!deserializeJson(wifiDoc, decrypted)) {
                        String wifiName = wifiDoc["wifiName"] | "";
                        String ipAddress = wifiDoc["ipAddress"] | "";
                        if (wifiName.length() > 0 && ipAddress.length() > 0) {
                            String pwd = getPasswordBySsid(wifiName);
                            if (pwd.length() > 0) {
                                Serial.printf("[CloudWifi] Switch to suggested: %s -> %s\n", wifiName.c_str(), ipAddress.c_str());
                                saveConfig(wifiName, pwd, ipAddress);
                                ok = true;
                            } else {
                                Serial.printf("[CloudWifi] SSID %s not in history, skip.\n", wifiName.c_str());
                            }
                        }
                    }
                }
            } else {
                // 200 且解析成功，但 Success 为 false 或 Data 为空
                const char* msg = resDoc["Message"] | resDoc["message"] | "";
                Serial.printf("[CloudWifi] GetNCBNetInfo Success=false (Message=%s)\n", strlen(msg) ? msg : "(empty)");
            }
        } else {
            Serial.printf("[CloudWifi] GetNCBNetInfo 200 but parse failed or empty body\n");
        }
    } else if (code == 200 && resBody.length() == 0) {
        Serial.println("[CloudWifi] GetNCBNetInfo HTTP 200 with empty body");
    } else if (code != 200) {
        Serial.printf("[CloudWifi] GetNCBNetInfo HTTP %d (heap=%d)\n", code, ESP.getFreeHeap());
    }
    return ok;
}

bool ProvisioningManager::isBtManualStartRequested() {
    return _btManualStartRequested;
}

void ProvisioningManager::startBtManualMode() {
    _btManualStartRequested = false;
    Preferences btPrefs;
    btPrefs.begin("nc_prov", false);
    btPrefs.putBool("bt_mode", true);
    btPrefs.end();
    Serial.println("[BT] Restarting for Bluetooth provisioning...");
    delay(200);
    ESP.restart();
}

void ProvisioningManager::stopBluetooth() {
    Preferences btPrefs;
    btPrefs.begin("nc_prov", false);
    bool manualBt = btPrefs.getBool("bt_mode", false);
    if (manualBt) {
        btPrefs.putBool("bt_mode", false);
        btPrefs.end();
        _btManualModeActive = true;
        Serial.println("[BT] Manual BT mode: Bluetooth stays active until provisioning or restart.");
        return;
    }
    btPrefs.end();

    if (_btActive) {
        SerialBT.end();
        _btActive = false;
        _btDelayedStart = false;
        delay(100);
        esp_bt_controller_deinit();
        esp_bt_mem_release(ESP_BT_MODE_CLASSIC_BT);
        Serial.printf("[BT] Bluetooth stopped, controller memory released. Free Heap: %d\n", ESP.getFreeHeap());
    }
    _btDelayedStart = false;
}

bool ProvisioningManager::isBtManualModeActive() {
    return _btManualModeActive;
}

bool ProvisioningManager::isConnected() { return WiFi.status() == WL_CONNECTED; }

String ProvisioningManager::getHost() {
    preferences.begin("nc_config_v2", true);
    String h = preferences.getString("ncb_host", "");
    preferences.end();
    return h;
}
