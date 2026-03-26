using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

public enum ExplosionMode
{
    StatelessGeometry = 0,
    PersistentCompute = 1,
}

[StructLayout(LayoutKind.Sequential)]
public struct TriangleState
{
    public Vector3 offset;
    public Vector3 velocity;
    public float lifetime;
    public uint active;
}

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class TriangleExplosionController : MonoBehaviour
{
    private static readonly int ExplosionModeId = Shader.PropertyToID("_ExplosionMode");
    private static readonly int TriangleStatesId = Shader.PropertyToID("_TriangleStates");
    private static readonly int TriangleCountId = Shader.PropertyToID("_TriangleCount");
    private static readonly int StatelessSpeedId = Shader.PropertyToID("_StatelessSpeed");
    private static readonly int StatelessDistanceId = Shader.PropertyToID("_StatelessDistance");
    private static readonly int DeltaTimeId = Shader.PropertyToID("_DeltaTime");
    private static readonly int AccelerationId = Shader.PropertyToID("_Acceleration");

    private const int ThreadGroupSize = 64;

    [SerializeField] private ComputeShader simulationShader;
    [SerializeField] private Material explosionMaterial;
    [SerializeField] private MeshFilter targetMeshFilter;
    [SerializeField] private ExplosionMode startMode = ExplosionMode.StatelessGeometry;
    [SerializeField] private float statelessSpeed = 2.25f;
    [SerializeField] private float statelessDistance = 1.75f;
    [SerializeField] private float initialSpeedMin = 2.0f;
    [SerializeField] private float initialSpeedMax = 6.0f;
    [SerializeField] private Vector3 acceleration = new Vector3(0.0f, -1.5f, 0.0f);
    [SerializeField] private float lifetimeMin = 1.2f;
    [SerializeField] private float lifetimeMax = 2.6f;
    [SerializeField] private int randomSeed = 12345;
    [SerializeField] private bool autoRestart = true;

    private MeshFilter runtimeMeshFilter;
    private MeshRenderer runtimeMeshRenderer;
    private Mesh sourceMesh;
    private Mesh expandedMesh;
    private Material originalMaterial;
    private Material runtimeMaterial;
    private ComputeBuffer triangleStateBuffer;
    private Vector3[] triangleCenters = Array.Empty<Vector3>();
    private Vector3[] triangleNormals = Array.Empty<Vector3>();
    private int triangleCount;
    private int kernelHandle = -1;
    private ExplosionMode currentMode;
    private float simulationElapsed;
    private int restartIndex;
    private bool initialized;

    private void OnEnable()
    {
        InitializeIfNeeded();
    }

    private void Update()
    {
        if (!initialized)
        {
            return;
        }

        HandleInput();
        PushMaterialProperties();

        if (currentMode == ExplosionMode.PersistentCompute)
        {
            DispatchSimulation();
        }
    }

    private void OnDisable()
    {
        ReleaseRuntimeResources();
    }

    private void OnDestroy()
    {
        ReleaseRuntimeResources();
    }

    private void InitializeIfNeeded()
    {
        if (initialized)
        {
            return;
        }

        if (!SystemInfo.supportsComputeShaders)
        {
            Debug.LogError("TriangleExplosionController requires compute shader support.");
            enabled = false;
            return;
        }

        runtimeMeshFilter = targetMeshFilter != null ? targetMeshFilter : GetComponent<MeshFilter>();
        runtimeMeshRenderer = runtimeMeshFilter != null ? runtimeMeshFilter.GetComponent<MeshRenderer>() : null;

        if (runtimeMeshFilter == null || runtimeMeshRenderer == null)
        {
            Debug.LogError("TriangleExplosionController requires a MeshFilter and MeshRenderer.");
            enabled = false;
            return;
        }

        if (simulationShader == null || explosionMaterial == null)
        {
            Debug.LogError("TriangleExplosionController is missing the compute shader or material reference.");
            enabled = false;
            return;
        }

        sourceMesh = runtimeMeshFilter.sharedMesh;
        if (sourceMesh == null)
        {
            Debug.LogError("TriangleExplosionController requires a source mesh.");
            enabled = false;
            return;
        }

        kernelHandle = simulationShader.FindKernel("UpdateTriangles");
        originalMaterial = runtimeMeshRenderer.sharedMaterial;

        BuildExpandedMesh();
        CreateRuntimeMaterial();
        CreateTriangleStateBuffer();
        RestartPersistentSimulation(false);
        SetMode(startMode);

        initialized = true;
    }

    private void HandleInput()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            SetMode(ExplosionMode.StatelessGeometry);
        }

        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            RestartPersistentSimulation(false);
            SetMode(ExplosionMode.PersistentCompute);
        }

        if (Input.GetKeyDown(KeyCode.Space))
        {
            RestartPersistentSimulation(true);
        }
    }

    private void BuildExpandedMesh()
    {
        Vector3[] sourceVertices = sourceMesh.vertices;
        int[] sourceTriangles = sourceMesh.triangles;

        triangleCount = sourceTriangles.Length / 3;
        if (triangleCount == 0)
        {
            Debug.LogError("TriangleExplosionController found no triangles in the source mesh.");
            enabled = false;
            return;
        }

        Vector3[] expandedVertices = new Vector3[sourceTriangles.Length];
        Vector3[] expandedNormals = new Vector3[sourceTriangles.Length];
        Vector2[] expandedTriangleIds = new Vector2[sourceTriangles.Length];
        int[] expandedTriangles = new int[sourceTriangles.Length];

        triangleCenters = new Vector3[triangleCount];
        triangleNormals = new Vector3[triangleCount];

        for (int triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
        {
            int srcOffset = triangleIndex * 3;

            Vector3 p0 = sourceVertices[sourceTriangles[srcOffset]];
            Vector3 p1 = sourceVertices[sourceTriangles[srcOffset + 1]];
            Vector3 p2 = sourceVertices[sourceTriangles[srcOffset + 2]];

            Vector3 faceNormal = Vector3.Cross(p1 - p0, p2 - p0);
            if (faceNormal.sqrMagnitude < 0.000001f)
            {
                faceNormal = Vector3.up;
            }
            else
            {
                faceNormal.Normalize();
            }

            triangleCenters[triangleIndex] = (p0 + p1 + p2) / 3.0f;
            triangleNormals[triangleIndex] = faceNormal;

            expandedVertices[srcOffset] = p0;
            expandedVertices[srcOffset + 1] = p1;
            expandedVertices[srcOffset + 2] = p2;

            expandedNormals[srcOffset] = faceNormal;
            expandedNormals[srcOffset + 1] = faceNormal;
            expandedNormals[srcOffset + 2] = faceNormal;

            Vector2 triangleId = new Vector2(triangleIndex, 0.0f);
            expandedTriangleIds[srcOffset] = triangleId;
            expandedTriangleIds[srcOffset + 1] = triangleId;
            expandedTriangleIds[srcOffset + 2] = triangleId;

            expandedTriangles[srcOffset] = srcOffset;
            expandedTriangles[srcOffset + 1] = srcOffset + 1;
            expandedTriangles[srcOffset + 2] = srcOffset + 2;
        }

        expandedMesh = new Mesh
        {
            name = $"{sourceMesh.name}_TriangleExplosionRuntime",
            indexFormat = expandedVertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
        };

        expandedMesh.vertices = expandedVertices;
        expandedMesh.normals = expandedNormals;
        expandedMesh.triangles = expandedTriangles;
        expandedMesh.uv2 = expandedTriangleIds;
        expandedMesh.RecalculateBounds();
        expandedMesh.MarkDynamic();

        Bounds bounds = expandedMesh.bounds;
        bounds.Expand(Vector3.one * CalculateBoundsPadding() * 2.0f);
        expandedMesh.bounds = bounds;

        runtimeMeshFilter.sharedMesh = expandedMesh;
    }

    private void CreateRuntimeMaterial()
    {
        runtimeMaterial = new Material(explosionMaterial)
        {
            name = $"{explosionMaterial.name} (Runtime)",
            hideFlags = HideFlags.DontSave,
        };

        runtimeMeshRenderer.sharedMaterial = runtimeMaterial;
    }

    private void CreateTriangleStateBuffer()
    {
        int stride = Marshal.SizeOf<TriangleState>();
        triangleStateBuffer = new ComputeBuffer(triangleCount, stride);
        simulationShader.SetBuffer(kernelHandle, TriangleStatesId, triangleStateBuffer);
        runtimeMaterial.SetBuffer(TriangleStatesId, triangleStateBuffer);
    }

    private void RestartPersistentSimulation(bool forcePersistentMode)
    {
        if (triangleStateBuffer == null || triangleCount == 0)
        {
            return;
        }

        int restartSeed = unchecked(randomSeed + restartIndex * 7919);
        restartIndex++;
        var random = new System.Random(restartSeed);
        TriangleState[] states = new TriangleState[triangleCount];

        for (int triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
        {
            Vector3 randomDirection = NextUnitVector(random);
            Vector3 launchDirection = triangleNormals[triangleIndex] + triangleCenters[triangleIndex].normalized * 0.15f + randomDirection * 0.55f;
            if (launchDirection.sqrMagnitude < 0.000001f)
            {
                launchDirection = triangleNormals[triangleIndex];
            }

            launchDirection.Normalize();

            states[triangleIndex] = new TriangleState
            {
                offset = Vector3.zero,
                velocity = launchDirection * Mathf.Lerp(initialSpeedMin, initialSpeedMax, NextFloat(random)),
                lifetime = Mathf.Lerp(lifetimeMin, lifetimeMax, NextFloat(random)),
                active = 1u,
            };
        }

        triangleStateBuffer.SetData(states);
        simulationElapsed = 0.0f;

        if (forcePersistentMode)
        {
            SetMode(ExplosionMode.PersistentCompute);
        }
    }

    private void DispatchSimulation()
    {
        simulationShader.SetFloat(DeltaTimeId, Time.deltaTime);
        simulationShader.SetVector(AccelerationId, acceleration);
        simulationShader.SetInt(TriangleCountId, triangleCount);

        int threadGroupsX = Mathf.CeilToInt(triangleCount / (float)ThreadGroupSize);
        simulationShader.Dispatch(kernelHandle, threadGroupsX, 1, 1);

        simulationElapsed += Time.deltaTime;

        if (autoRestart && simulationElapsed >= lifetimeMax + 0.15f)
        {
            RestartPersistentSimulation(false);
        }
    }

    private void PushMaterialProperties()
    {
        runtimeMaterial.SetInt(ExplosionModeId, (int)currentMode);
        runtimeMaterial.SetInt(TriangleCountId, triangleCount);
        runtimeMaterial.SetFloat(StatelessSpeedId, statelessSpeed);
        runtimeMaterial.SetFloat(StatelessDistanceId, statelessDistance);
        runtimeMaterial.SetBuffer(TriangleStatesId, triangleStateBuffer);
    }

    private void SetMode(ExplosionMode mode)
    {
        currentMode = mode;
        if (runtimeMaterial != null)
        {
            runtimeMaterial.SetInt(ExplosionModeId, (int)currentMode);
        }
    }

    private float CalculateBoundsPadding()
    {
        float statelessPadding = Mathf.Max(0.0f, statelessDistance);
        float persistentPadding = Mathf.Max(initialSpeedMin, initialSpeedMax) * lifetimeMax
            + 0.5f * acceleration.magnitude * lifetimeMax * lifetimeMax;

        return Mathf.Max(statelessPadding, persistentPadding) + 1.0f;
    }

    private static float NextFloat(System.Random random)
    {
        return (float)random.NextDouble();
    }

    private static Vector3 NextUnitVector(System.Random random)
    {
        float z = Mathf.Lerp(-1.0f, 1.0f, NextFloat(random));
        float angle = NextFloat(random) * Mathf.PI * 2.0f;
        float radius = Mathf.Sqrt(Mathf.Max(0.0f, 1.0f - z * z));

        return new Vector3(
            radius * Mathf.Cos(angle),
            radius * Mathf.Sin(angle),
            z
        );
    }

    private void ReleaseRuntimeResources()
    {
        if (triangleStateBuffer != null)
        {
            triangleStateBuffer.Release();
            triangleStateBuffer = null;
        }

        if (runtimeMeshFilter != null && sourceMesh != null)
        {
            runtimeMeshFilter.sharedMesh = sourceMesh;
        }

        if (runtimeMeshRenderer != null && originalMaterial != null)
        {
            runtimeMeshRenderer.sharedMaterial = originalMaterial;
        }

        if (expandedMesh != null)
        {
            DestroyRuntimeObject(expandedMesh);
            expandedMesh = null;
        }

        if (runtimeMaterial != null)
        {
            DestroyRuntimeObject(runtimeMaterial);
            runtimeMaterial = null;
        }

        initialized = false;
    }

    private static void DestroyRuntimeObject(UnityEngine.Object runtimeObject)
    {
        if (runtimeObject == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(runtimeObject);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(runtimeObject);
        }
    }
}
