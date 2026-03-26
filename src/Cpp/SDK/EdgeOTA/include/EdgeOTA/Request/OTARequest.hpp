#pragma once
#include <string>

namespace EdgeOTA::Request {

struct OTARequest {
    std::string appKey;
    std::string appSecret;
    std::string uid;
    std::string did;
    std::string firmwareType;
};

struct GetRemoteVersionInfoRequest : OTARequest {};

struct CheckForUpdateRequest {
    std::string uid;
    std::string did;
    std::string firmwareType;
};

struct DownloadUpdateRequest : CheckForUpdateRequest {
    std::string baseUrl;
};

} // namespace EdgeOTA::Request
