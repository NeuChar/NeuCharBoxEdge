#pragma once
#include <string>

namespace EdgeOTA::Entity {

struct OTAEdgeConfig {
    std::string firmwareType;
    std::string did;
    std::string uid;
    std::string remoteVersion;
    std::string remoteFilePath;
};

} // namespace EdgeOTA::Entity
