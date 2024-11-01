using UnityEngine;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

public class DepthMapProcessorGPU : MonoBehaviour
{
    public string imageFolder = "Assets/Resources/DepthMaps/";
    private const int TEXTURE_SIZE = 128;
    private const int PADDING = 1;
    public Texture3D volumeTexture;
    public List<string[]> frameFiles;
    public int currentFrameIndex = 1;
    public string texturePropertyName = "VolumeTexture";
    public float targetFPS = 60f;
    public GameObject volumeContainerPrefab;

    private VolumeRenderedObject volumeRenderer;
    private VolumeDataset dataset;
    public NativeArray<Color> volumeData;
    private Texture2D[][] loadedTextures;
    private RenderTexture[] viewRenderTextures;
    private RenderTexture finalRenderTexture;
    private ComputeShader processComputeShader;
    private ComputeBuffer[] viewBuffers;
    private ComputeBuffer combinedBuffer;
    private ComputeBuffer viewIndexBuffer;
    private ComputeShader clearComputeShader;

    private float nextUpdateTime;

    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential
    )]
    public struct VoxelColorData
    {
        public Vector4 color;
    }

    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential
    )]
    public struct VoxelIndexData
    {
        public uint viewIndex;
    }

    private void Awake()
    {
        if (volumeContainerPrefab == null)
        {
            Debug.LogError("VolumeContainer prefab is not assigned in the Inspector.");
            return;
        }

        // Create and assign the VolumeDataset
        dataset = CreateVolumeDataset(TEXTURE_SIZE, TEXTURE_SIZE, TEXTURE_SIZE);
        if (dataset == null)
        {
            Debug.LogError("Failed to create VolumeDataset.");
            return;
        }

        // Create the initial volume texture
        CreateInitialVolumeTexture();

        // Load frame files and initialize buffers
        LoadFrameFiles();
        InitializeBuffers();

        if (frameFiles == null || frameFiles.Count == 0)
        {
            Debug.LogError("No frame files loaded.");
            return;
        }

        clearComputeShader = Resources.Load<ComputeShader>("ClearVolumeBuffer");
        if (clearComputeShader == null)
        {
            Debug.LogError("ClearVolumeBuffer compute shader not found in Resources folder.");
        }

        LoadAllTextures();
        InitializeRenderTextures();

        // Instantiate and set up VolumeRenderedObject
        SetupVolumeRenderedObject();

        UpdateVolumeTexture();

        nextUpdateTime = Time.time;
    }

    private void SetupVolumeRenderedObject()
    {
        // Instantiate VolumeRenderedObject
        GameObject volumeObject = new GameObject("VolumeRenderedObject");

        // Instantiate VolumeContainer prefab as a child of VolumeRenderedObject
        GameObject volumeContainer = Instantiate(volumeContainerPrefab, volumeObject.transform);
        volumeContainer.name = "VolumeContainer";

        // Get the MeshRenderer component from the VolumeContainer
        MeshRenderer containerMeshRenderer = volumeContainer.GetComponent<MeshRenderer>();
        if (containerMeshRenderer == null)
        {
            Debug.LogError("No MeshRenderer found on VolumeContainer!");
            return;
        }

        // Add VolumeRenderedObject component and set its properties
        volumeRenderer = volumeObject.AddComponent<VolumeRenderedObject>();
        volumeRenderer.meshRenderer = containerMeshRenderer;
        volumeRenderer.volumeContainerObject = volumeContainer;

        // Assign dataset and initial texture
        volumeRenderer.SetDataset(dataset);
        volumeRenderer.UpdateTexture(volumeTexture);
    }

    private void InitializeBuffers()
    {
        viewBuffers = new ComputeBuffer[6];
        for (int i = 0; i < 6; i++)
        {
            viewBuffers[i] = new ComputeBuffer(
                TEXTURE_SIZE * TEXTURE_SIZE * TEXTURE_SIZE,
                System.Runtime.InteropServices.Marshal.SizeOf(typeof(VoxelColorData))
            );
        }

        viewIndexBuffer = new ComputeBuffer(
            TEXTURE_SIZE * TEXTURE_SIZE * TEXTURE_SIZE,
            System.Runtime.InteropServices.Marshal.SizeOf(typeof(VoxelIndexData))
        );

        combinedBuffer = new ComputeBuffer(
            TEXTURE_SIZE * TEXTURE_SIZE * TEXTURE_SIZE,
            System.Runtime.InteropServices.Marshal.SizeOf(typeof(VoxelColorData))
        );
    }

    void Update()
    {
        if (Time.time >= nextUpdateTime)
        {
            UpdateVolumeTexture();
            nextUpdateTime = Time.time + (1f / targetFPS);
        }
        // Ensure dataset is assigned
        if (volumeRenderer != null && dataset != null)
        {
            volumeRenderer.dataset = dataset;
        }
    }

    private void LoadFrameFiles()
    {
        string[] imageFiles = Directory.GetFiles(imageFolder, "*.png");
        frameFiles = new List<string[]>(imageFiles.Length / 6);

        foreach (string file in imageFiles)
        {
            string fileName = Path.GetFileName(file);
            int frameNumber = int.Parse(fileName.Substring(0, 4));

            while (frameFiles.Count <= frameNumber)
            {
                frameFiles.Add(new string[6]);
            }

            string viewDirection = fileName.Substring(4, 2);
            int viewIndex = GetViewIndex(viewDirection);

            frameFiles[frameNumber][viewIndex] = file;
        }
    }

    private int GetViewIndex(string viewDirection)
    {
        switch (viewDirection)
        {
            case "nx":
                return 0;
            case "ny":
                return 1;
            case "nz":
                return 2;
            case "px":
                return 3;
            case "py":
                return 4;
            case "pz":
                return 5;
            default:
                throw new System.ArgumentOutOfRangeException(
                    nameof(viewDirection),
                    viewDirection,
                    null
                );
        }
    }

    private void LoadAllTextures()
    {
        loadedTextures = new Texture2D[frameFiles.Count][];
        for (int frameIndex = 1; frameIndex < frameFiles.Count; frameIndex++)
        {
            loadedTextures[frameIndex] = new Texture2D[6];
            for (int viewIndex = 0; viewIndex < 6; viewIndex++)
            {
                string file = frameFiles[frameIndex][viewIndex];
                if (!string.IsNullOrEmpty(file))
                {
                    byte[] imageData = File.ReadAllBytes(file);
                    loadedTextures[frameIndex][viewIndex] = new Texture2D(
                        TEXTURE_SIZE,
                        TEXTURE_SIZE,
                        TextureFormat.RGBAFloat,
                        false
                    );
                    loadedTextures[frameIndex][viewIndex].LoadImage(imageData);
                }
            }
        }
    }

    private void InitializeRenderTextures()
    {
        viewRenderTextures = new RenderTexture[6];
        for (int i = 0; i < 6; i++)
        {
            viewRenderTextures[i] = new RenderTexture(
                TEXTURE_SIZE,
                TEXTURE_SIZE,
                0,
                RenderTextureFormat.ARGBFloat
            );
        }

        finalRenderTexture = new RenderTexture(
            TEXTURE_SIZE,
            TEXTURE_SIZE,
            0,
            RenderTextureFormat.ARGBFloat
        );

        processComputeShader = Resources.Load<ComputeShader>("ProcessViewsComputeShader");

        if (processComputeShader == null)
        {
            Debug.LogError("ProcessViewsComputeShader not found in Resources folder.");
        }
    }

    public void CreateInitialVolumeTexture()
    {
        if (volumeTexture == null || volumeTexture.width != TEXTURE_SIZE)
        {
            volumeTexture = new Texture3D(
                TEXTURE_SIZE,
                TEXTURE_SIZE,
                TEXTURE_SIZE,
                TextureFormat.RGBAFloat,
                false
            );
            volumeTexture.wrapMode = TextureWrapMode.Repeat;
            volumeTexture.filterMode = FilterMode.Point;
        }

        // Initialize the texture with default values (e.g., black)
        volumeData = new NativeArray<Color>(
            TEXTURE_SIZE * TEXTURE_SIZE * TEXTURE_SIZE,
            Allocator.Persistent
        );
        for (int i = 0; i < volumeData.Length; i++)
        {
            volumeData[i] = Color.black;
        }
        volumeTexture.SetPixelData(volumeData, 0);
        volumeTexture.Apply();

        // Update the VolumeRenderedObject with the initial texture
        if (volumeRenderer != null)
        {
            volumeRenderer.UpdateTexture(volumeTexture);
        }
    }

    private VolumeDataset CreateVolumeDataset(int width, int height, int depth)
    {
        VolumeDataset dataset = ScriptableObject.CreateInstance<VolumeDataset>();
        dataset.dimX = width;
        dataset.dimY = height;
        dataset.dimZ = depth;
        dataset.data = new float[width * height * depth * 4]; // 4 times larger to hold RGBA data
        dataset.volumeScale = 1f;
        dataset.scale = Vector3.one;
        dataset.rotation = Quaternion.identity;
        dataset.datasetName = "Generated Dataset";
        return dataset;
    }

    private void UpdateVolumeTexture()
    {
        currentFrameIndex = (currentFrameIndex % (frameFiles.Count - 1)) + 1;

        ProcessFrame(currentFrameIndex);

        // Create a new Texture3D from the combined buffer data
        if (volumeTexture == null || volumeTexture.width != TEXTURE_SIZE)
        {
            volumeTexture = new Texture3D(
                TEXTURE_SIZE,
                TEXTURE_SIZE,
                TEXTURE_SIZE,
                TextureFormat.RGBAFloat,
                false
            );
            volumeTexture.wrapMode = TextureWrapMode.Repeat;
            volumeTexture.filterMode = FilterMode.Point;
        }

        if (combinedBuffer != null)
        {
            VoxelColorData[] bufferData = new VoxelColorData[
                TEXTURE_SIZE * TEXTURE_SIZE * TEXTURE_SIZE
            ];
            combinedBuffer.GetData(bufferData);

            Color[] combinedData = new Color[TEXTURE_SIZE * TEXTURE_SIZE * TEXTURE_SIZE];
            for (int i = 0; i < TEXTURE_SIZE * TEXTURE_SIZE * TEXTURE_SIZE; i++)
            {
                VoxelColorData voxel = bufferData[i];
                combinedData[i] = new Color(
                    voxel.color.x,
                    voxel.color.y,
                    1f,
                    1f
                );
            }

            volumeTexture.SetPixelData(combinedData, 0);
            volumeTexture.Apply();
        }
        else
        {
            Debug.LogError("combinedBuffer is null");
        }

        if (volumeRenderer != null)
        {
            volumeRenderer.UpdateTexture(volumeTexture);
        }
        else 
        {
            Debug.LogError("volumeRenderer is null");
        }

        UpdateDatasetData();
    }

    private void ClearBuffer(ComputeBuffer colorBuffer, ComputeBuffer indexBuffer)
    {
        int clearKernelIndex = clearComputeShader.FindKernel("CSMain");
        clearComputeShader.SetBuffer(clearKernelIndex, "_CombinedBuffer", colorBuffer);
        clearComputeShader.SetBuffer(clearKernelIndex, "_ViewIndexBuffer", indexBuffer);
        clearComputeShader.SetInt("_BufferSize", TEXTURE_SIZE * TEXTURE_SIZE * TEXTURE_SIZE);
        clearComputeShader.Dispatch(
            clearKernelIndex,
            (TEXTURE_SIZE * TEXTURE_SIZE * TEXTURE_SIZE + 255) / 256,
            1,
            1
        );
    }

    private void UpdateDatasetData()
    {
        if (dataset != null)
        {
            VoxelColorData[] colorBufferData = new VoxelColorData[
                TEXTURE_SIZE * TEXTURE_SIZE * TEXTURE_SIZE
            ];
            combinedBuffer.GetData(colorBufferData);

            VoxelIndexData[] indexBufferData = new VoxelIndexData[
                TEXTURE_SIZE * TEXTURE_SIZE * TEXTURE_SIZE
            ];
            viewIndexBuffer.GetData(indexBufferData);

            for (int i = 0; i < TEXTURE_SIZE * TEXTURE_SIZE * TEXTURE_SIZE; i++)
            {
                VoxelColorData colorData = colorBufferData[i];
                VoxelIndexData indexData = indexBufferData[i];

                int baseIndex = i * 4;
                dataset.data[baseIndex] = colorData.color.x;
                dataset.data[baseIndex + 1] = colorData.color.y;
                dataset.data[baseIndex + 2] = colorData.color.z;
                dataset.data[baseIndex + 3] = colorData.color.w;
            }
            dataset.dataTexture = null; // Force recreation of the texture
        }
    }

    private void ProcessFrame(int frameIndex)
    {
        if (frameFiles == null || frameFiles.Count == 0)
        {
            Debug.LogWarning("No frame files loaded. Cannot process frame.");
            return;
        }

        // Clear all view buffers
        for (int i = 0; i < 6; i++)
        {
            ClearBuffer(viewBuffers[i], viewIndexBuffer);
        }

        for (int viewIndex = 0; viewIndex < 6; viewIndex++)
        {
            if (
                frameIndex >= 1
                && frameIndex <= frameFiles.Count
                && viewIndex < frameFiles[frameIndex].Length
            )
            {
                Texture2D imageTexture = loadedTextures[frameIndex][viewIndex];
                if (imageTexture != null)
                {
                    ProcessView(imageTexture, viewIndex);
                }
                else
                {
                    Debug.LogWarning($"Missing texture for frame {frameIndex}, view {viewIndex}");
                }
            }
            else
            {
                Debug.LogWarning(
                    $"Invalid frame or view index: frame {frameIndex}, view {viewIndex}"
                );
            }
        }

        CombineVoxelsCPU();
    }

    void ProcessView(Texture2D imageTexture, int viewIndex)
    {
        if (
            imageTexture == null
            || viewRenderTextures == null
            || viewRenderTextures.Length <= viewIndex
            || viewBuffers[viewIndex] == null
            || processComputeShader == null
        )
        {
            Debug.LogError(
                $"Invalid texture, render texture array, view buffer, or compute shader for view {viewIndex}"
            );
            return;
        }

        RenderTexture.active = viewRenderTextures[viewIndex];
        Graphics.Blit(imageTexture, viewRenderTextures[viewIndex]);

        int kernelIndex = processComputeShader.FindKernel("CSMain");
        if (kernelIndex == -1)
        {
            Debug.LogError("Kernel 'CSMain' not found in ProcessViewsComputeShader");
            return;
        }

        processComputeShader.SetInt("_ViewIndex", viewIndex);
        processComputeShader.SetTexture(
            kernelIndex,
            "_InputTexture",
            viewRenderTextures[viewIndex]
        );
        processComputeShader.SetBuffer(kernelIndex, "_VolumeBuffer", viewBuffers[viewIndex]);
        processComputeShader.SetInt("_BufferWidth", TEXTURE_SIZE);
        processComputeShader.SetInt("_BufferHeight", TEXTURE_SIZE);
        processComputeShader.SetInt("_BufferDepth", TEXTURE_SIZE);
        processComputeShader.SetInt("_Padding", PADDING);
        processComputeShader.Dispatch(kernelIndex, TEXTURE_SIZE / 16, TEXTURE_SIZE / 16, 1);
    }

    private void CombineVoxelsCPU()
    {
        if (combinedBuffer == null || viewIndexBuffer == null)
        {
            Debug.LogError("Combined buffer or ViewIndex buffer is null");
            return;
        }

        VoxelColorData[] combinedData = new VoxelColorData[
            TEXTURE_SIZE * TEXTURE_SIZE * TEXTURE_SIZE
        ];
        VoxelIndexData[] viewIndexData = new VoxelIndexData[
            TEXTURE_SIZE * TEXTURE_SIZE * TEXTURE_SIZE
        ];

        for (int i = 0; i < 6; i++)
        {
            VoxelIndexData[] viewBufferData = new VoxelIndexData[
                TEXTURE_SIZE * TEXTURE_SIZE * TEXTURE_SIZE
            ];
            viewBuffers[i].GetData(viewBufferData);

            for (int j = 0; j < TEXTURE_SIZE * TEXTURE_SIZE * TEXTURE_SIZE; j++)
            {
                if (viewBufferData[j].viewIndex > 0)
                {
                    combinedData[j].color += new Vector4(
                        viewBufferData[j].viewIndex,
                        viewBufferData[j].viewIndex,
                        viewBufferData[j].viewIndex,
                        viewBufferData[j].viewIndex
                    );
                    viewIndexData[j].viewIndex = (uint)
                        Mathf.Max(viewIndexData[j].viewIndex, viewBufferData[j].viewIndex);
                }
            }
        }

        // Normalize colors
        for (int i = 0; i < TEXTURE_SIZE * TEXTURE_SIZE * TEXTURE_SIZE; i++)
        {
            if (combinedData[i].color.w > 0f)
            {
                combinedData[i].color = new Vector4(
                    combinedData[i].color.x / combinedData[i].color.w,
                    combinedData[i].color.y / combinedData[i].color.w,
                    combinedData[i].color.z / combinedData[i].color.w,
                    1f
                );
            }
        }

        combinedBuffer.SetData(combinedData);
        viewIndexBuffer.SetData(viewIndexData);
    }

    void OnDisable()
    {
        if (combinedBuffer != null)
        {
            combinedBuffer.Release();
        }
        if (volumeTexture != null)
        {
            Destroy(volumeTexture);
        }
        if (volumeData.IsCreated)
        {
            volumeData.Dispose();
        }

        if (viewRenderTextures != null)
        {
            foreach (var rt in viewRenderTextures)
            {
                if (rt != null)
                {
                    RenderTexture.active = null;
                    rt.Release();
                }
            }
        }

        if (finalRenderTexture != null)
        {
            RenderTexture.active = null;
            finalRenderTexture.Release();
        }
        if (viewBuffers != null)
        {
            foreach (var buffer in viewBuffers)
            {
                if (buffer != null)
                {
                    buffer.Release();
                }
            }
        }

        if (viewIndexBuffer != null)
        {
            viewIndexBuffer.Release();
        }
    }
}
