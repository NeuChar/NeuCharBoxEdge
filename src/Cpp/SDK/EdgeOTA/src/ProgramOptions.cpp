#include "EdgeOTA/ProgramOptions.hpp"
#include <sstream>

namespace EdgeOTA {

std::string usageText() {
    return "用法: EdgeOTA <DLL文件名或进程名> -did <设备ID> -uid <用户ID> -firmwareType <backend|frontend> [-frontpath <前端路径>] [-n <进程名> | -pid <进程ID>]";
}

std::optional<ProgramOptions> parseProgramOptions(const std::vector<std::string>& args, std::string& errorMessage) {
    if (args.empty()) {
        errorMessage = "请提供要终止的DLL文件名或进程名";
        return std::nullopt;
    }

    ProgramOptions opt;
    opt.processNameOrDll = args.front();

    for (std::size_t i = 1; i < args.size(); ++i) {
        const auto& arg = args[i];
        auto next = [&](std::string_view flag) -> std::optional<std::string> {
            if (i + 1 >= args.size()) {
                errorMessage = std::string(flag) + " 缺少参数值";
                return std::nullopt;
            }
            return args[++i];
        };

        if (arg == "-n") {
            auto value = next("-n");
            if (!value) return std::nullopt;
            opt.byName = true;
            opt.processNameOrDll = *value;
            if (value->size() >= 4 && value->substr(value->size() - 4) == ".dll") {
                opt.entryAssemblyName = *value;
                opt.dllFileName = *value;
            } else {
                opt.dllFileName = *value + ".dll";
            }
        } else if (arg == "-pid") {
            auto value = next("-pid");
            if (!value) return std::nullopt;
            opt.byPid = true;
            try { opt.processId = std::stoi(*value); }
            catch (...) { errorMessage = "无效的进程ID: " + *value; return std::nullopt; }
        } else if (arg == "-did") {
            auto value = next("-did"); if (!value) return std::nullopt; opt.did = *value;
        } else if (arg == "-uid") {
            auto value = next("-uid"); if (!value) return std::nullopt; opt.uid = *value;
        } else if (arg == "-firmwareType") {
            auto value = next("-firmwareType"); if (!value) return std::nullopt; opt.firmwareType = *value;
        } else if (arg == "-frontpath") {
            auto value = next("-frontpath"); if (!value) return std::nullopt; opt.frontPath = *value;
        }
    }

    if (opt.did.empty() || opt.uid.empty() || opt.firmwareType.empty()) {
        errorMessage = "错误: 必须提供 DID、UID 和 FirmwareType 参数\n" + usageText();
        return std::nullopt;
    }

    if (!opt.byName && !opt.byPid) {
        opt.dllFileName = opt.processNameOrDll;
        if (opt.dllFileName.size() < 4 || opt.dllFileName.substr(opt.dllFileName.size() - 4) != ".dll") {
            opt.dllFileName += ".dll";
        }
    }
    return opt;
}

} // namespace EdgeOTA
