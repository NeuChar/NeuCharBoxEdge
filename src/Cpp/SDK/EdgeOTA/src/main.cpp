#include <algorithm>
#include "EdgeOTA/Logger.hpp"
#include "EdgeOTA/OTAHelper.hpp"
#include "EdgeOTA/ProgramOptions.hpp"
#include "EdgeOTA/Utils/FileUtils.hpp"
#include "EdgeOTA/Utils/JsonUtils.hpp"
#include "EdgeOTA/Utils/ProcessUtils.hpp"
#include <chrono>
#include <filesystem>
#include <iostream>
#include <thread>

using namespace EdgeOTA;

int main(int argc, char* argv[]) {
    Logger logger(std::filesystem::current_path());
    logger.log("=============================================================");
    logger.log("更新文件程序启动");

    std::vector<std::string> args;
    for (int i = 1; i < argc; ++i) args.emplace_back(argv[i]);
    std::string error;
    auto parsed = parseProgramOptions(args, error);
    if (!parsed) {
        std::cerr << error << std::endl;
        logger.log(error);
        return 1;
    }
    auto opt = *parsed;

    if (opt.firmwareType == OTAHelper::FirmwareType_Frontend && opt.frontPath.empty()) {
        std::cerr << "前端固件类型必须提供 -frontpath" << std::endl;
        logger.log("前端固件类型，但未提供前端路径参数");
        return 1;
    }

    if (opt.firmwareType == OTAHelper::FirmwareType_Backend) {
        bool found = false;
        if (opt.byPid && opt.processId > 0) {
            auto p = Utils::findProcessByPid(opt.processId);
            if (p) {
                if (opt.entryAssemblyName.empty()) opt.dllFileName = p->name + ".dll";
                found = Utils::killProcess(p->pid);
                logger.log("已终止进程ID: " + std::to_string(p->pid) + ", 进程名: " + p->name);
            }
        } else if (opt.byName) {
            auto ps = Utils::findProcessesByName(opt.processNameOrDll);
            for (const auto& p : ps) {
                found = Utils::killProcess(p.pid) || found;
                logger.log("已终止进程: " + p.name);
            }
        } else {
            auto ps = Utils::findProcessesByDllSuffix(opt.dllFileName);
            for (const auto& p : ps) {
                found = Utils::killProcess(p.pid) || found;
                logger.log("已终止进程: " + p.name);
            }
        }
        if (!found) {
            std::cerr << "未找到匹配进程" << std::endl;
            logger.log("未找到匹配进程");
            return 1;
        }
        std::this_thread::sleep_for(std::chrono::seconds(5));
    }

    auto configs = Utils::loadOTAConfigs(OTAHelper::getVersionFilePath());
    auto it = std::find_if(configs.begin(), configs.end(), [&](const auto& c) {
        return c.uid == opt.uid && c.did == opt.did && c.firmwareType == opt.firmwareType;
    });
    if (it == configs.end()) {
        std::cerr << "未找到OTA配置信息" << std::endl;
        logger.log("未找到OTA配置信息");
        return 1;
    }

    auto extractPath = OTAHelper::getExtractDir();
    auto baseDir = (opt.firmwareType == OTAHelper::FirmwareType_Frontend) ? std::filesystem::path(opt.frontPath) : std::filesystem::current_path();
    logger.log("开始将文件从 " + extractPath.string() + " 复制到 " + baseDir.string());
    try {
        Utils::copyDirectoryContents(extractPath, baseDir);
        logger.log("文件复制完成");
    } catch (const std::exception& ex) {
        std::cerr << ex.what() << std::endl;
        logger.log(std::string("复制文件时发生错误: ") + ex.what());
        return 1;
    }

    it->currentVersion = it->remoteVersion;
    Utils::saveOTAConfigs(OTAHelper::getVersionFilePath(), configs);

    if (opt.firmwareType == OTAHelper::FirmwareType_Backend) {
        if (Utils::restartDotnetDll(opt.dllFileName)) {
            logger.log("已重启进程: " + opt.dllFileName);
            std::cout << "已重启进程: " << opt.dllFileName << std::endl;
        } else {
            logger.log("重启进程失败: " + opt.dllFileName);
            std::cerr << "重启进程失败: " << opt.dllFileName << std::endl;
            return 1;
        }
    }

    logger.log("程序结束");
    logger.log("=============================================================");
    return 0;
}
