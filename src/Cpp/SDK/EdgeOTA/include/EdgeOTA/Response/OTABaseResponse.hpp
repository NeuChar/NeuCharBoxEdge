#pragma once
#include <string>

namespace EdgeOTA::Response {

template <typename T>
struct OTABaseResponse {
    bool success{false};
    std::string message;
    T data{};

    OTABaseResponse() = default;
    OTABaseResponse(bool ok, std::string msg) : success(ok), message(std::move(msg)), data{} {}
    OTABaseResponse(bool ok, std::string msg, T value) : success(ok), message(std::move(msg)), data(std::move(value)) {}
};

} // namespace EdgeOTA::Response
