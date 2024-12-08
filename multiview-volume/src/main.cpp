/**
 * @file main.cpp
 * @brief Multi-view depth map processor that converts multiple view images into volumetric data
 * @author a-sumo
 * @date 12-09-2024
 *
 * This program processes depth maps from six different views (nx, ny, nz, px, py, pz)
 * and combines them into a single volumetric dataset using OpenVDB.
 */

#define STB_IMAGE_IMPLEMENTATION
#include "stb_image.h"

#include <openvdb/openvdb.h>
#include <openvdb/math/Transform.h>
#include <openvdb/math/Mat4.h>
#include <iostream>
#include <cmath>
#include <string>
#include <iomanip>
#include <sstream>
#include <vector>
#include <array>
#include <filesystem>
#include <cstring>

/**
 * @struct VoxelData
 * @brief Represents a single voxel's position and color data
 */
struct VoxelData
{
    int x, y, z;          ///< Grid coordinates
    openvdb::Vec3f color; ///< RGB color values
    float alpha;          ///< Alpha/transparency value
};

/**
 * @struct ProgramOptions
 * @brief Configuration options for the program
 */
struct ProgramOptions
{
    int startFrame = 1;
    int endFrame = 25;
    std::string baseDir = "../textures/viewdepthmaps/";
    std::string outputDir = "../output/";
    std::string outputPrefix = "volume";
    int textureSize = 128;
    bool verbose = false;
};

/**
 * @brief Parses command line arguments into program options
 * @param argc Argument count
 * @param argv Argument values
 * @return ProgramOptions structure with parsed values
 */
ProgramOptions parseCommandLine(int argc, char *argv[])
{
    ProgramOptions options;

    for (int i = 1; i < argc; i++)
    {
        if (strcmp(argv[i], "--start") == 0 && i + 1 < argc)
        {
            options.startFrame = std::stoi(argv[++i]);
        }
        else if (strcmp(argv[i], "--end") == 0 && i + 1 < argc)
        {
            options.endFrame = std::stoi(argv[++i]);
        }
        else if (strcmp(argv[i], "--dir") == 0 && i + 1 < argc)
        {
            options.baseDir = argv[++i];
        }
        else if (strcmp(argv[i], "--outdir") == 0 && i + 1 < argc)
        {
            options.outputDir = argv[++i];
        }
        else if (strcmp(argv[i], "--prefix") == 0 && i + 1 < argc)
        {
            options.outputPrefix = argv[++i];
        }
        else if (strcmp(argv[i], "--size") == 0 && i + 1 < argc)
        {
            options.textureSize = std::stoi(argv[++i]);
        }
        else if (strcmp(argv[i], "--verbose") == 0)
        {
            options.verbose = true;
        }
        else if (strcmp(argv[i], "--help") == 0)
        {
            std::cout << "Usage: " << argv[0] << " [options]\n"
                      << "Options:\n"
                      << "  --start N        Start frame number (default: 1)\n"
                      << "  --end N          End frame number (default: 25)\n"
                      << "  --dir path       Base directory for textures\n"
                      << "  --outdir path    Output directory for VDB files\n"
                      << "  --prefix name    Prefix for output files (default: volume)\n"
                      << "  --size N         Texture size (default: 128)\n"
                      << "  --verbose        Enable verbose output\n"
                      << "  --help           Show this help message\n";
            exit(0);
        }
    }

    return options;
}

/**
 * @brief Process a view image and map texture coordinates to grid index coordinates
 * @param filename Path to the image file
 * @param voxelDataList Vector to store the processed voxel data
 * @param viewIndex Index indicating the view direction (0-5)
 * @param textureSize Size of the texture (assumed square)
 * @param verbose Enable verbose logging
 */
void processView(const std::string &filename,
                 std::vector<VoxelData> &voxelDataList,
                 int viewIndex,
                 int textureSize,
                 bool verbose)
{
    if (verbose)
    {
        std::cout << "Processing view: " << filename << std::endl;
    }

    int width, height, channels;
    unsigned char *img = stbi_load(filename.c_str(), &width, &height, &channels, 0);

    if (img == nullptr)
    {
        std::cerr << "Error in loading the image: " << filename << std::endl;
        return;
    }

    if (verbose)
    {
        std::cout << "Image loaded successfully: "
                  << width << "x" << height
                  << " with " << channels << " channels" << std::endl;
    }

    const float depthThreshold = 0.05f;

    int processedVoxels = 0;
    int skippedVoxels = 0;

    for (int y = 0; y < height; y++)
    {
        for (int z = 0; z < width; z++)
        {
            float r = img[(z * width * channels) + (y * channels)] / 255.0f;
            float g = img[(z * width * channels) + (y * channels) + 1] / 255.0f;
            float b = img[(z * width * channels) + (y * channels) + 2] / 255.0f;
            float a = img[(z * width * channels) + (y * channels) + 3] / 255.0f;

            float depth = 1.0f - a;
            int x = static_cast<int>(std::round(depth * (textureSize - 1)));

            if (depth < depthThreshold || depth > (1.0f - depthThreshold))
            {
                skippedVoxels++;
                continue;
            }

            VoxelData voxel;
            voxel.color = openvdb::Vec3f(r, g, b);
            voxel.alpha = 1.0;

            // Calculate coordinates based on view axis and up vector
            switch (viewIndex)
            {
            case 0: // NX
                voxel.x = textureSize - 1 - x;
                voxel.y = y;
                voxel.z = z;
                break;
            case 1: // NY
                voxel.x = textureSize - 1 - z;
                voxel.y = textureSize - 1 - y;
                voxel.z = x;
                break;
            case 2: // NZ
                voxel.x = textureSize - 1 - y;
                voxel.y = textureSize - 1 - x;
                voxel.z = z;
                break;
            case 3: // PX
                voxel.x = x;
                voxel.y = textureSize - 1 - y;
                voxel.z = z;
                break;
            case 4: // PY
                voxel.x = textureSize - 1 - z;
                voxel.y = y;
                voxel.z = textureSize - 1 - x;
                break;
            case 5: // PZ
                voxel.x = y;
                voxel.y = x;
                voxel.z = z;
                break;
            }

            voxelDataList.push_back(voxel);
            processedVoxels++;
        }
    }

    if (verbose)
    {
        std::cout << "View processing complete: " << std::endl
                  << "  - Processed voxels: " << processedVoxels << std::endl
                  << "  - Skipped voxels: " << skippedVoxels << std::endl;
    }

    stbi_image_free(img);
}

void combineVoxels(openvdb::Vec3fGrid::Ptr rgbGrid, openvdb::FloatGrid::Ptr alphaGrid, const std::vector<VoxelData> &voxelDataList, int textureSize)
{
    for (const auto &voxel : voxelDataList)
    {
        if (voxel.x >= 0 && voxel.x < textureSize &&
            voxel.y >= 0 && voxel.y < textureSize &&
            voxel.z >= 0 && voxel.z < textureSize)
        {
            openvdb::Coord coord(voxel.x, voxel.y, voxel.z);
            openvdb::Vec3f existingColor = rgbGrid->tree().getValue(coord);
            float existingAlpha = alphaGrid->tree().getValue(coord);

            if (existingAlpha == 0.0f)
            {
                rgbGrid->tree().setValue(coord, voxel.color);
                alphaGrid->tree().setValue(coord, voxel.alpha);
            }
            else
            {
                float totalAlpha = existingAlpha + voxel.alpha;
                openvdb::Vec3f combinedColor(
                    (existingColor[0] * existingAlpha + voxel.color[0] * voxel.alpha) / totalAlpha,
                    (existingColor[1] * existingAlpha + voxel.color[1] * voxel.alpha) / totalAlpha,
                    (existingColor[2] * existingAlpha + voxel.color[2] * voxel.alpha) / totalAlpha);
                rgbGrid->tree().setValue(coord, combinedColor);
                alphaGrid->tree().setValue(coord, totalAlpha);
            }
        }
    }
}

/**
 * @brief Main program entry point
 */
int main(int argc, char *argv[])
{
    // Initialize OpenVDB
    openvdb::initialize();

    // Parse command line arguments
    ProgramOptions options = parseCommandLine(argc, argv);

    // Validate input directory
    if (!std::filesystem::exists(options.baseDir))
    {
        std::cerr << "Error: Input directory does not exist: " << options.baseDir << std::endl;
        return 1;
    }

    // Process frames
    for (int frame = options.startFrame; frame <= options.endFrame; ++frame)
    {
        if (options.verbose)
        {
            std::cout << "Processing frame " << frame << "..." << std::endl;
        }

        std::vector<VoxelData> voxelDataList;

        // Process all six views
        const std::array<std::string, 6> viewSuffixes = {
            "nx.png", "ny.png", "nz.png", "px.png", "py.png", "pz.png"};

        for (int viewIndex = 0; viewIndex < viewSuffixes.size(); ++viewIndex)
        {
            std::ostringstream oss;
            oss << options.baseDir << std::setw(4) << std::setfill('0')
                << frame << viewSuffixes[viewIndex];
            std::string filename = oss.str();

            processView(filename, voxelDataList, viewIndex,
                        options.textureSize, options.verbose);
        }

        // Create and initialize OpenVDB grids
        auto rgbGrid = openvdb::Vec3fGrid::create();
        rgbGrid->setName("RGB");

        auto alphaGrid = openvdb::FloatGrid::create();
        alphaGrid->setName("Alpha");

        // Process voxel data
        combineVoxels(rgbGrid, alphaGrid, voxelDataList, options.textureSize);

        // Apply transformations
        auto transform = rgbGrid->transformPtr();
        transform->postRotate(M_PI / 2, openvdb::math::X_AXIS);
        rgbGrid->setTransform(transform);

        transform = alphaGrid->transformPtr();
        transform->postRotate(M_PI / 2, openvdb::math::X_AXIS);
        alphaGrid->setTransform(transform);

        // Save output
        std::ostringstream vdbOss;
        vdbOss << options.outputDir << "/"
               << options.outputPrefix << "_"
               << std::setw(4) << std::setfill('0') << frame << ".vdb";
        std::string outputPath = vdbOss.str();

        // Check if file exists
        if (std::filesystem::exists(outputPath) && options.verbose)
        {
            std::cout << "Overwriting existing file: " << outputPath << std::endl;
        }

        // Save the file 
        openvdb::io::File file(outputPath);
        file.write({rgbGrid, alphaGrid});

        if (options.verbose)
        {
            std::cout << "Saved " << outputPath << std::endl;
        }

        if (options.verbose)
        {
            std::cout << "Saved " << vdbOss.str() << std::endl;
        }
    }

    return 0;
}