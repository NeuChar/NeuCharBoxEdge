# NeuCharBoxEdge C++ Port

This is a compileable C++17 port of the original `SDK/EdgeOTA` project structure.

## What is fully implemented

- Multi-file C++ project layout aligned with the original `SDK/EdgeOTA`
- OTA metadata persistence using JSON files (`json-c`)
- Remote version query and package download using `libcurl`
- ZIP extraction using the system `unzip` command
- Process discovery/kill/restart on Linux/macOS-style systems using `ps`, `kill`, and `dotnet`
- Backend and frontend file replacement flow
- Example targets `EdgeLamp`, `EdgeLed`, and `EdgeTube` that compile and run

## Build

```bash
cd SDK
cmake -S . -B build
cmake --build build -j
```

Build examples only without the `EdgeOTA` dependency stack:

```bash
cmake -S . -B build -DNEUCHARBOXEDGE_BUILD_EDGEOTA=OFF
cmake --build build --target EdgeTube
```

## Notes

- This port targets Unix-like environments first because the original project was frequently used on Linux edge devices.
- The OTA executable expects the same style of parameters as the original C# tool.
- `EdgeTube` is a functional C++ port of the TM1637 display logic from the C# and ESP32 samples, but it still uses in-memory segment state instead of real GPIO writes.
- The example apps are compileable examples, not ASP.NET feature-equivalent rewrites.
- ZIP extraction depends on `unzip` being installed.
