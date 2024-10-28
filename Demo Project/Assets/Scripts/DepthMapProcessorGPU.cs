using UnityEngine;
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

    private VolumeDataset dataset;
    private NativeArray<Color> volumeData;
    public VolumeRenderedObject volumeRenderer;
    private Texture2D[][] loadedTextures;
    private RenderTexture[] viewRenderTextures;
    private RenderTexture finalRenderTexture;
    private ComputeShader processComputeShader;
    private ComputeShader combineComputeShader;
    private ComputeShader clearComputeShader;
    
    private ComputeBuffer volumeBuffer;
    private float nextUpdateTime;

    private void Awake()
    {
        if (string.IsNullOrEmpty(imageFolder))
        {
            Debug.LogError("Image folder path is not set.");
            return;
        }

        LoadFrameFiles();
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
        CreateInitialVolumeBuffer();

        if (volumeBuffer == null)
        {
            Debug.LogError("Failed to create volume buffer.");
            return;
        }

        if (volumeRenderer == null)
        {
            Debug.LogError("VolumeRenderedObject is not assigned in the Inspector.");
            return;
        }

        dataset = CreateVolumeDataset(TEXTURE_SIZE, TEXTURE_SIZE, TEXTURE_SIZE);
        if (dataset == null)
        {
            Debug.LogError("Failed to create VolumeDataset.");
            return;
        }

        volumeRenderer.dataset = dataset;

        UpdateVolumeTexture();

        nextUpdateTime = Time.time;
    }

    void Update()
    {
        if (Time.time >= nextUpdateTime)
        {
            UpdateVolumeTexture();
            nextUpdateTime = Time.time + (1f / targetFPS);
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
        combineComputeShader = Resources.Load<ComputeShader>("CombineVoxelsComputeShader");

        if (processComputeShader == null)
        {
            Debug.LogError("ProcessViewsComputeShader not found in Resources folder.");
        }

        if (combineComputeShader == null)
        {
            Debug.LogError("CombineVoxelsComputeShader not found in Resources folder.");
        }
    }

    public void CreateInitialVolumeTexture()
    {
        RenderTextureDescriptor descriptor = new RenderTextureDescriptor(
            TEXTURE_SIZE,
            TEXTURE_SIZE,
            RenderTextureFormat.ARGBFloat,
            0,
            0,
            RenderTextureReadWrite.Linear
        );
        descriptor.enableRandomWrite = true;
        descriptor.volumeDepth = TEXTURE_SIZE;
        descriptor.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;

        RenderTexture renderTexture = new RenderTexture(descriptor);
        renderTexture.Create();

        volumeTexture = new Texture3D(
            TEXTURE_SIZE,
            TEXTURE_SIZE,
            TEXTURE_SIZE,
            TextureFormat.RGBAFloat,
            false
        );
        volumeTexture.wrapMode = TextureWrapMode.Repeat;
        volumeTexture.filterMode = FilterMode.Point;

        volumeData = new NativeArray<Color>(
            TEXTURE_SIZE * TEXTURE_SIZE * TEXTURE_SIZE,
            Allocator.Persistent
        );
        ProcessFrame(1);

        // Instead of using Graphics.CopyTexture, we'll update the volumeTexture directly
        volumeTexture.SetPixelData(volumeData, 0);
        volumeTexture.Apply();

        renderTexture.Release();
    }

    public void CreateInitialVolumeBuffer()
    {
        volumeBuffer = new ComputeBuffer(
            TEXTURE_SIZE * TEXTURE_SIZE * TEXTURE_SIZE,
            sizeof(float) * 4
        );
        ProcessFrame(1);
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

        // Create a new Texture3D from the buffer data
        if (volumeTexture == null || volumeTexture.width != TEXTURE_SIZE)
        {
            volumeTexture = new Texture3D(
                TEXTURE_SIZE,
                TEXTURE_SIZE,
                TEXTURE_SIZE,
                TextureFormat.RGBAFloat,
                false
            );
        }

        Color[] bufferData = new Color[TEXTURE_SIZE * TEXTURE_SIZE * TEXTURE_SIZE];
        volumeBuffer.GetData(bufferData);
        volumeTexture.SetPixelData(bufferData, 0);
        volumeTexture.Apply();

        volumeRenderer.UpdateTexture(volumeTexture);

        UpdateDatasetData();
    }

    private void UpdateDatasetData()
    {
        if (dataset != null)
        {
            Color[] bufferData = new Color[TEXTURE_SIZE * TEXTURE_SIZE * TEXTURE_SIZE];
            volumeBuffer.GetData(bufferData);

            for (int i = 0; i < bufferData.Length; i++)
            {
                dataset.data[i] = bufferData[i].r; // Store red channel
                dataset.data[i + bufferData.Length] = bufferData[i].g; // Store green channel
                dataset.data[i + bufferData.Length * 2] = bufferData[i].b; // Store blue channel
                dataset.data[i + bufferData.Length * 3] = bufferData[i].a; // Store alpha channel
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

        // Clear the volume buffer
        int clearKernelIndex = clearComputeShader.FindKernel("CSMain");
        clearComputeShader.SetBuffer(clearKernelIndex, "_VolumeBuffer", volumeBuffer);
        clearComputeShader.SetInt("_BufferSize", TEXTURE_SIZE * TEXTURE_SIZE * TEXTURE_SIZE);
        clearComputeShader.Dispatch(
            clearKernelIndex,
            (TEXTURE_SIZE * TEXTURE_SIZE * TEXTURE_SIZE + 255) / 256,
            1,
            1
        );

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

        CombineVoxelsGPU();
    }

    void ProcessView(Texture2D imageTexture, int viewIndex)
    {
        if (
            imageTexture == null
            || viewRenderTextures == null
            || viewRenderTextures.Length <= viewIndex
            || volumeBuffer == null
            || processComputeShader == null
        )
        {
            Debug.LogError(
                $"Invalid texture, render texture array, volume buffer, or compute shader for view {viewIndex}"
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
        processComputeShader.SetBuffer(kernelIndex, "_VolumeBuffer", volumeBuffer);
        processComputeShader.SetInt("_BufferWidth", TEXTURE_SIZE);
        processComputeShader.SetInt("_BufferHeight", TEXTURE_SIZE);
        processComputeShader.SetInt("_BufferDepth", TEXTURE_SIZE);
        processComputeShader.Dispatch(kernelIndex, TEXTURE_SIZE / 16, TEXTURE_SIZE / 16, 1);
    }

    void CombineVoxelsGPU()
    {
        if (volumeBuffer == null)
        {
            Debug.LogError("Volume buffer is null");
            return;
        }

        int kernelIndex = combineComputeShader.FindKernel("CSMain");
        combineComputeShader.SetBuffer(kernelIndex, "_VolumeBuffer", volumeBuffer);
        combineComputeShader.SetInt("_BufferWidth", TEXTURE_SIZE);
        combineComputeShader.SetInt("_BufferHeight", TEXTURE_SIZE);
        combineComputeShader.SetInt("_BufferDepth", TEXTURE_SIZE);
        combineComputeShader.Dispatch(
            kernelIndex,
            TEXTURE_SIZE / 8,
            TEXTURE_SIZE / 8,
            TEXTURE_SIZE / 8
        );
    }

    void OnDisable()
    {
        if (volumeBuffer != null)
        {
            volumeBuffer.Release();
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
    }
}
