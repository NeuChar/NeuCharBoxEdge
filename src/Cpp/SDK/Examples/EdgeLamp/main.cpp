
#include <iostream>

struct RegisterInfo {
    const char* name = "LED灯";
    const char* uid = "475B9077-1A08-5682-4E60-0E4D0EC9BE45";
    const char* version = "1.0.1";
};

int main() {
    RegisterInfo info;
    std::cout << "EdgeLamp example running\n";
    std::cout << "Name: " << info.name << "\nUID: " << info.uid << "\nVersion: " << info.version << std::endl;
    return 0;
}
