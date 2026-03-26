#include "EdgeOTA/Utils/FileUtils.hpp"
#include <chrono>
#include <cstdlib>
#include <stdexcept>
#include <thread>

namespace EdgeOTA::Utils {

void ensureDirectory(const std::filesystem::path& dir) {
    std::filesystem::create_directories(dir);
}

void extractZip(const std::filesystem::path& zipFile, const std::filesystem::path& outputDir) {
    ensureDirectory(outputDir);
    std::string command = "unzip -o '" + zipFile.string() + "' -d '" + outputDir.string() + "' >/dev/null";
    int rc = std::system(command.c_str());
    if (rc != 0) throw std::runtime_error("解压失败: " + zipFile.string());
}

void copyDirectoryContents(const std::filesystem::path& src, const std::filesystem::path& dst, int retries) {
    if (!std::filesystem::exists(src)) throw std::runtime_error("源目录不存在: " + src.string());
    for (auto const& entry : std::filesystem::recursive_directory_iterator(src)) {
        if (!entry.is_regular_file()) continue;
        auto relative = std::filesystem::relative(entry.path(), src);
        auto target = dst / relative;
        ensureDirectory(target.parent_path());
        bool copied = false;
        for (int attempt = 0; attempt < retries && !copied; ++attempt) {
            try {
                std::filesystem::copy_file(entry.path(), target, std::filesystem::copy_options::overwrite_existing);
                copied = true;
            } catch (...) {
                std::this_thread::sleep_for(std::chrono::seconds(2));
            }
        }
        if (!copied) throw std::runtime_error("复制文件失败: " + relative.string());
    }
}

} // namespace EdgeOTA::Utils
