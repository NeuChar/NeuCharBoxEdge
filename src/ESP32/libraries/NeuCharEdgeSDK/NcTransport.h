#ifndef NC_TRANSPORT_H
#define NC_TRANSPORT_H

#include <WebSocketsClient.h>
#include <ArduinoJson.h>
#include "NcMcp.h"

// WebSocket 调度参数（Cloud/Local 共用，可按项目覆盖）
#ifndef NC_WS_RECONNECT_INTERVAL_MS
#define NC_WS_RECONNECT_INTERVAL_MS 5000
#endif

#ifndef NC_WS_HEARTBEAT_INTERVAL_MS
#define NC_WS_HEARTBEAT_INTERVAL_MS 10000
#endif

#ifndef NC_WS_HEARTBEAT_TIMEOUT_MS
#define NC_WS_HEARTBEAT_TIMEOUT_MS 3000
#endif

#ifndef NC_WS_HEARTBEAT_MISSES
#define NC_WS_HEARTBEAT_MISSES 2
#endif

#ifndef NC_WS_KEEPALIVE_INTERVAL_MS
#define NC_WS_KEEPALIVE_INTERVAL_MS 10000
#endif

// ==========================================
// 传输基类：处理 WebSocket 基础连接与 SignalR 握手
// ==========================================
class NcTransport {
protected:
    WebSocketsClient _webSocket;
    String _did;
    String _uid;
    String _secret;
    String _name;
    String _extraHeaders;
    bool _isConnected = false;
    bool _isSSL = false;
    bool _isPaused = false;
    bool _signalRReady = false;
    unsigned long _signalRReadyAt = 0;
    std::vector<NcMcpTool*>* _toolsRef = nullptr;

    // 延迟连接相关变量
    String _host;
    int _port;
    String _url;
    bool _useOrigin;
    String _overrideOrigin;
    unsigned long _bootTime = 0;
    unsigned long _connectDelay = 0;
    unsigned long _lastPingAt = 0;
    bool _pendingConnect = false;

    void sendJson(DynamicJsonDocument& doc) {
        String jsonString;
        serializeJson(doc, jsonString);
        int len = jsonString.length();
        uint8_t * payload = (uint8_t *)malloc(len + 1);
        if (!payload) return;
        memcpy(payload, jsonString.c_str(), len);
        payload[len] = 0x1e;
        _webSocket.sendTXT(payload, len + 1);
        free(payload);
    }
    
    void sendRaw(String txt) {
        int len = txt.length();
        uint8_t * payload = (uint8_t *)malloc(len + 1);
        if (!payload) return;
        memcpy(payload, txt.c_str(), len);
        payload[len] = 0x1e;
        _webSocket.sendTXT(payload, len + 1);
        free(payload);
    }

    virtual void handleMessage(uint8_t * payload, size_t length) = 0;
    virtual void onLoop() {}

    void performConnect() {
        Serial.printf("[%s] Free Heap: %d\n", _name.c_str(), ESP.getFreeHeap());
        Serial.printf("[%s] Attempting connection to %s:%d\n", _name.c_str(), _host.c_str(), _port);
        Serial.printf("[%s] Full URL: %s\n", _name.c_str(), _url.c_str());

        if (_isSSL) {
            // WebSockets 2.7.2 下 beginSSL() 在未提供 CA/fingerprint 时会自动走 insecure。
            _webSocket.beginSSL(_host.c_str(), (uint16_t)_port, _url.c_str(), (const char*)NULL, "");
        }
        else {
            _webSocket.begin(_host, _port, _url, "");
        }

        _extraHeaders = "";
        
        if (_overrideOrigin.length() > 0) {
             _extraHeaders += "Origin: " + _overrideOrigin;
        } else if (_useOrigin) {
            String originPrefix = _isSSL ? "https://" : "http://";
            _extraHeaders += "Origin: " + originPrefix + _host;
        }
        
        if (_extraHeaders.length() > 0) {
             _webSocket.setExtraHeaders(_extraHeaders.c_str());
        }

        _webSocket.onEvent([this](WStype_t type, uint8_t * payload, size_t length) {
            switch(type) {
                case WStype_DISCONNECTED: 
                    _isConnected = false;
                    _signalRReady = false;
                    Serial.printf("[%s] Connection Lost or Failed! (Will retry in %lu ms)\n", _name.c_str(), (unsigned long)NC_WS_RECONNECT_INTERVAL_MS);
                    break;
                case WStype_CONNECTED: 
                    _isConnected = true;
                    _signalRReady = false;
                    Serial.printf("[%s] WebSocket Connected! Sending SignalR Handshake...\n", _name.c_str());
                    sendRaw("{\"protocol\":\"json\",\"version\":1}");
                    break;
                case WStype_TEXT:
                    if (payload[0] == '{' && payload[1] == '}') {
                        Serial.printf("[%s] SignalR Handshake OK\n", _name.c_str());
                        _signalRReady = true;
                        _signalRReadyAt = millis();
                        sendRaw("{\"type\":6}");
                    } else handleMessage(payload, length);
                    break;
                case WStype_ERROR:
                    Serial.printf("[%s] WebSocket Error!\n", _name.c_str());
                    break;
            }
        });
        
        _webSocket.setReconnectInterval(NC_WS_RECONNECT_INTERVAL_MS);
        _webSocket.enableHeartbeat(NC_WS_HEARTBEAT_INTERVAL_MS, NC_WS_HEARTBEAT_TIMEOUT_MS, NC_WS_HEARTBEAT_MISSES);
    }

public:
    NcTransport(String name) : _name(name) {}
    virtual ~NcTransport() {}

    void setTools(std::vector<NcMcpTool*>& tools) { _toolsRef = &tools; }

    // 增加 connectDelay 参数，默认 0ms
    void begin(String host, int port, String url, String did, String uid, String secret, bool isSSL = false, bool useOrigin = false, String overrideOrigin = "", unsigned long connectDelay = 0) {
        _did = did; _uid = uid; _secret = secret; _isSSL = isSSL;
        _isConnected = false;
        _lastPingAt = 0;
        
        // 保存参数，延迟启动
        _host = host;
        _port = port;
        _url = url;
        _useOrigin = useOrigin;
        _overrideOrigin = overrideOrigin;
        
        _connectDelay = connectDelay;
        _bootTime = millis();
        _pendingConnect = true;
        
        if (_connectDelay > 0) {
            Serial.printf("[%s] Connection scheduled in %lu ms...\n", _name.c_str(), _connectDelay);
        }
    }

    void loop() { 
        if (_pendingConnect) {
            if (millis() - _bootTime >= _connectDelay) {
                _pendingConnect = false;
                performConnect();
            } else {
                return; // 未到启动时间，跳过 loop
            }
        }
        
        _webSocket.loop(); 
        onLoop();
    }

    void keepAlive() {
        if (_isConnected) {
            if (millis() - _lastPingAt > NC_WS_KEEPALIVE_INTERVAL_MS) {
                _lastPingAt = millis();
                sendRaw("{\"type\":6}");
            }
        }
    }

    bool isConnected() { return _isConnected; }
    void setPaused(bool paused) { _isPaused = paused; }
    bool isPaused() { return _isPaused; }
    void disconnect() { _webSocket.disconnect(); _isConnected = false; }
};

// ==========================================
// 云端子类：负责能力同步 (Sync) 和 指令触发 (Trigger)
// ==========================================
class NcCloudTransport : public NcTransport {
public:
    NcCloudTransport() : NcTransport("Cloud") {}

    void syncCapabilities() {
        if (!_isConnected) return;
        Serial.println("[Cloud] Syncing capabilities...");
        
        DynamicJsonDocument doc(6144); 
        doc["type"] = 1;
        doc["target"] = "PushSyncDeviceFunctions"; 
        
        JsonArray args = doc.createNestedArray("arguments");
        JsonObject syncData = args.createNestedObject();
        syncData["DID"] = _did;
        syncData["UID"] = _uid; 
        
        JsonArray interfaceList = syncData.createNestedArray("DevicePoolFunctionInterfaces");
        if (_toolsRef) {
            for(auto tool : *_toolsRef) tool->getInterfaces(interfaceList);
        }

        DynamicJsonDocument aiToolsDoc(2048);
        JsonArray aiToolsArr = aiToolsDoc.to<JsonArray>();
        if (_toolsRef) {
            for(auto tool : *_toolsRef) tool->getOpenAiTools(aiToolsArr);
        }
        String aiToolsStr;
        serializeJson(aiToolsArr, aiToolsStr);
        syncData["FunctionTools"] = aiToolsStr;
        syncData["FunctionToolsForChat"] = aiToolsStr;
        
        JsonArray listenerList = syncData.createNestedArray("DevicePoolFunctionListeners");
        if (_toolsRef) {
            for(auto tool : *_toolsRef) {
                for(const auto& func : tool->getFunctions()) {
                    if (func.isRealData) {
                        JsonObject listener = listenerList.createNestedObject();
                        listener["ReturnParameter"] = func.name;
                        listener["ReturnParameterDataType"] = "String";
                        listener["Description"] = func.description;
                    }
                }
            }
        }
        
        sendJson(doc);
    }

protected:
    void handleMessage(uint8_t * payload, size_t length) override {
        // 创建一个安全的临时副本，确保有 null 终止符
        char* safePayload = (char*)malloc(length + 1);
        if (!safePayload) return;
        memcpy(safePayload, payload, length);
        safePayload[length] = '\0';

        String payloadStr = safePayload;
        free(safePayload);

        // 移除 SignalR 分隔符
        if (payloadStr.endsWith("\x1e")) payloadStr.remove(payloadStr.length() - 1);
        
        if (payloadStr.length() > 0 && payloadStr != "{}") {
            Serial.printf("[Cloud] RX: %s\n", payloadStr.c_str());
        }

        if (payloadStr == "{}" || payloadStr.length() == 0) return;

        DynamicJsonDocument doc(2048);
        DeserializationError error = deserializeJson(doc, payloadStr);
        if (error) {
            Serial.printf("[Cloud] JSON parse error: %s\n", error.c_str());
            return;
        }
        
        if (doc["type"] == 1) { 
            String target = doc["target"] | "";
            String invocationId = doc["invocationId"] | "";
            JsonArray args = doc["arguments"];

            if (target.equalsIgnoreCase("SyncDeviceFunctions")) {
                Serial.println("[Cloud] Received SyncDeviceFunctions request");
                this->syncCapabilities();
            }
            else if (target.equalsIgnoreCase("SendDeviceFunctionTrigger")) {
                JsonObject triggerData = args[0];
                // 兼容大小写：优先尝试日志中显示的小写格式
                String methodName = triggerData["interfaceName"] | triggerData["InterfaceName"] | "";
                JsonArray paramList = triggerData["parameterData"].isNull() ? triggerData["ParameterData"] : triggerData["parameterData"];

                DynamicJsonDocument paramDoc(512);
                JsonObject params = paramDoc.to<JsonObject>();
                for (JsonObject p : paramList) {
                    String pName = p["parameterName"] | p["ParameterName"] | "";
                    if (pName.length() > 0) {
                        // 强制转为 String 再赋值，确保兼容性
                        String pVal = p["parameterValue"].isNull() ? p["ParameterValue"] | "" : p["parameterValue"] | "";
                        params[pName] = pVal;
                        Serial.printf("[Cloud] Param Parsed: %s = %s\n", pName.c_str(), pVal.c_str());
                    }
                }

                Serial.printf("[Cloud] Trigger: %s\n", methodName.c_str());

                String resultData = "";
                bool success = false;
                if (_toolsRef) {
                    for (auto tool : *_toolsRef) {
                        if (tool->execute(methodName, params, resultData)) {
                            success = true;
                            break;
                        }
                    }
                }

                DynamicJsonDocument returnDoc(1536);
                returnDoc["type"] = 1;
                returnDoc["target"] = "ReturnDeviceFunctionTrigger";
                JsonArray returnArgs = returnDoc.createNestedArray("arguments");
                returnArgs.add(_did);
                returnArgs.add(_uid);
                JsonObject dataObj = returnArgs.createNestedObject();
                dataObj["did"] = _did;
                dataObj["uid"] = _uid;
                
                if (triggerData.containsKey("interfaceId")) dataObj["interfaceId"] = triggerData["interfaceId"];
                else if (triggerData.containsKey("InterfaceId")) dataObj["interfaceId"] = triggerData["InterfaceId"];
                
                dataObj["interfaceName"] = methodName;
                JsonArray paramDataArr = dataObj.createNestedArray("parameterData");
                JsonObject resultParam = paramDataArr.createNestedObject();
                resultParam["parameterName"] = success ? "result" : "error";
                resultParam["parameterValue"] = success ? resultData : "Method not found";
                Serial.printf("[Cloud] Sending ReturnDeviceFunctionTrigger: %s\n", returnDoc.as<String>().c_str());
                sendJson(returnDoc);

                if (invocationId.length() > 0) {
                    DynamicJsonDocument compDoc(256);
                    compDoc["type"] = 3;
                    compDoc["invocationId"] = invocationId;
                    compDoc["result"] = "OK";
                    Serial.printf("[Cloud] Sending InvokeEdgeApiResult: %s\n", compDoc.as<String>().c_str());
                    sendJson(compDoc);
                }
            }
        }
    }
};

// ==========================================
// 本地子类：负责 100ms 高频数据推送 (PushEdgeRealData)
// ==========================================
class NcLocalTransport : public NcTransport {
public:
    NcLocalTransport() : NcTransport("Local") {}

protected:
    static const unsigned long SYNC_INITIAL_DELAY_MS = 500;
    static const unsigned long SYNC_RETRY_INTERVAL_MS = 3000;
    static const unsigned long SYNC_GIVE_UP_MS = 300000; // 5 分钟后放弃

    int _syncAttempts = 0;
    unsigned long _lastSyncAt = 0;
    unsigned long _syncStartAt = 0;
    bool _syncCompleted = false;
    String _syncInvocationId;

    void sendSyncPayload() {
        DynamicJsonDocument doc(6144);
        doc["type"] = 1;
        doc["invocationId"] = _syncInvocationId;
        doc["target"] = "PushSyncDeviceFunctions";

        JsonArray args = doc.createNestedArray("arguments");
        JsonObject syncData = args.createNestedObject();
        syncData["DID"] = _did;
        syncData["UID"] = _uid;

        JsonArray interfaceList = syncData.createNestedArray("DevicePoolFunctionInterfaces");
        if (_toolsRef) {
            for (auto tool : *_toolsRef) tool->getInterfaces(interfaceList);
        }

        DynamicJsonDocument aiToolsDoc(2048);
        JsonArray aiToolsArr = aiToolsDoc.to<JsonArray>();
        if (_toolsRef) {
            for (auto tool : *_toolsRef) tool->getOpenAiTools(aiToolsArr);
        }
        String aiToolsStr;
        serializeJson(aiToolsArr, aiToolsStr);
        syncData["FunctionTools"] = aiToolsStr;
        syncData["FunctionToolsForChat"] = aiToolsStr;

        JsonArray listenerList = syncData.createNestedArray("DevicePoolFunctionListeners");
        if (_toolsRef) {
            for (auto tool : *_toolsRef) {
                for (const auto& func : tool->getFunctions()) {
                    if (func.isRealData) {
                        JsonObject listener = listenerList.createNestedObject();
                        listener["ReturnParameter"] = func.name;
                        listener["ReturnParameterDataType"] = "String";
                        listener["Description"] = func.description;
                    }
                }
            }
        }

        sendJson(doc);
    }

    void syncCapabilitiesWithRetry() {
        if (_syncCompleted || !_signalRReady) return;

        unsigned long now = millis();

        // 首次同步：记录开始时间，等待初始延迟
        if (_syncAttempts == 0) {
            if (_syncStartAt == 0) _syncStartAt = now;
            if (now - _signalRReadyAt < SYNC_INITIAL_DELAY_MS) return;
        } else {
            // 超时保护：5 分钟内都没收到 ACK，停止重试
            if (now - _syncStartAt > SYNC_GIVE_UP_MS) {
                _syncCompleted = true;
                Serial.printf("[Local] Sync gave up after %d attempts (5min timeout)\n", _syncAttempts);
                return;
            }
            if (now - _lastSyncAt < SYNC_RETRY_INTERVAL_MS) return;
        }

        _syncAttempts++;
        _lastSyncAt = now;
        _syncInvocationId = "sync_" + String(now);
        Serial.printf("[Local] Syncing capabilities (attempt %d, invId=%s)...\n", _syncAttempts, _syncInvocationId.c_str());
        sendSyncPayload();
    }

    void handleMessage(uint8_t * payload, size_t length) override {
        // 创建一个安全的临时副本，确保有 null 终止符
        char* safePayload = (char*)malloc(length + 1);
        if (!safePayload) return;
        memcpy(safePayload, payload, length);
        safePayload[length] = '\0';

        String payloadStr = safePayload;
        free(safePayload);

        // 移除 SignalR 分隔符
        if (payloadStr.endsWith("\x1e")) payloadStr.remove(payloadStr.length() - 1);
        if (payloadStr == "{}" || payloadStr.length() == 0) return;

        DynamicJsonDocument doc(2048);
        DeserializationError error = deserializeJson(doc, payloadStr);
        if (error) return;

        // 处理 Completion (type: 3) 消息，这是 Invoke 的返回结果
        if (doc["type"] == 3) {
            String invocationId = doc["invocationId"] | "";

            // 同步 ACK：服务端确认 PushSyncDeviceFunctions 处理成功
            if (invocationId == _syncInvocationId && _syncInvocationId.length() > 0) {
                String result = doc["result"] | "";
                if (result == "OK") {
                    _syncCompleted = true;
                    Serial.printf("[Local] Sync ACK received (attempt %d) - SUCCESS\n", _syncAttempts);
                } else {
                    Serial.printf("[Local] Sync ACK received but failed (result=%s), will retry...\n", result.c_str());
                }
                return;
            }

            // Token 返回结果
            if (invocationId == _tokenInvocationId && !doc["result"].isNull()) {
                _token = doc["result"]["token"] | doc["result"]["Token"] | "";
                _tokenExpireTime = doc["result"]["expireTime"] | doc["result"]["ExpireTime"] | "";
                _tokenExpiresMilliseconds = doc["result"]["expiresMilliseconds"] | doc["result"]["ExpiresMilliseconds"] | 0;
                _tokenLastReceivedAt = millis();
                Serial.printf("[Local] Token Received: %s (Expires in %d ms)\n", _token.substring(0, 10).c_str(), _tokenExpiresMilliseconds);
            }
        }
    }

    void onLoop() override {
        if (!_isConnected || _isPaused) {
            _syncCompleted = false;
            _syncAttempts = 0;
            _syncStartAt = 0;
            return;
        }

        syncCapabilitiesWithRetry();
        
        static unsigned long lastPush = 0;
        if (millis() - lastPush < 100) return;
        lastPush = millis();

        if (!_toolsRef) return;

        for (auto tool : *_toolsRef) {
            for (const auto& func : tool->getFunctions()) {
                if (!func.isRealData) continue;

                StaticJsonDocument<64> emptyDoc;
                JsonObject emptyParams = emptyDoc.to<JsonObject>();
                String result = func.callback(emptyParams);

                DynamicJsonDocument doc(1024);
                doc["type"] = 1;
                doc["target"] = "PushEdgeRealData";
                JsonArray args = doc.createNestedArray("arguments");
                JsonObject dataObj = args.createNestedObject();

                String fullName = func.name;
                int dashIdx = fullName.lastIndexOf('-');
                dataObj["methodName"] = (dashIdx != -1) ? fullName.substring(dashIdx + 1) : fullName;
                dataObj["className"] = (dashIdx != -1) ? fullName.substring(0, dashIdx) : "";
                dataObj["fullMethodName"] = fullName;
                dataObj["description"] = func.description;
                dataObj["result"] = result;
                dataObj["intervalMilliseconds"] = 100;
                dataObj["timestamp"] = "1901-01-01T12:00:00Z"; 

                sendJson(doc);
            }
        }
    }

    // --- 新增 Token 获取支持 ---
    String _token;
    String _tokenExpireTime;
    int _tokenExpiresMilliseconds = 0;
    unsigned long _tokenLastReceivedAt = 0;
    String _tokenInvocationId;

public:
    void requestToken() {
        if (!_isConnected) return;
        
        _tokenInvocationId = String(millis()); // 简单作为 ID
        DynamicJsonDocument doc(256);
        doc["type"] = 1;
        doc["invocationId"] = _tokenInvocationId;
        doc["target"] = "GetToken";
        doc.createNestedArray("arguments"); // 空参数列表
        
        sendJson(doc);
        Serial.println("[Local] Requested Token (SignalR Invoke GetToken)");
    }

    String getToken() { return _token; }
    String getTokenExpireTime() { return _tokenExpireTime; }
    
    bool isTokenExpiring() {
        if (_token.length() == 0) return true;
        if ( _tokenExpiresMilliseconds <= 0) return false;
        
        unsigned long elapsed = millis() - _tokenLastReceivedAt;
        // 提前 5 分钟 (300,000 毫秒) 刷新
        return (elapsed + 300000) >= (unsigned long)_tokenExpiresMilliseconds;
    }
};

#endif
