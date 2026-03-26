#pragma once
#include <string>

namespace EdgeOTA::Response {

struct CheckForUpdateResponse {
    bool isNeedUpdate{false};
    std::string currentVersion;
    std::string remoteVersion;
};

} // namespace EdgeOTA::Response
