# NeuCharBoxEdge C++ Port

This is a compileable C++17 port of the original `SDK/EdgeOTA` project structure.

## What is fully implemented

- Multi-file C++ project layout aligned with the original `SDK/EdgeOTA`
- OTA metadata persistence using JSON files (`json-c`)
- Remote version query and package download using `libcurl`
- ZIP extraction using the system `unzip` command
- Process discovery/kill/restart on Linux/macOS-style systems using `ps`, `kill`, and `dotnet`
- Backend and frontend file replacement flow
- Example targets `EdgeLamp` and `EdgeLed` that compile and run

## Build

```bash
cd SDK
cmake -S . -B build
cmake --build build -j
```

## Notes

- This port targets Unix-like environments first because the original project was frequently used on Linux edge devices.
- The OTA executable expects the same style of parameters as the original C# tool.
- The example apps are compileable placeholders, not ASP.NET feature-equivalent rewrites.
- ZIP extraction depends on `unzip` being installed.
