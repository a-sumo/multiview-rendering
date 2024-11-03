#define STB_IMAGE_IMPLEMENTATION
#include "stb_image.h"

#include <openvdb/openvdb.h>
#include <iostream>
#include <cmath>
#include <string>
#include <iomanip>
#include <sstream>
#include <vector>
#include <array>
#include <openvdb/math/Transform.h>
#include <openvdb/math/Mat4.h>

struct VoxelData
{
    int x, y, z;
    openvdb::Vec3f color;
    float alpha;
};
/**
 * Process a view image and map texture coordinates (u, v, w) to grid index coordinates (i, j, k).
 * 
 * Assumptions:
 * - Views nx, px, nz, pz have up vector +y
 * - Views ny, py have up vector +x
 */
void processView(const std::string &filename, std::vector<VoxelData> &voxelDataList, int viewIndex, int textureSize)
{
    int width, height, channels;
    unsigned char *img = stbi_load(filename.c_str(), &width, &height, &channels, 0);

    if (img == nullptr)
    {
        std::cerr << "Error in loading the image: " << filename << std::endl;
        return;
    }

    const float depthThreshold = 0.05f; // Define a threshold (e.g., 5% of textureSize)

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

            // Check if depth is close to zero or textureSize
            if (depth < depthThreshold || depth > (1.0f - depthThreshold))
            {
                continue; // Skip this pixel if it's too close to the edges
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
        }
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

int main()
{
    openvdb::initialize();

    const int startFrame = 1;
    const int endFrame = 25;
    const std::string baseDir = "../textures/viewdepthmaps/";
    const int textureSize = 128; // Adjust this to match your texture size

    for (int frame = startFrame; frame <= endFrame; ++frame)
    {
        std::vector<VoxelData> voxelDataList;

        for (int viewIndex = 0; viewIndex < 6; ++viewIndex)
        {
            std::string viewSuffix;
            switch (viewIndex)
            {
            case 0:
                viewSuffix = "nx.png";
                break;
            case 1:
                viewSuffix = "ny.png";
                break;
            case 2:
                viewSuffix = "nz.png";
                break;
            case 3:
                viewSuffix = "px.png";
                break;
            case 4:
                viewSuffix = "py.png";
                break;
            case 5:
                viewSuffix = "pz.png";
                break;
            }

            std::ostringstream oss;
            oss << baseDir << std::setw(4) << std::setfill('0') << frame << viewSuffix;
            std::string filename = oss.str();

            processView(filename, voxelDataList, viewIndex, textureSize);
        }

        // Create OpenVDB grids
        openvdb::Vec3fGrid::Ptr rgbGrid = openvdb::Vec3fGrid::create();
        rgbGrid->setName("RGB");

        openvdb::FloatGrid::Ptr alphaGrid = openvdb::FloatGrid::create();
        alphaGrid->setName("Alpha");

        // Combine voxel data
        combineVoxels(rgbGrid, alphaGrid, voxelDataList, textureSize);


        // Apply rotation using OpenVDB's Transform
        openvdb::math::Transform::Ptr transform = rgbGrid->transformPtr();
        transform->postRotate(M_PI / 2, openvdb::math::X_AXIS); // Rotate 90 degrees around X-axis
        rgbGrid->setTransform(transform);

        transform = alphaGrid->transformPtr();
        transform->postRotate(M_PI / 2, openvdb::math::X_AXIS); // Rotate 90 degrees around X-axis
        alphaGrid->setTransform(transform);
        
        // Save the OpenVDB grids
        std::ostringstream vdbOss;
        vdbOss << "output_" << std::setw(4) << std::setfill('0') << frame << ".vdb";
        openvdb::io::File(vdbOss.str()).write({rgbGrid, alphaGrid});

        std::cout << "Processed frame " << frame << " and saved as " << vdbOss.str() << std::endl;
    }

    return 0;
}
