#pragma once
#include <filesystem>
#include <optional>
#include <string>
#include <vector>
#include "EdgeOTA/Entity/OTAConfig.hpp"
#include "EdgeOTA/Entity/OTAEdgeConfig.hpp"
#include "EdgeOTA/Response/OTAResponse.hpp"

namespace EdgeOTA::Utils {

std::vector<Entity::OTAConfig> loadOTAConfigs(const std::filesystem::path& path);
void saveOTAConfigs(const std::filesystem::path& path, const std::vector<Entity::OTAConfig>& configs);
std::vector<Entity::OTAEdgeConfig> loadOTAEdgeConfigs(const std::filesystem::path& path);
void saveOTAEdgeConfigs(const std::filesystem::path& path, const std::vector<Entity::OTAEdgeConfig>& configs);
std::optional<Response::OTAResponse> parseOTAResponseData(const std::string& jsonText, bool& success, std::string& message);

} // namespace EdgeOTA::Utils
