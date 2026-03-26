cmake_minimum_required(VERSION 3.16)
project(EdgeOTA LANGUAGES CXX)

find_package(PkgConfig REQUIRED)
pkg_check_modules(JSONC REQUIRED json-c)
pkg_check_modules(LIBCURL REQUIRED libcurl)

add_library(EdgeOTALib
    src/Logger.cpp
    src/ProgramOptions.cpp
    src/OTAHelper.cpp
    src/JsonUtils.cpp
    src/HttpUtils.cpp
    src/FileUtils.cpp
    src/ProcessUtils.cpp
)

target_include_directories(EdgeOTALib PUBLIC include ${JSONC_INCLUDE_DIRS} ${LIBCURL_INCLUDE_DIRS})
target_compile_features(EdgeOTALib PUBLIC cxx_std_17)
target_compile_options(EdgeOTALib PUBLIC ${JSONC_CFLAGS_OTHER} ${LIBCURL_CFLAGS_OTHER})
target_link_directories(EdgeOTALib PUBLIC ${JSONC_LIBRARY_DIRS} ${LIBCURL_LIBRARY_DIRS})
target_link_libraries(EdgeOTALib PUBLIC ${JSONC_LIBRARIES} ${LIBCURL_LIBRARIES})

add_executable(EdgeOTA src/main.cpp)
target_link_libraries(EdgeOTA PRIVATE EdgeOTALib)
