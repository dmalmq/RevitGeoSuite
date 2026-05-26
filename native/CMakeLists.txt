cmake_minimum_required(VERSION 3.16)
project(draco_dec CXX)

set(CMAKE_CXX_STANDARD 14)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

# DRACO_ROOT must point at a checkout of https://github.com/google/draco
if(NOT DEFINED DRACO_ROOT)
    message(FATAL_ERROR "Set -DDRACO_ROOT=<path-to-google/draco-checkout> when invoking cmake.")
endif()

# Build draco's static library as a subdirectory.
set(DRACO_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(DRACO_TRANSCODER_SUPPORTED OFF CACHE BOOL "" FORCE)
add_subdirectory(${DRACO_ROOT} ${CMAKE_BINARY_DIR}/draco-build)

add_library(draco_dec SHARED draco_c_api.cpp)
target_link_libraries(draco_dec PRIVATE draco)
target_include_directories(draco_dec PRIVATE
    ${DRACO_ROOT}/src
    ${CMAKE_BINARY_DIR})
set_target_properties(draco_dec PROPERTIES
    OUTPUT_NAME "draco_dec"
    PREFIX ""
    WINDOWS_EXPORT_ALL_SYMBOLS OFF)
