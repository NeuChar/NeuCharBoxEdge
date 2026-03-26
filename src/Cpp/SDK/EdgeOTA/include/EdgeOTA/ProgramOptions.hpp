#pragma once
#include <optional>
#include <string>
#include <vector>

namespace EdgeOTA {

struct ProgramOptions {
    std::string processNameOrDll;
    bool byName{false};
    bool byPid{false};
    int processId{-1};
    std::string dllFileName;
    std::string entryAssemblyName;
    std::string did;
    std::string uid;
    std::string firmwareType;
    std::string frontPath;
};

std::optional<ProgramOptions> parseProgramOptions(const std::vector<std::string>& args, std::string& errorMessage);
std::string usageText();

} // namespace EdgeOTA
