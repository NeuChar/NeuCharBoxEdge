#include <iostream>

struct RegisterInfo {
    const char* name = "数字管";
    const char* uid = "XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX";
    const char* version = "1.0.1";
};

int main() {
    RegisterInfo info;
    std::cout << "EdgeLed example running\n";
    std::cout << "Name: " << info.name << "\nUID: " << info.uid << "\nVersion: " << info.version << std::endl;
    return 0;
}
