#pragma once
#include <filesystem>
#include <mutex>
#include <string>

namespace EdgeOTA {

class Logger {
public:
    explicit Logger(std::filesystem::path baseDir = std::filesystem::current_path());
    void log(const std::string& message);
    const std::filesystem::path& logFilePath() const noexcept { return logFilePath_; }
private:
    std::filesystem::path logFilePath_;
    std::mutex mutex_;
};

} // namespace EdgeOTA
