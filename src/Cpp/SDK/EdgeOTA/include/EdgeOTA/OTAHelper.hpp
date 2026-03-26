#pragma once
#include <filesystem>
#include <string>
#include <tuple>
#include <vector>
#include "EdgeOTA/Entity/OTAConfig.hpp"
#include "EdgeOTA/Entity/OTAEdgeConfig.hpp"
#include "EdgeOTA/Request/OTARequest.hpp"
#include "EdgeOTA/Response/CheckForUpdateResponse.hpp"
#include "EdgeOTA/Response/OTABaseResponse.hpp"
#include "EdgeOTA/Response/OTAResponse.hpp"

namespace EdgeOTA {

class OTAHelper {
public:
    static constexpr const char* DefaultRemoteVersion = "初始版本";
    static constexpr const char* FirmwareType_Backend = "backend";
    static constexpr const char* FirmwareType_Frontend = "frontend";

    static std::filesystem::path getVersionFilePath();
    static std::filesystem::path getVersionDownloadDir();
    static std::filesystem::path getExtractDir();

    static std::tuple<Entity::OTAConfig*, std::vector<Entity::OTAConfig>> getOTAConfig(const std::string& uid,
                                                                                        const std::string& did,
                                                                                        const std::string& firmwareType);

    static Response::OTABaseResponse<std::string> getRemoteVersionInfo(const std::string& baseUrl,
                                                                       const std::string& api,
                                                                       const Request::GetRemoteVersionInfoRequest& req,
                                                                       const std::string& token = "");

    static Response::OTABaseResponse<Response::CheckForUpdateResponse> checkForUpdate(const Request::CheckForUpdateRequest& req);

    static Response::OTABaseResponse<std::string> downloadUpdate(const Request::DownloadUpdateRequest& req,
                                                                 const std::string& token = "");

    static std::filesystem::path getVersionFilePath_E();
    static std::filesystem::path getWWWRootDir();
    static std::filesystem::path getVersionDownloadDir_E();
    static std::tuple<Entity::OTAEdgeConfig*, std::vector<Entity::OTAEdgeConfig>> getOTAEdgeConfig(const std::string& uid,
                                                                                                    const std::string& did,
                                                                                                    const std::string& firmwareType);
    static Response::OTABaseResponse<Response::OTAResponse> getOTAInfoForEdgeRequest(const std::string& uid,
                                                                                      const std::string& did,
                                                                                      const std::string& firmwareType);
    static Response::OTABaseResponse<std::string> getRemoteVersionInfoForEdge(const std::string& baseUrl,
                                                                              const std::string& api,
                                                                              const Request::GetRemoteVersionInfoRequest& req,
                                                                              const std::string& token = "");
};

} // namespace EdgeOTA
