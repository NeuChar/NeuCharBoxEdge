cmake_minimum_required(VERSION 3.16)
project(NeuCharBoxEdgeCpp LANGUAGES CXX)

option(NEUCHARBOXEDGE_BUILD_EDGEOTA "Build the EdgeOTA target and its third-party dependencies" ON)

if(NEUCHARBOXEDGE_BUILD_EDGEOTA)
    add_subdirectory(EdgeOTA)
endif()

add_subdirectory(Examples/EdgeLamp)
add_subdirectory(Examples/EdgeLed)
add_subdirectory(Examples/EdgeTube)
