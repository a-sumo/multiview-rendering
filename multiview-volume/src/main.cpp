#define STB_IMAGE_IMPLEMENTATION
#include "stb_image.h"

#include <openvdb/openvdb.h>
#include <iostream>
#include <cmath>
#include <string>
#include <iomanip>
#include <sstream>

int main() {
    openvdb::initialize();

    const int startFrame = 1;
    const int endFrame = 25;
    const std::string baseDir = "../textures/viewdepthmaps/";
    const std::string baseFilename = "0000nx.png";

    for (int frame = startFrame; frame <= endFrame; ++frame) {
        std::ostringstream oss;
        oss << baseDir << std::setw(4) << std::setfill('0') << frame << baseFilename.substr(4);
        std::string filename = oss.str();

        int width, height, channels;
        unsigned char* img = stbi_load(filename.c_str(), &width, &height, &channels, 0);

        if(img == nullptr) {
            std::cerr << "Error in loading the image: " << filename << std::endl;
            continue;
        }

        // Create OpenVDB grids
        openvdb::Vec3fGrid::Ptr rgbGrid = openvdb::Vec3fGrid::create();
        rgbGrid->setName("RGB");

        openvdb::FloatGrid::Ptr alphaGrid = openvdb::FloatGrid::create();
        alphaGrid->setName("Alpha");

        // Set values in the grids
        for (int y = 0; y < height; y++) {
            for (int z = 0; z < width; z++) {
                float r = img[(z * width * channels) + (y * channels)] / 255.0f;
                float g = img[(z * width * channels) + (y * channels) + 1] / 255.0f;
                float b = img[(z * width * channels) + (y * channels) + 2] / 255.0f;
                float a = img[(z * width * channels) + (y * channels) + 3] / 255.0f;

                float depth = 1.0f - a;
                int x = static_cast<int>(std::round(depth * (width - 1)));

                openvdb::Vec3f rgb(r, g, b);
                rgbGrid->tree().setValue(openvdb::Coord(width - 1 - x, y, z), rgb);

                alphaGrid->tree().setValue(openvdb::Coord(width - 1 - x, y, z), a);
            }
        }

        // Free the image data
        stbi_image_free(img);

        // Save the OpenVDB grids
        std::ostringstream vdbOss;
        vdbOss << "output_" << std::setw(4) << std::setfill('0') << frame << ".vdb";
        openvdb::io::File(vdbOss.str()).write({rgbGrid, alphaGrid});

        std::cout << "Processed frame " << frame << " and saved as " << vdbOss.str() << std::endl;
    }

    return 0;
}
