#include <WiFi.h>
#include "NeuCharEdge.h"

// ==========================================
// 0. 硬编码配置
// ==========================================
const char* HARDCODED_DID = "<从Neuchar.com获取的DID>";
const char* HARDCODED_UID = "<从Neuchar.com获取的UID>";
const char* DEVICE_NAME = "<定义设备名称>";
// 是否启用云端 WebSocket 连接：
// true: 连接到 www.neuchar.com 进行远程控制与调试
// false: 仅在局域网内工作，断开与云端的所有实时通信
const bool ENABLE_CLOUD = true;

// 私钥，从Neuchar.com获取的私钥
const char* PRIVATE_KEY = R"rawliteral(-----BEGIN RSA PRIVATE KEY-----
MIIEowIBAAKCAQEAxU+UJN4l7JOaEymuJ8R/DVIU3K5ZGe5ECjcf1bq/JF3jcAP/
...(省略中间内容)
t7dMSE58VaFYoDLPVcBtb0LLOen3HzVaBe+V/ke7LxH97pWoYM58Vd3K3ebZcjw5
AjLLlRzTEo6T91JqjeQB/HFGbkxmWnrwJFpGjX9G8dOFzEaF/NtQ
-----END RSA PRIVATE KEY-----
)rawliteral";

// 数字管接 ESP32：CLK=时钟引脚, DIO=数据引脚, VCC/GND 接电源
#define TUBE_CLK_PIN 18
#define TUBE_DIO_PIN 19
#define BOOT_BTN 0   // ESP32 板载 BOOT 按键

// ==========================================
// 1. 业务逻辑 (Service) - 用户关注区域
// ==========================================
// TM1637 四线数字管：仅用 CLK、DIO 与 ESP32 通信
class TubeService {
private:
    int _clkPin;
    int _dioPin;
    uint8_t _currentSegments[4];
    char _currentDisplay[5];

    static const uint8_t SEG_0_9[10];
    static const uint8_t ADDR_AUTO;
    static const uint8_t STARTADDR;
    uint8_t _brightness;

    void start() {
        digitalWrite(_dioPin, HIGH);
        digitalWrite(_clkPin, HIGH);
        delayMicroseconds(2);
        digitalWrite(_dioPin, LOW);
        digitalWrite(_clkPin, LOW);
    }
    void stop() {
        digitalWrite(_dioPin, LOW);
        digitalWrite(_clkPin, HIGH);
        delayMicroseconds(2);
        digitalWrite(_dioPin, HIGH);
    }
    void writeByte(uint8_t b) {
        for (int i = 0; i < 8; i++) {
            digitalWrite(_clkPin, LOW);
            digitalWrite(_dioPin, (b & 0x01) ? HIGH : LOW);
            b >>= 1;
            digitalWrite(_clkPin, HIGH);
        }
        digitalWrite(_clkPin, LOW);
        digitalWrite(_dioPin, HIGH);
        digitalWrite(_clkPin, HIGH);
        digitalWrite(_clkPin, LOW);
    }
    void writeSegments(uint8_t segs[4]) {
        memcpy(_currentSegments, segs, 4);
        for (int i = 0; i < 4; i++) _currentDisplay[i] = segmentToChar(segs[i]);
        _currentDisplay[4] = '\0';

        start();
        writeByte(ADDR_AUTO);
        stop();
        start();
        writeByte(STARTADDR);
        for (int i = 0; i < 4; i++) writeByte(segs[i]);
        stop();
        start();
        writeByte(_brightness & 0x8F);
        stop();
    }
    char segmentToChar(uint8_t seg) {
        if (seg == 0x40) return '-';
        if (seg == 0x00) return ' ';
        for (int i = 0; i < 10; i++)
            if (SEG_0_9[i] == seg) return (char)('0' + i);
        return '?';
    }

public:
    TubeService(int clkPin, int dioPin) : _clkPin(clkPin), _dioPin(dioPin), _brightness(0x8F) {
        pinMode(_clkPin, OUTPUT);
        pinMode(_dioPin, OUTPUT);
        memset(_currentSegments, 0, 4);
        memset(_currentDisplay, ' ', 5);
        _currentDisplay[4] = '\0';
    }

    void displayNumber(int number) {
        if (number > 9999) number = 9999;
        if (number < -999) number = -999;
        uint8_t segs[4] = {0, 0, 0, 0};
        bool neg = (number < 0);
        number = neg ? -number : number;
        String s = String(number);
        int len = s.length();
        int startPos = (neg && len < 4) ? (4 - len - 1) : (4 - len);
        if (neg && len < 4) {
            segs[startPos] = 0x40;
            for (int i = 0; i < len; i++)
                segs[startPos + 1 + i] = SEG_0_9[s[i] - '0'];
        } else {
            for (int i = 0; i < len; i++)
                segs[startPos + i] = SEG_0_9[s[i] - '0'];
        }
        writeSegments(segs);
    }

    void clear() {
        uint8_t segs[4] = {0, 0, 0, 0};
        writeSegments(segs);
    }

    String getCurrentDisplay() {
        return String(_currentDisplay);
    }
};

const uint8_t TubeService::SEG_0_9[10] = {
    0x3f, 0x06, 0x5b, 0x4f, 0x66, 0x6d, 0x7d, 0x07, 0x7f, 0x6f
};
const uint8_t TubeService::ADDR_AUTO  = 0x40;
const uint8_t TubeService::STARTADDR  = 0xc0;

// ==========================================
// 2. 接口定义 (Tool) - 用户关注区域
// 注意：方法入参需要兼容字符串类型
// ==========================================
class EdgeTubeTool : public NcMcpTool {
public:
    EdgeTubeTool(TubeService* service) : NcMcpTool("Senparc.Xncf.NeuCharEdge.ESP32.DigitalTubeController") {
        registerAction("DisplayNumber", "数字管显示数字",
            "{\"number\":{\"type\":\"string\",\"description\":\"最大4位字符串，范围'-999'至'9999'\"}}",
            [service](JsonObject params) -> String {
                const char* numStr = params["number"].as<const char*>();
                if (!numStr) return "ERR:missing number";
                int n = atoi(numStr);
                Serial.printf("[Action] DisplayNumber: %d\n", n);
                service->displayNumber(n);
                return String(n);
            });
        registerAction("Clear", "清空数字管显示的内容", "{}", [service](JsonObject params) -> String {
            service->clear();
            return "OK";
        });
        registerAction("GetCurrentDisplay", "获取当前数字管显示的内容", "{}",
            [service](JsonObject params) -> String {
                return service->getCurrentDisplay();
            }, true);
    }
};

TubeService* tubeService = nullptr;
EdgeTubeTool* tubeTool = nullptr;

// ==========================================
// 3. 主程序 - 标准模版
// ==========================================
void setup() {
    Serial.begin(115200);

    // 初始化 Service 和 Tool（数字管接 ESP32：CLK=TUBE_CLK_PIN, DIO=TUBE_DIO_PIN）
    tubeService = new TubeService(TUBE_CLK_PIN, TUBE_DIO_PIN);
    tubeTool = new EdgeTubeTool(tubeService);

    // 注册 Tool
    NcEdge.registerTool(*tubeTool);

    // 启动 NeuChar Edge SDK (托管配网、RSA、云端通信)
    // 最后一个参数控制是否启用云端连接 (ENABLE_CLOUD)
    NcEdge.begin(DEVICE_NAME, HARDCODED_DID, HARDCODED_UID, PRIVATE_KEY, BOOT_BTN, ENABLE_CLOUD);
}

void loop() {
    // 运行 SDK 守护进程
    NcEdge.run();
}
