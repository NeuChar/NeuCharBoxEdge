#pragma once
#include <optional>
#include <string>
#include <vector>

namespace EdgeOTA::Utils {

struct ProcessInfo {
    int pid{-1};
    std::string name;
    std::string command;
};

std::vector<ProcessInfo> listProcesses();
std::optional<ProcessInfo> findProcessByPid(int pid);
std::vector<ProcessInfo> findProcessesByName(const std::string& name);
std::vector<ProcessInfo> findProcessesByDllSuffix(const std::string& dllSuffix);
bool killProcess(int pid);
bool restartDotnetDll(const std::string& dllFileName);

} // namespace EdgeOTA::Utils
