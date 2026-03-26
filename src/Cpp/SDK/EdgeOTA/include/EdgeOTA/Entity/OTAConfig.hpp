#pragma once
#include <string>

namespace EdgeOTA::Entity {

struct OTAConfig {
    std::string firmwareType;
    std::string did;
    std::string uid;
    std::string currentVersion;
    std::string remoteVersion;
    std::string remoteFilePath;
    std::string ignoreVersion;
    std::string filePath;
};

} // namespace EdgeOTA::Entity
