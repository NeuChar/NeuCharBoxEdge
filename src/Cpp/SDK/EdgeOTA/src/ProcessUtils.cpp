#include "EdgeOTA/Utils/ProcessUtils.hpp"
#include <array>
#include <csignal>
#include <cstdio>
#include <cstdlib>
#include <filesystem>
#include <sstream>
#include <stdexcept>
#include <string>
#include <sys/types.h>
#include <unistd.h>

namespace EdgeOTA::Utils {
namespace {
std::string execRead(const char* cmd) {
    std::array<char, 512> buf{};
    std::string out;
    FILE* pipe = popen(cmd, "r");
    if (!pipe) return out;
    while (fgets(buf.data(), static_cast<int>(buf.size()), pipe)) out += buf.data();
    pclose(pipe);
    return out;
}
std::string trim(std::string s) {
    while (!s.empty() && (s.back()=='\n' || s.back()=='\r' || s.back()==' ' || s.back()=='\t')) s.pop_back();
    std::size_t i=0; while (i<s.size() && (s[i]==' ' || s[i]=='\t')) ++i; return s.substr(i);
}
}

std::vector<ProcessInfo> listProcesses() {
    std::vector<ProcessInfo> list;
    std::istringstream iss(execRead("ps -eo pid=,comm=,args="));
    std::string line;
    while (std::getline(iss, line)) {
        std::istringstream ls(line);
        ProcessInfo p;
        if (!(ls >> p.pid >> p.name)) continue;
        std::getline(ls, p.command);
        p.command = trim(p.command);
        list.push_back(std::move(p));
    }
    return list;
}

std::optional<ProcessInfo> findProcessByPid(int pid) {
    for (const auto& p : listProcesses()) if (p.pid == pid) return p;
    return std::nullopt;
}

std::vector<ProcessInfo> findProcessesByName(const std::string& name) {
    std::vector<ProcessInfo> out;
    for (const auto& p : listProcesses()) if (p.name == name) out.push_back(p);
    return out;
}

std::vector<ProcessInfo> findProcessesByDllSuffix(const std::string& dllSuffix) {
    std::vector<ProcessInfo> out;
    for (const auto& p : listProcesses()) if (p.command.find(dllSuffix) != std::string::npos) out.push_back(p);
    return out;
}

bool killProcess(int pid) {
    return ::kill(static_cast<pid_t>(pid), SIGKILL) == 0;
}

bool restartDotnetDll(const std::string& dllFileName) {
    std::string cmd = "nohup dotnet '" + dllFileName + "' >/tmp/edgeota_restart.log 2>&1 &";
    return std::system(cmd.c_str()) == 0;
}

} // namespace EdgeOTA::Utils
