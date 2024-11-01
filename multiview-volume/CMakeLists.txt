cmake_minimum_required(VERSION 3.20)
project(multiview-volume VERSION 1.0)

# Set C++ standard
set(CMAKE_CXX_STANDARD 14)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Add OpenVDB CMake module path
list(APPEND CMAKE_MODULE_PATH "$ENV{HOME}/openvdb/lib/cmake/OpenVDB")

# Find OpenVDB
find_package(OpenVDB REQUIRED)

# Add the executable
add_executable(${PROJECT_NAME})

# Source files
file(GLOB SOURCES src/*.cpp)
target_sources(${PROJECT_NAME} PRIVATE ${SOURCES})

# Include directories
target_include_directories(${PROJECT_NAME} PRIVATE
    ${OpenVDB_INCLUDE_DIRS}
    ${CMAKE_CURRENT_SOURCE_DIR}/include
)

# Link against OpenVDB
target_link_libraries(${PROJECT_NAME} PRIVATE OpenVDB::openvdb)

# Enable warnings
if(MSVC)
    add_compile_options(/W4 /WX)
else()
    add_compile_options(-Wall -Wextra -pedantic -Werror)
endif()

# Output directory
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)

# Install rules
install(TARGETS ${PROJECT_NAME}
    DESTINATION bin
)

# Generate compile_commands.json for clang-tidy and other tools
set(CMAKE_EXPORT_COMPILE_COMMANDS ON)
