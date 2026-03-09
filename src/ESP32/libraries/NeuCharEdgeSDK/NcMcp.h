#ifndef NC_MCP_H
#define NC_MCP_H

#include <Arduino.h>
#include <ArduinoJson.h>
#include <vector>
#include <functional>

using McpActionCallback = std::function<String(JsonObject)>;

struct McpFunctionDefinition {
    String name;
    String description;
    String paramSchema; 
    McpActionCallback callback;
    bool isRealData = false;
};

class NcMcpTool {
protected:
    String _toolName;
    std::vector<McpFunctionDefinition> _functions;

public:
    NcMcpTool(String name) : _toolName(name) {}
    String getName() { return _toolName; }

    void registerAction(String actionName, String description, String paramSchema, McpActionCallback callback, bool isRealData = false) {
        McpFunctionDefinition func;
        func.name = _toolName + "-" + actionName;
        func.description = description;
        func.paramSchema = paramSchema;
        func.callback = callback;
        func.isRealData = isRealData;
        _functions.push_back(func);
    }

    bool execute(String actionName, JsonObject params, String& result) {
        for (const auto& func : _functions) {
            if (func.name.equalsIgnoreCase(actionName)) {
                result = func.callback(params);
                return true;
            }
        }
        return false;
    }

    // 1. 生成云端 UI 所需的接口定义 (DevicePoolFunctionInterface_VD)
    void getInterfaces(JsonArray& list) {
        for (const auto& func : _functions) {
            JsonObject interfaceObj = list.createNestedObject();
            interfaceObj["InterfaceName"] = func.name;
            interfaceObj["InterfaceDescription"] = func.description;
            interfaceObj["ReturnMessageType"] = "string";
            JsonArray paramsArr = interfaceObj.createNestedArray("DevicePoolFunctionInterfaceParameters");
            
            StaticJsonDocument<512> schemaDoc;
            deserializeJson(schemaDoc, func.paramSchema);
            JsonObject schemaObj = schemaDoc.as<JsonObject>();
            for (JsonPair p : schemaObj) {
                JsonObject pObj = paramsArr.createNestedObject();
                pObj["ParameterName"] = p.key().c_str();
                
                // 支持两种格式：
                // 1. 简单格式: {"number":"string"}  -> 描述使用参数名
                // 2. 扩展格式: {"number":{"type":"string","description":"要显示的数字"}} -> 使用自定义描述
                if (p.value().is<JsonObject>()) {
                    JsonObject paramDef = p.value().as<JsonObject>();
                    pObj["ParameterDescription"] = paramDef["description"] | p.key().c_str();
                } else {
                    pObj["ParameterDescription"] = p.key().c_str();
                }
                pObj["ParameterType"] = "input"; // 云端通常用 input 作为通用输入类型
            }
        }
    }

    // 2. 生成 AI 对话所需的 OpenAI Tool 规范 (Tool 数组)
    void getOpenAiTools(JsonArray& list) {
        for (const auto& func : _functions) {
            JsonObject toolObj = list.createNestedObject();
            toolObj["type"] = "function";
            JsonObject fObj = toolObj.createNestedObject("function");
            fObj["name"] = func.name;
            fObj["description"] = func.description;
            
            JsonObject paramsObj = fObj.createNestedObject("parameters");
            paramsObj["type"] = "object";
            JsonObject props = paramsObj.createNestedObject("properties");
            JsonArray requiredArr = paramsObj.createNestedArray("required");
            
            StaticJsonDocument<512> schemaDoc;
            deserializeJson(schemaDoc, func.paramSchema);
            JsonObject schemaObj = schemaDoc.as<JsonObject>();
            for (JsonPair p : schemaObj) {
                JsonObject prop = props.createNestedObject(p.key().c_str());
                
                // 支持两种格式：
                // 1. 简单格式: {"number":"string"}  -> 只设置 type
                // 2. 扩展格式: {"number":{"type":"string","description":"要显示的数字"}} -> 设置 type 和 description
                if (p.value().is<JsonObject>()) {
                    JsonObject paramDef = p.value().as<JsonObject>();
                    prop["type"] = paramDef["type"] | "string";
                    if (paramDef.containsKey("description")) {
                        prop["description"] = paramDef["description"].as<String>();
                    }
                } else {
                    // 兼容旧格式
                    prop["type"] = p.value().as<const char*>();
                }
                requiredArr.add(p.key().c_str());
            }
        }
    }
    
    // 3. 生成 MCP 协议所需的 Tools 列表 (JSON-RPC 格式)
    void getMcpTools(JsonArray& list) {
        for (const auto& func : _functions) {
            // MCP Tool Object Structure:
            // {
            //   "name": "...",
            //   "description": "...",
            //   "inputSchema": { "type": "object", "properties": {...}, "required": [...] }
            // }
            JsonObject toolObj = list.createNestedObject();
            toolObj["name"] = func.name;
            toolObj["description"] = func.description;
            
            JsonObject inputSchema = toolObj.createNestedObject("inputSchema");
            inputSchema["type"] = "object";
            JsonObject props = inputSchema.createNestedObject("properties");
            JsonArray requiredArr = inputSchema.createNestedArray("required");
            
            StaticJsonDocument<512> schemaDoc;
            deserializeJson(schemaDoc, func.paramSchema);
            JsonObject schemaObj = schemaDoc.as<JsonObject>();
            for (JsonPair p : schemaObj) {
                JsonObject prop = props.createNestedObject(p.key().c_str());
                
                // 支持两种格式：
                // 1. 简单格式: {"number":"string"}  -> 只设置 type
                // 2. 扩展格式: {"number":{"type":"string","description":"要显示的数字"}} -> 设置 type 和 description
                if (p.value().is<JsonObject>()) {
                    JsonObject paramDef = p.value().as<JsonObject>();
                    prop["type"] = paramDef["type"] | "string";
                    if (paramDef.containsKey("description")) {
                        prop["description"] = paramDef["description"].as<String>();
                    }
                } else {
                    // 兼容旧格式
                    prop["type"] = p.value().as<const char*>();
                }
                requiredArr.add(p.key().c_str());
            }
        }
    }

    const std::vector<McpFunctionDefinition>& getFunctions() { return _functions; }
};

#endif
