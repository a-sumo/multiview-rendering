#!/bin/bash

# build_and_run.sh
mkdir -p build
cd build
cmake ..
make -j$(nproc)

if [ $? -eq 0 ]; then
    echo "Build successful! Running the program..."
    ./multiview-volume "$@"
else
    echo "Build failed!"
    exit 1
fi
