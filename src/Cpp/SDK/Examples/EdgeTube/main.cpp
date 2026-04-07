#include <array>
#include <cmath>
#include <cstdint>
#include <cstdlib>
#include <iomanip>
#include <iostream>
#include <sstream>
#include <stdexcept>
#include <string>

struct RegisterInfo {
    const char* name = "数字管";
    const char* uid = "XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX";
    const char* version = "1.0.1";
};

static int clampInt(int value, int minValue, int maxValue) {
    if (value < minValue) {
        return minValue;
    }
    if (value > maxValue) {
        return maxValue;
    }
    return value;
}

class TubeService {
public:
    static constexpr std::size_t kDigits = 4;
    static constexpr uint8_t kMinusSegment = 0x40;
    static constexpr uint8_t kBlankSegment = 0x00;

    TubeService() {
        clear();
    }

    void displayNumber(int number) {
        number = clampInt(number, -999, 9999);

        std::array<uint8_t, kDigits> segments{};
        const bool negative = number < 0;
        number = std::abs(number);

        const std::string digits = std::to_string(number);
        const int startPos = (negative && digits.size() < kDigits)
            ? static_cast<int>(kDigits - digits.size() - 1)
            : static_cast<int>(kDigits - digits.size());

        if (negative && digits.size() < kDigits) {
            segments[static_cast<std::size_t>(startPos)] = kMinusSegment;
            for (std::size_t i = 0; i < digits.size(); ++i) {
                segments[static_cast<std::size_t>(startPos + 1 + static_cast<int>(i))] =
                    digitToSegment(digits[i]);
            }
        } else {
            for (std::size_t i = 0; i < digits.size(); ++i) {
                segments[static_cast<std::size_t>(startPos + static_cast<int>(i))] =
                    digitToSegment(digits[i]);
            }
        }

        writeSegments(segments, false);
    }

    void show(const std::string& text) {
        std::array<uint8_t, kDigits> segments{};
        for (std::size_t i = 0; i < std::min(text.size(), kDigits); ++i) {
            segments[i] = charToSegment(text[i]);
        }
        writeSegments(segments, false);
    }

    void showHex(int value) {
        if (value < 0 || value > 0xFFFF) {
            throw std::out_of_range("hex value must be between 0 and 0xFFFF");
        }

        std::array<uint8_t, kDigits> segments{};
        for (int i = static_cast<int>(kDigits) - 1; i >= 0; --i) {
            segments[static_cast<std::size_t>(i)] = kDigitToSegment[static_cast<std::size_t>(value & 0x0F)];
            value >>= 4;
        }
        writeSegments(segments, false);
    }

    void showTime(int hours, int minutes) {
        if (hours < 0 || hours > 23 || minutes < 0 || minutes > 59) {
            throw std::out_of_range("time must be in 24-hour format");
        }

        std::array<uint8_t, kDigits> segments{
            kDigitToSegment[static_cast<std::size_t>(hours / 10)],
            kDigitToSegment[static_cast<std::size_t>(hours % 10)],
            kDigitToSegment[static_cast<std::size_t>(minutes / 10)],
            kDigitToSegment[static_cast<std::size_t>(minutes % 10)]
        };
        writeSegments(segments, true);
    }

    void displayTemperature(double temperature) {
        int temp = static_cast<int>(std::round(temperature * 10.0));
        if (temp < -999 || temp > 999) {
            throw std::out_of_range("temperature must be between -99.9 and 99.9");
        }

        std::array<uint8_t, kDigits> segments{};
        const bool negative = temp < 0;
        temp = std::abs(temp);

        segments[3] = kDigitToSegment[static_cast<std::size_t>(temp % 10)];
        temp /= 10;
        segments[2] = static_cast<uint8_t>(kDigitToSegment[static_cast<std::size_t>(temp % 10)] | 0x80);
        temp /= 10;

        if (negative) {
            segments[1] = kDigitToSegment[static_cast<std::size_t>(temp % 10)];
            segments[0] = kMinusSegment;
        } else {
            segments[1] = temp > 0 ? kDigitToSegment[static_cast<std::size_t>(temp % 10)] : kBlankSegment;
            segments[0] = temp > 9
                ? kDigitToSegment[static_cast<std::size_t>(temp / 10)]
                : kDigitToSegment[static_cast<std::size_t>(temp)];
        }

        writeSegments(segments, false);
    }

    void clear() {
        writeSegments({}, false);
    }

    std::string getCurrentDisplay() const {
        return currentDisplay_;
    }

    std::array<uint8_t, kDigits> getCurrentSegments() const {
        return currentSegments_;
    }

    bool isColonEnabled() const {
        return colonEnabled_;
    }

private:
    static constexpr std::array<uint8_t, 16> kDigitToSegment{
        0x3f, 0x06, 0x5b, 0x4f,
        0x66, 0x6d, 0x7d, 0x07,
        0x7f, 0x6f, 0x77, 0x7c,
        0x39, 0x5e, 0x79, 0x71
    };

    std::array<uint8_t, kDigits> currentSegments_{};
    std::string currentDisplay_ = std::string(kDigits, ' ');
    bool colonEnabled_ = false;

    static uint8_t digitToSegment(char c) {
        if (c < '0' || c > '9') {
            throw std::invalid_argument("expected a numeric digit");
        }
        return kDigitToSegment[static_cast<std::size_t>(c - '0')];
    }

    static uint8_t charToSegment(char c) {
        if (c >= '0' && c <= '9') {
            return kDigitToSegment[static_cast<std::size_t>(c - '0')];
        }
        if (c >= 'A' && c <= 'F') {
            return kDigitToSegment[static_cast<std::size_t>(10 + c - 'A')];
        }
        if (c >= 'a' && c <= 'f') {
            return kDigitToSegment[static_cast<std::size_t>(10 + c - 'a')];
        }
        if (c == '-') {
            return kMinusSegment;
        }
        return kBlankSegment;
    }

    static char segmentToChar(uint8_t segment) {
        const uint8_t normalized = static_cast<uint8_t>(segment & 0x7F);
        for (std::size_t i = 0; i < kDigitToSegment.size(); ++i) {
            if (kDigitToSegment[i] == normalized) {
                return i < 10
                    ? static_cast<char>('0' + static_cast<int>(i))
                    : static_cast<char>('A' + static_cast<int>(i - 10));
            }
        }
        if (normalized == kMinusSegment) {
            return '-';
        }
        if (normalized == kBlankSegment) {
            return ' ';
        }
        return '?';
    }

    void writeSegments(const std::array<uint8_t, kDigits>& segments, bool showColon) {
        currentSegments_ = segments;
        colonEnabled_ = showColon;
        currentDisplay_.clear();
        currentDisplay_.reserve(kDigits);
        for (uint8_t segment : currentSegments_) {
            currentDisplay_.push_back(segmentToChar(segment));
        }
    }
};

class EdgeTubeTool {
public:
    explicit EdgeTubeTool(TubeService& service) : service_(service) {}

    std::string displayNumber(const std::string& number) {
        const int value = std::stoi(number);
        service_.displayNumber(value);
        return std::to_string(clampInt(value, -999, 9999));
    }

    std::string clear() {
        service_.clear();
        return "OK";
    }

    std::string getCurrentDisplay() const {
        return service_.getCurrentDisplay();
    }

private:
    TubeService& service_;
};

static std::string formatSegments(const std::array<uint8_t, TubeService::kDigits>& segments) {
    std::ostringstream oss;
    for (std::size_t i = 0; i < segments.size(); ++i) {
        if (i != 0) {
            oss << ' ';
        }
        oss << "0x"
            << std::hex << std::setw(2) << std::setfill('0')
            << static_cast<int>(segments[i]);
    }
    return oss.str();
}

int main() {
    RegisterInfo info;
    TubeService tubeService;
    EdgeTubeTool tubeTool(tubeService);

    std::cout << "EdgeTube example running\n";
    std::cout << "Name: " << info.name << "\nUID: " << info.uid << "\nVersion: " << info.version << "\n\n";

    std::cout << "[Tool] DisplayNumber(\"-42\") => " << tubeTool.displayNumber("-42") << '\n';
    std::cout << "Current display: [" << tubeTool.getCurrentDisplay() << "]\n";
    std::cout << "Segments: " << formatSegments(tubeService.getCurrentSegments()) << "\n\n";

    tubeService.showHex(0x1AF3);
    std::cout << "[Service] ShowHex(0x1AF3)\n";
    std::cout << "Current display: [" << tubeService.getCurrentDisplay() << "]\n";
    std::cout << "Segments: " << formatSegments(tubeService.getCurrentSegments()) << "\n\n";

    tubeService.showTime(12, 34);
    std::cout << "[Service] ShowTime(12, 34)\n";
    std::cout << "Current display: [" << tubeService.getCurrentDisplay() << "]";
    std::cout << " colon=" << (tubeService.isColonEnabled() ? "on" : "off") << "\n\n";

    tubeService.displayTemperature(23.5);
    std::cout << "[Service] DisplayTemperature(23.5)\n";
    std::cout << "Current display: [" << tubeService.getCurrentDisplay() << "]\n";
    std::cout << "Segments: " << formatSegments(tubeService.getCurrentSegments()) << "\n\n";

    std::cout << "[Tool] Clear() => " << tubeTool.clear() << '\n';
    std::cout << "Current display: [" << tubeTool.getCurrentDisplay() << "]\n";

    return 0;
}
