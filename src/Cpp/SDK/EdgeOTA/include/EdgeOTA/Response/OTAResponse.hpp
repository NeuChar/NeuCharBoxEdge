#pragma once
#include <string>

namespace EdgeOTA::Response {

struct OTAResponse {
    std::string id;
    std::string devicePoolId;
    std::string operatingSystem;
    std::string developmentMode;
    std::string otaMode;
    std::string firmwareType;
    std::string firmwareVersion;
    std::string firmwarePackage;
    double firmwareSize{0.0};
    std::string updateDescription;
};

} // namespace EdgeOTA::Response
