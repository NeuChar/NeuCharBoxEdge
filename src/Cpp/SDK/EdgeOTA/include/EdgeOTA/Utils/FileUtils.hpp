#pragma once
#include <filesystem>
#include <string>

namespace EdgeOTA::Utils {

void ensureDirectory(const std::filesystem::path& dir);
void extractZip(const std::filesystem::path& zipFile, const std::filesystem::path& outputDir);
void copyDirectoryContents(const std::filesystem::path& src, const std::filesystem::path& dst, int retries = 3);

} // namespace EdgeOTA::Utils
