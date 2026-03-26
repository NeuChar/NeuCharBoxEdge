#include "EdgeOTA/Utils/JsonUtils.hpp"
#include <fstream>
#include <sstream>
#include <stdexcept>

namespace EdgeOTA::Utils {
namespace {
std::string readText(const std::filesystem::path& path) {
    std::ifstream in(path);
    if (!in) return {};
    std::ostringstream oss;
    oss << in.rdbuf();
    return oss.str();
}

void writeText(const std::filesystem::path& path, const std::string& text) {
    std::filesystem::create_directories(path.parent_path());
    std::ofstream out(path);
    if (!out) throw std::runtime_error("无法写入文件: " + path.string());
    out << text;
}

std::string getString(json_object* obj, const char* key) {
    json_object* value = nullptr;
    if (!json_object_object_get_ex(obj, key, &value) || value == nullptr) return {};
    return json_object_get_string(value) ? json_object_get_string(value) : "";
}
}

std::vector<Entity::OTAConfig> loadOTAConfigs(const std::filesystem::path& path) {
    std::vector<Entity::OTAConfig> result;
    auto text = readText(path);
    if (text.empty()) return result;
    json_object* root = json_tokener_parse(text.c_str());
    if (!root || !json_object_is_type(root, json_type_array)) { if (root) json_object_put(root); return result; }
    const int len = json_object_array_length(root);
    for (int i = 0; i < len; ++i) {
        auto* item = json_object_array_get_idx(root, i);
        if (!item) continue;
        result.push_back({
            getString(item, "firmwareType"), getString(item, "did"), getString(item, "uid"),
            getString(item, "currentVersion"), getString(item, "remoteVersion"), getString(item, "remoteFilePath"),
            getString(item, "ignoreVersion"), getString(item, "filePath")
        });
    }
    json_object_put(root);
    return result;
}

void saveOTAConfigs(const std::filesystem::path& path, const std::vector<Entity::OTAConfig>& configs) {
    json_object* arr = json_object_new_array();
    for (const auto& c : configs) {
        json_object* o = json_object_new_object();
        json_object_object_add(o, "firmwareType", json_object_new_string(c.firmwareType.c_str()));
        json_object_object_add(o, "did", json_object_new_string(c.did.c_str()));
        json_object_object_add(o, "uid", json_object_new_string(c.uid.c_str()));
        json_object_object_add(o, "currentVersion", json_object_new_string(c.currentVersion.c_str()));
        json_object_object_add(o, "remoteVersion", json_object_new_string(c.remoteVersion.c_str()));
        json_object_object_add(o, "remoteFilePath", json_object_new_string(c.remoteFilePath.c_str()));
        json_object_object_add(o, "ignoreVersion", json_object_new_string(c.ignoreVersion.c_str()));
        json_object_object_add(o, "filePath", json_object_new_string(c.filePath.c_str()));
        json_object_array_add(arr, o);
    }
    writeText(path, json_object_to_json_string_ext(arr, JSON_C_TO_STRING_PRETTY));
    json_object_put(arr);
}

std::vector<Entity::OTAEdgeConfig> loadOTAEdgeConfigs(const std::filesystem::path& path) {
    std::vector<Entity::OTAEdgeConfig> result;
    auto text = readText(path);
    if (text.empty()) return result;
    json_object* root = json_tokener_parse(text.c_str());
    if (!root || !json_object_is_type(root, json_type_array)) { if (root) json_object_put(root); return result; }
    const int len = json_object_array_length(root);
    for (int i = 0; i < len; ++i) {
        auto* item = json_object_array_get_idx(root, i);
        if (!item) continue;
        result.push_back({
            getString(item, "firmwareType"), getString(item, "did"), getString(item, "uid"),
            getString(item, "remoteVersion"), getString(item, "remoteFilePath")
        });
    }
    json_object_put(root);
    return result;
}

void saveOTAEdgeConfigs(const std::filesystem::path& path, const std::vector<Entity::OTAEdgeConfig>& configs) {
    json_object* arr = json_object_new_array();
    for (const auto& c : configs) {
        json_object* o = json_object_new_object();
        json_object_object_add(o, "firmwareType", json_object_new_string(c.firmwareType.c_str()));
        json_object_object_add(o, "did", json_object_new_string(c.did.c_str()));
        json_object_object_add(o, "uid", json_object_new_string(c.uid.c_str()));
        json_object_object_add(o, "remoteVersion", json_object_new_string(c.remoteVersion.c_str()));
        json_object_object_add(o, "remoteFilePath", json_object_new_string(c.remoteFilePath.c_str()));
        json_object_array_add(arr, o);
    }
    writeText(path, json_object_to_json_string_ext(arr, JSON_C_TO_STRING_PRETTY));
    json_object_put(arr);
}

std::optional<Response::OTAResponse> parseOTAResponseData(const std::string& jsonText, bool& success, std::string& message) {
    success = false;
    message.clear();
    json_object* root = json_tokener_parse(jsonText.c_str());
    if (!root || !json_object_is_type(root, json_type_object)) {
        if (root) json_object_put(root);
        message = "JSON解析失败";
        return std::nullopt;
    }

    json_object* successObj = nullptr;
    if (json_object_object_get_ex(root, "success", &successObj)) {
        success = json_object_get_boolean(successObj);
    }
    message = getString(root, "message");
    json_object* data = nullptr;
    if (!json_object_object_get_ex(root, "data", &data) || data == nullptr || json_object_is_type(data, json_type_null)) {
        json_object_put(root);
        return std::nullopt;
    }

    Response::OTAResponse response{};
    response.id = getString(data, "id");
    response.devicePoolId = getString(data, "devicePoolId");
    response.operatingSystem = getString(data, "operatingSystem");
    response.developmentMode = getString(data, "developmentMode");
    response.otaMode = getString(data, "otaMode");
    response.firmwareType = getString(data, "firmwareType");
    response.firmwareVersion = getString(data, "firmwareVersion");
    response.firmwarePackage = getString(data, "firmwarePackage");
    response.updateDescription = getString(data, "updateDescription");
    json_object* sizeObj = nullptr;
    if (json_object_object_get_ex(data, "firmwareSize", &sizeObj)) {
        response.firmwareSize = json_object_get_double(sizeObj);
    }
    json_object_put(root);
    return response;
}

} // namespace EdgeOTA::Utils
