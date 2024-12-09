# Multiview Rendering
<img width="652" alt="side_by_side_blebder" src="https://github.com/user-attachments/assets/b98769a4-2a23-4b62-8233-1e4ffd7ebdac">

A C++ project for processing and rendering multi-view depth maps using OpenVDB.

## Overview

This project converts a series of depth map images from different viewpoints into a volumetric representation using OpenVDB. It processes images from six different views (nx, ny, nz, px, py, pz) and combines them into a single volumetric dataset.

## Prerequisites

- CMake (3.20 or higher)
- OpenVDB library
- C++14 compatible compiler
- Build essentials (make, etc.)

### Installing OpenVDB

#### Ubuntu/Debian
apt-get install -y libboost-iostreams-dev libtbb-dev libblosc-dev libopenvdb-dev

#### macOS
brew install openvdb

## C++ Project Structure

 ```
├── CMakeLists.txt          # CMake configuration
├── include/                # Header files
│   └── stb_image.h        # Image loading library
├── src/                   # Source files
│   └── main.cpp          # Main program
└── textures/             # Input textures directory
    └── viewdepthmaps/    # Depth map images
 ```
## Building the Project

1. Clone the repository:
 ```
git clone https://github.com/a-sumo/multiview-rendering
cd cpp
 ```

2. Use the provided build script:
 ```
chmod +x build_and_run.sh
./build_and_run.sh
 ```
## Usage

### Command Line Arguments

./cpp [options]
Options:
  --start N     Start frame number (default: 1)
  --end N       End frame number (default: 25)
  --dir path    Base directory for textures
  --size N      Texture size (default: 128)
  --help        Show this help message

### Input Image Format

Place your depth map images in the `textures/viewdepthmaps/` directory using the following naming convention:
- Format: <frame_number><view_direction>.png
- Example: 0001nx.png (frame 1, negative x direction)

View directions:
- nx: negative x
- ny: negative y
- nz: negative z
- px: positive x
- py: positive y
- pz: positive z

### Example Usage

# Process frames 1-10 with custom texture size
./build_and_run.sh --start 1 --end 10 --size 256

# Process specific directory
./build_and_run.sh --dir /path/to/textures/

## Output

![side_by_side_1](https://github.com/user-attachments/assets/9e4100e8-bbe2-4dfa-85be-ce8bd36ff244)

The program generates OpenVDB files named output_XXXX.vdb where XXXX is the frame number. These files contain:
- RGB color information
- Alpha channel data

## Using the VDB Files in Blender

1. Open Blender (version 2.83 or later)

2. Create a new scene or open your existing project

3. Import the VDB sequence:
   - Switch to the Layout workspace
   - Press Shift + A to add a new object
   - Select Volume > OpenVDB Volume
   - Navigate to your output directory
   - Select the first file in your VDB sequence

4. Set up the volume sequence:
   - In the Properties panel, go to the Volume Properties tab
   - Under Sequence, check "Use Sequence"
   - Set the Frame Start and Frame Offset to match your render
   - Adjust the Frame Duration if needed

5. Configure the volume display:
   - In Volume Properties, under Viewport Display
   - Set the density to adjust visibility
   - Choose a color method (RGB or single color)

6. For animation:
   - The volume will automatically update based on the frame number
   - Press Alt + A to preview the animation
   - Adjust the sequence timing in the Volume Properties if needed

7. Rendering:
   - Switch to Cycles render engine for best results
   - Enable "Use Denoising" for cleaner renders
   - Adjust the Volume Sampling Step Size in Render Properties
     for better quality (lower values) or faster renders (higher values)

Note: For better performance, you may need to:
- Adjust the Volume Step Rate in the Render Properties
- Enable GPU rendering if available
- Use lower resolution preview in the viewport

## License

MIT License

