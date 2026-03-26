#include "EdgeOTA/Utils/HttpUtils.hpp"
#include <curl/curl.h>
#include <memory>
#include <sstream>
#include <stdexcept>

namespace EdgeOTA::Utils {
namespace {
size_t writeStringCallback(void* contents, size_t size, size_t nmemb, void* userp) {
    auto* str = static_cast<std::string*>(userp);
    str->append(static_cast<char*>(contents), size * nmemb);
    return size * nmemb;
}
size_t writeBytesCallback(void* contents, size_t size, size_t nmemb, void* userp) {
    auto* bytes = static_cast<std::vector<unsigned char>*>(userp);
    auto total = size * nmemb;
    auto* c = static_cast<unsigned char*>(contents);
    bytes->insert(bytes->end(), c, c + total);
    return total;
}
std::string urlEncode(CURL* curl, const std::string& in) {
    char* encoded = curl_easy_escape(curl, in.c_str(), static_cast<int>(in.size()));
    if (!encoded) return {};
    std::string out(encoded);
    curl_free(encoded);
    return out;
}
}

std::string buildQueryString(const std::map<std::string, std::string>& params) {
    CURL* curl = curl_easy_init();
    if (!curl) throw std::runtime_error("curl 初始化失败");
    std::ostringstream oss;
    bool first = true;
    for (const auto& [k, v] : params) {
        if (!first) oss << '&';
        first = false;
        oss << urlEncode(curl, k) << '=' << urlEncode(curl, v);
    }
    curl_easy_cleanup(curl);
    return oss.str();
}

HttpResponse httpGet(const std::string& url, const std::map<std::string, std::string>& headers) {
    CURL* curl = curl_easy_init();
    if (!curl) throw std::runtime_error("curl 初始化失败");
    HttpResponse response;
    struct curl_slist* headerList = nullptr;
    for (const auto& [k, v] : headers) {
        headerList = curl_slist_append(headerList, (k + ": " + v).c_str());
    }
    curl_easy_setopt(curl, CURLOPT_URL, url.c_str());
    curl_easy_setopt(curl, CURLOPT_FOLLOWLOCATION, 1L);
    curl_easy_setopt(curl, CURLOPT_TIMEOUT, 600L);
    curl_easy_setopt(curl, CURLOPT_WRITEFUNCTION, writeStringCallback);
    curl_easy_setopt(curl, CURLOPT_WRITEDATA, &response.body);
    curl_easy_setopt(curl, CURLOPT_USERAGENT, "EdgeOTA-Cpp/1.0");
    if (headerList) curl_easy_setopt(curl, CURLOPT_HTTPHEADER, headerList);
    auto code = curl_easy_perform(curl);
    if (code != CURLE_OK) {
        std::string msg = curl_easy_strerror(code);
        curl_slist_free_all(headerList);
        curl_easy_cleanup(curl);
        throw std::runtime_error("HTTP请求失败: " + msg);
    }
    curl_easy_getinfo(curl, CURLINFO_RESPONSE_CODE, &response.statusCode);
    curl_slist_free_all(headerList);
    curl_easy_cleanup(curl);
    return response;
}

} // namespace EdgeOTA::Utils
