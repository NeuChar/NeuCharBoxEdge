#include "EdgeOTA/OTAHelper.hpp"
#include "EdgeOTA/Utils/FileUtils.hpp"
#include "EdgeOTA/Utils/HttpUtils.hpp"
#include "EdgeOTA/Utils/JsonUtils.hpp"
#include <filesystem>
#include <fstream>

namespace EdgeOTA {
using namespace EdgeOTA::Response;
using namespace EdgeOTA::Request;
using namespace EdgeOTA::Entity;
using namespace EdgeOTA::Utils;

std::filesystem::path OTAHelper::getVersionFilePath() { return std::filesystem::current_path() / "OTAVersion.json"; }
std::filesystem::path OTAHelper::getVersionDownloadDir() { return std::filesystem::current_path() / "OTAVersionDownload"; }
std::filesystem::path OTAHelper::getExtractDir() { return getVersionDownloadDir() / "OTAExtract"; }

std::tuple<OTAConfig*, std::vector<OTAConfig>> OTAHelper::getOTAConfig(const std::string& uid, const std::string& did, const std::string& firmwareType) {
    auto configs = loadOTAConfigs(getVersionFilePath());
    OTAConfig* found = nullptr;
    for (auto& c : configs) {
        if (c.uid == uid && c.did == did && c.firmwareType == firmwareType) { found = &c; break; }
    }
    return {found, configs};
}

OTABaseResponse<std::string> OTAHelper::getRemoteVersionInfo(const std::string& baseUrl, const std::string& api, const GetRemoteVersionInfoRequest& req, const std::string& token) {
    try {
        std::string cleanBase = baseUrl;
        if (!cleanBase.empty() && cleanBase.back() == '/') cleanBase.pop_back();
        std::string cleanApi = api.empty() ? "" : (api.front() == '/' ? api : "/" + api);
        std::map<std::string, std::string> params{{"did", req.did}, {"uid", req.uid}, {"firmwareType", req.firmwareType}, {"appKey", req.appKey}, {"appSecret", req.appSecret}};
        std::string requestUrl = cleanBase + cleanApi + "?" + buildQueryString(params);
        std::map<std::string, std::string> headers{{"Accept", "application/json"}};
        if (!token.empty()) headers["Authorization"] = "Bearer " + token;
        auto http = httpGet(requestUrl, headers);
        if (http.statusCode < 200 || http.statusCode >= 300) return {false, "服务器返回错误: " + std::to_string(http.statusCode)};
        bool ok = false; std::string message;
        auto parsed = parseOTAResponseData(http.body, ok, message);
        if (!ok) return {false, message.empty() ? "远程接口返回失败" : message};
        if (!parsed) return {false, "远程固件版本为空"};

        auto configs = loadOTAConfigs(getVersionFilePath());
        OTAConfig* found = nullptr;
        for (auto& c : configs) if (c.uid == req.uid && c.did == req.did && c.firmwareType == req.firmwareType) { found = &c; break; }
        if (!found) {
            configs.push_back({req.firmwareType, req.did, req.uid, DefaultRemoteVersion, "", "", "", ""});
            found = &configs.back();
        }
        if (found->remoteVersion != parsed->firmwareVersion || found->remoteFilePath != parsed->firmwarePackage) {
            found->remoteVersion = parsed->firmwareVersion;
            found->remoteFilePath = parsed->firmwarePackage;
            saveOTAConfigs(getVersionFilePath(), configs);
        }
        return {true, "", found->remoteVersion};
    } catch (const std::exception& ex) {
        return {false, ex.what()};
    }
}

OTABaseResponse<CheckForUpdateResponse> OTAHelper::checkForUpdate(const CheckForUpdateRequest& req) {
    try {
        auto configs = loadOTAConfigs(getVersionFilePath());
        for (const auto& c : configs) {
            if (c.uid == req.uid && c.did == req.did && c.firmwareType == req.firmwareType) {
                if (c.remoteVersion.empty()) return {false, "远程版本号为空"};
                bool need = c.ignoreVersion != c.remoteVersion && c.currentVersion != c.remoteVersion;
                return {true, need ? "有新版本" : "没有新版本", {need, c.currentVersion, c.remoteVersion}};
            }
        }
        return {false, "版本信息不存在"};
    } catch (const std::exception& ex) {
        return {false, ex.what()};
    }
}

OTABaseResponse<std::string> OTAHelper::downloadUpdate(const DownloadUpdateRequest& req, const std::string& token) {
    try {
        auto configs = loadOTAConfigs(getVersionFilePath());
        OTAConfig* found = nullptr;
        for (auto& c : configs) if (c.uid == req.uid && c.did == req.did && c.firmwareType == req.firmwareType) { found = &c; break; }
        if (!found) return {false, "版本信息不存在"};
        if (found->remoteFilePath.empty()) return {false, "更新包下载地址为空"};

        std::string cleanBase = req.baseUrl;
        if (!cleanBase.empty() && cleanBase.back() == '/') cleanBase.pop_back();
        std::string remotePackUrl = cleanBase + found->remoteFilePath;
        auto downloadDir = getVersionDownloadDir();
        ensureDirectory(downloadDir);
        auto downloadPath = downloadDir / std::filesystem::path(found->remoteFilePath).filename();
        if (!std::filesystem::exists(downloadPath)) {
            std::map<std::string, std::string> headers;
            if (!token.empty()) headers["Authorization"] = "Bearer " + token;
            auto http = httpGet(remotePackUrl, headers);
            if (http.statusCode < 200 || http.statusCode >= 300) return {false, "下载失败，HTTP状态码: " + std::to_string(http.statusCode)};
            std::ofstream out(downloadPath, std::ios::binary);
            out.write(http.body.data(), static_cast<std::streamsize>(http.body.size()));
        }
        auto extractPath = getExtractDir();
        extractZip(downloadPath, extractPath);
        std::filesystem::remove(extractPath / "appsettings.json");
        for (auto const& entry : std::filesystem::recursive_directory_iterator(extractPath)) {
            if (!entry.is_regular_file()) continue;
            auto name = entry.path().filename().string();
            if (name.rfind("EdgeOTA", 0) == 0) std::filesystem::remove(entry.path());
        }
        return {true, "下载并解压成功", extractPath.string()};
    } catch (const std::exception& ex) {
        return {false, ex.what()};
    }
}

std::filesystem::path OTAHelper::getVersionFilePath_E() { return std::filesystem::current_path() / "EdgesOTAVersion.json"; }
std::filesystem::path OTAHelper::getWWWRootDir() { return std::filesystem::current_path() / "wwwroot"; }
std::filesystem::path OTAHelper::getVersionDownloadDir_E() { return getWWWRootDir() / "EdgesOTAVersionDownload"; }

std::tuple<OTAEdgeConfig*, std::vector<OTAEdgeConfig>> OTAHelper::getOTAEdgeConfig(const std::string& uid, const std::string& did, const std::string& firmwareType) {
    auto configs = loadOTAEdgeConfigs(getVersionFilePath_E());
    OTAEdgeConfig* found = nullptr;
    for (auto& c : configs) if (c.uid == uid && c.did == did && c.firmwareType == firmwareType) { found = &c; break; }
    return {found, configs};
}

OTABaseResponse<OTAResponse> OTAHelper::getOTAInfoForEdgeRequest(const std::string& uid, const std::string& did, const std::string& firmwareType) {
    auto configs = loadOTAEdgeConfigs(getVersionFilePath_E());
    for (const auto& c : configs) {
        if (c.uid == uid && c.did == did && c.firmwareType == firmwareType) {
            OTAResponse res{};
            res.firmwareType = firmwareType;
            res.firmwareVersion = c.remoteVersion;
            res.firmwarePackage = c.remoteFilePath;
            return {true, "", res};
        }
    }
    return {false, "版本信息不存在", {}};
}

OTABaseResponse<std::string> OTAHelper::getRemoteVersionInfoForEdge(const std::string& baseUrl, const std::string& api, const GetRemoteVersionInfoRequest& req, const std::string& token) {
    try {
        std::string cleanBase = baseUrl;
        if (!cleanBase.empty() && cleanBase.back() == '/') cleanBase.pop_back();
        std::string cleanApi = api.empty() ? "" : (api.front() == '/' ? api : "/" + api);
        std::map<std::string, std::string> params{{"did", req.did}, {"uid", req.uid}, {"firmwareType", req.firmwareType}, {"appKey", req.appKey}, {"appSecret", req.appSecret}};
        std::string requestUrl = cleanBase + cleanApi + "?" + buildQueryString(params);
        std::map<std::string, std::string> headers{{"Accept", "application/json"}};
        if (!token.empty()) headers["Authorization"] = "Bearer " + token;
        auto http = httpGet(requestUrl, headers);
        if (http.statusCode < 200 || http.statusCode >= 300) return {false, "服务器返回错误: " + std::to_string(http.statusCode)};
        bool ok = false; std::string message;
        auto parsed = parseOTAResponseData(http.body, ok, message);
        if (!ok) return {false, message.empty() ? "远程接口返回失败" : message};
        if (!parsed) return {false, "远程固件版本为空"};

        auto configs = loadOTAEdgeConfigs(getVersionFilePath_E());
        OTAEdgeConfig* found = nullptr;
        for (auto& c : configs) if (c.uid == req.uid && c.did == req.did && c.firmwareType == req.firmwareType) { found = &c; break; }
        if (!found) {
            configs.push_back({req.firmwareType, req.did, req.uid, "", ""});
            found = &configs.back();
        }
        if (found->remoteVersion != parsed->firmwareVersion) {
            std::string remotePackUrl = cleanBase + parsed->firmwarePackage;
            auto downloadDir = getVersionDownloadDir_E();
            ensureDirectory(downloadDir);
            auto downloadPath = downloadDir / std::filesystem::path(remotePackUrl).filename();
            if (!std::filesystem::exists(downloadPath)) {
                auto bin = httpGet(remotePackUrl, headers);
                if (bin.statusCode < 200 || bin.statusCode >= 300) return {false, "下载失败，HTTP状态码: " + std::to_string(bin.statusCode)};
                std::ofstream out(downloadPath, std::ios::binary);
                out.write(bin.body.data(), static_cast<std::streamsize>(bin.body.size()));
            }
            found->remoteVersion = parsed->firmwareVersion;
            auto rel = std::filesystem::relative(downloadPath, getWWWRootDir());
            found->remoteFilePath = "/" + rel.string();
            saveOTAEdgeConfigs(getVersionFilePath_E(), configs);
        }
        return {true, "", found->remoteVersion};
    } catch (const std::exception& ex) {
        return {false, ex.what()};
    }
}

} // namespace EdgeOTA
