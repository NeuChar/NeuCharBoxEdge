#pragma once
#include <map>
#include <string>
#include <vector>

namespace EdgeOTA::Utils {

struct HttpResponse {
    long statusCode{0};
    std::string body;
    std::vector<unsigned char> bytes;
};

HttpResponse httpGet(const std::string& url, const std::map<std::string, std::string>& headers = {});
std::string buildQueryString(const std::map<std::string, std::string>& params);

} // namespace EdgeOTA::Utils
