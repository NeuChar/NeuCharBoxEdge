#include "EdgeOTA/Logger.hpp"
#include <chrono>
#include <fstream>
#include <iomanip>
#include <iostream>

namespace EdgeOTA {
namespace {
std::string nowString(const char* fmt) {
    auto now = std::chrono::system_clock::now();
    std::time_t t = std::chrono::system_clock::to_time_t(now);
    std::tm tm{};
    localtime_r(&t, &tm);
    std::ostringstream oss;
    oss << std::put_time(&tm, fmt);
    return oss.str();
}
}

Logger::Logger(std::filesystem::path baseDir) {
    auto logDir = baseDir / "OTALogs";
    std::filesystem::create_directories(logDir);
    logFilePath_ = logDir / ("OTA_Log_" + nowString("%Y-%m-%d") + ".txt");
    std::cout << "日志系统初始化完成，日志文件: " << logFilePath_ << std::endl;
}

void Logger::log(const std::string& message) {
    std::lock_guard<std::mutex> lock(mutex_);
    std::ofstream out(logFilePath_, std::ios::app);
    out << '[' << nowString("%Y-%m-%d %H:%M:%S") << "] " << message << '\n';
}

} // namespace EdgeOTA
