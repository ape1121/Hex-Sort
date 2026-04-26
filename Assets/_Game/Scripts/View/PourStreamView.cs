using Ape.Core;
using UnityEngine;

public sealed class PourStreamView : MonoBehaviour
{
    [Header("Audio")]
    [Tooltip("SoundManager clip name played (looping) while the stream is visible. Empty = silent. The clip itself should have Loop = true on its Sound asset.")]
    [SerializeField] private string pourSoundName = "pour";

    private const int ArcSegmentCount = 18;
    private const int RadialSideCount = 8;
    private const int IgnoreRaycastLayer = 2;
    private const float StreamGravity = 7.4f;
    private const float StreamWidthMin = 0.045f;
    private const float StreamWidthMax = 0.115f;
    private const float StreamEndShrink = 0.78f;
    private const float WobbleAmplitude = 0.012f;
    private const float UvScrollSpeedMin = 1.4f;
    private const float UvScrollSpeedMax = 4.6f;
    private const float SplashEmitInterval = 0.045f;

    private MeshFilter tubeMeshFilter;
    private MeshRenderer tubeRenderer;
    private Mesh tubeMesh;
    private Material streamMaterial;
    private Vector3[] tubeVertices;
    private Vector2[] tubeUVs;
    private int[] tubeTriangles;
    private Vector3[] arcPoints;
    private Vector3 lastFrameRight;

    private ParticleSystem splashSystem;
    private ParticleSystem.EmitParams splashEmitParams;
    private float lastSplashEmitTime;
    private float uvScrollOffset;
    private float instanceSeed;

    private bool isVisible;
    private Vector3 fromPoint;
    private Vector3 toPoint;
    private Color currentColor;
    private float currentIntensity;

    public void Initialize(HexSortMaterialLibrary materials)
    {
        instanceSeed = Mathf.Abs(GetInstanceID() * 0.1731f);
        lastFrameRight = Vector3.right;

        GameObject tubeObject = new GameObject("StreamTube");
        tubeObject.transform.SetParent(transform, false);
        RuntimeViewUtility.SetLayerRecursively(tubeObject, IgnoreRaycastLayer);

        tubeMeshFilter = tubeObject.AddComponent<MeshFilter>();
        tubeRenderer = tubeObject.AddComponent<MeshRenderer>();
        tubeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tubeRenderer.receiveShadows = false;

        streamMaterial = materials.CreateStreamMaterialInstance();
        tubeRenderer.sharedMaterial = streamMaterial;

        BuildTubeBuffers();
        tubeMesh = new Mesh { name = "StreamTubeMesh" };
        tubeMesh.MarkDynamic();
        tubeMesh.vertices = tubeVertices;
        tubeMesh.triangles = tubeTriangles;
        tubeMesh.uv = tubeUVs;
        tubeMeshFilter.sharedMesh = tubeMesh;

        tubeObject.SetActive(false);

        splashSystem = BuildSplashSystem(materials);
    }

    public void Show(Vector3 startPoint, Vector3 endPoint, Color color, float intensity)
    {
        fromPoint = startPoint;
        toPoint = endPoint;
        currentColor = color;
        currentIntensity = Mathf.Clamp01(intensity);

        // Fire the loop sound only on the off→on edge so we don't keep restarting it every
        // frame the stream is visible.
        if (!isVisible)
        {
            StartPourSound();
        }
        isVisible = true;

        if (splashSystem != null && !splashSystem.isPlaying)
        {
            splashSystem.Play();
        }

        UpdateVisual(Time.time, Time.deltaTime);
    }

    public void Hide()
    {
        if (isVisible)
        {
            StopPourSound();
        }
        isVisible = false;
        if (tubeRenderer != null)
        {
            tubeRenderer.gameObject.SetActive(false);
        }

        if (splashSystem != null && splashSystem.isPlaying)
        {
            splashSystem.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    private void StartPourSound()
    {
        if (string.IsNullOrEmpty(pourSoundName) || App.Sound == null)
        {
            return;
        }
        App.Sound.PlaySound(pourSoundName);
    }

    private void StopPourSound()
    {
        if (string.IsNullOrEmpty(pourSoundName) || App.Sound == null)
        {
            return;
        }
        App.Sound.StopSound(pourSoundName);
    }

    private void OnDisable()
    {
        // Make sure the loop doesn't keep playing if the view is disabled mid-pour (e.g. on
        // scene unload).
        if (isVisible)
        {
            StopPourSound();
            isVisible = false;
        }
    }

    private void LateUpdate()
    {
        if (!isVisible || tubeRenderer == null)
        {
            return;
        }

        UpdateVisual(Time.time, Time.deltaTime);
    }

    private void UpdateVisual(float timeValue, float deltaTime)
    {
        Vector3 horizontalDelta = new Vector3(toPoint.x - fromPoint.x, 0f, toPoint.z - fromPoint.z);
        float horizontalDistance = horizontalDelta.magnitude;
        float verticalDistance = Mathf.Abs(toPoint.y - fromPoint.y);
        float totalDistance = Mathf.Sqrt((horizontalDistance * horizontalDistance) + (verticalDistance * verticalDistance));
        if (totalDistance < 0.005f)
        {
            Hide();
            return;
        }

        ComputeArcPoints(fromPoint, toPoint, currentIntensity, arcPoints);
        RebuildTubeMesh(arcPoints, currentIntensity, timeValue);

        Color tintedColor = Color.Lerp(currentColor, Color.white, 0.18f);
        tintedColor.a = 0.78f + (currentIntensity * 0.18f);
        RuntimeViewUtility.SetMaterialColor(streamMaterial, tintedColor);

        float scrollSpeed = Mathf.Lerp(UvScrollSpeedMin, UvScrollSpeedMax, currentIntensity);
        uvScrollOffset -= scrollSpeed * deltaTime;
        if (uvScrollOffset < -10f || uvScrollOffset > 10f)
        {
            uvScrollOffset %= 1f;
        }

        RuntimeViewUtility.SetMaterialMainTextureOffset(streamMaterial, new Vector2(0f, uvScrollOffset));

        tubeRenderer.gameObject.SetActive(true);

        EmitSplash(timeValue, tintedColor);
    }

    private void EmitSplash(float timeValue, Color tintedColor)
    {
        if (splashSystem == null)
        {
            return;
        }

        if (timeValue - lastSplashEmitTime < SplashEmitInterval)
        {
            return;
        }

        lastSplashEmitTime = timeValue;

        Vector3 impactPoint = arcPoints[arcPoints.Length - 1];
        Vector3 incomingTangent = (impactPoint - arcPoints[arcPoints.Length - 2]).normalized;
        Vector3 reflected = Vector3.Reflect(incomingTangent, Vector3.up);

        int particleCount = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(1, 5, currentIntensity)), 1, 6);
        for (int i = 0; i < particleCount; i++)
        {
            Vector3 randomOffset = Random.insideUnitSphere * 0.05f;
            randomOffset.y = Mathf.Abs(randomOffset.y) * 0.4f;

            Vector3 baseVelocity = reflected * Mathf.Lerp(0.6f, 1.6f, currentIntensity);
            Vector3 jitter = new Vector3(
                Random.Range(-0.7f, 0.7f),
                Random.Range(0.4f, 1.3f),
                Random.Range(-0.7f, 0.7f));
            Vector3 finalVelocity = baseVelocity + jitter;

            Color splashColor = tintedColor;
            splashColor.a = 0.85f;

            splashEmitParams.position = impactPoint + randomOffset;
            splashEmitParams.velocity = finalVelocity;
            splashEmitParams.startColor = splashColor;
            splashEmitParams.startSize = Random.Range(0.045f, 0.085f) * Mathf.Lerp(0.7f, 1.1f, currentIntensity);
            splashEmitParams.startLifetime = Random.Range(0.30f, 0.55f);

            splashSystem.Emit(splashEmitParams, 1);
        }
    }

    private void ComputeArcPoints(Vector3 start, Vector3 end, float intensity, Vector3[] outPoints)
    {
        Vector3 horizontalDelta = new Vector3(end.x - start.x, 0f, end.z - start.z);
        float horizontalDistance = horizontalDelta.magnitude;
        Vector3 horizontalDir = horizontalDistance > 0.001f
            ? horizontalDelta / horizontalDistance
            : Vector3.forward;

        float horizontalSpeed = Mathf.Lerp(1.6f, 3.4f, intensity);
        float timeOfFlight = horizontalDistance / Mathf.Max(horizontalSpeed, 0.25f);

        if (horizontalDistance < 0.06f)
        {
            timeOfFlight = Mathf.Sqrt(Mathf.Max(0.05f, 2f * Mathf.Abs(start.y - end.y) / StreamGravity));
        }

        timeOfFlight = Mathf.Clamp(timeOfFlight, 0.05f, 1.6f);

        float initialVerticalVelocity =
            ((end.y - start.y) + (0.5f * StreamGravity * timeOfFlight * timeOfFlight)) / timeOfFlight;

        for (int i = 0; i < outPoints.Length; i++)
        {
            float u = i / (float)(outPoints.Length - 1);
            float t = u * timeOfFlight;
            float horizontalDistanceAtT = horizontalSpeed * t;
            if (horizontalDistance < 0.06f)
            {
                horizontalDistanceAtT = u * horizontalDistance;
            }

            Vector3 horizontalPosition = horizontalDir * horizontalDistanceAtT;
            float yPosition = start.y + (initialVerticalVelocity * t) - (0.5f * StreamGravity * t * t);
            outPoints[i] = new Vector3(start.x + horizontalPosition.x, yPosition, start.z + horizontalPosition.z);
        }

        outPoints[outPoints.Length - 1] = end;
    }

    private void RebuildTubeMesh(Vector3[] points, float intensity, float timeValue)
    {
        int segmentCount = points.Length;
        float startWidth = Mathf.Lerp(StreamWidthMin, StreamWidthMax, intensity);
        float endWidth = startWidth * StreamEndShrink;

        for (int s = 0; s < segmentCount; s++)
        {
            Vector3 tangent;
            if (s == 0)
            {
                tangent = (points[1] - points[0]).normalized;
            }
            else if (s == segmentCount - 1)
            {
                tangent = (points[s] - points[s - 1]).normalized;
            }
            else
            {
                tangent = (points[s + 1] - points[s - 1]).normalized;
            }

            Vector3 right = Vector3.Cross(tangent, Vector3.up);
            if (right.sqrMagnitude < 0.0001f)
            {
                right = Vector3.Cross(tangent, lastFrameRight).normalized;
                if (right.sqrMagnitude < 0.0001f)
                {
                    right = Vector3.right;
                }
            }
            else
            {
                right.Normalize();
            }

            lastFrameRight = right;
            Vector3 binormal = Vector3.Cross(right, tangent).normalized;

            float along = s / (float)(segmentCount - 1);
            float radius = Mathf.Lerp(startWidth, endWidth, along) * 0.5f;

            float wobble = Mathf.Sin((timeValue * 8f) + (along * 14f) + instanceSeed) * WobbleAmplitude;
            radius += wobble * (0.4f + intensity);

            Vector3 lateralWobble = right * (Mathf.Sin((timeValue * 6f) + (along * 11f) + instanceSeed) * WobbleAmplitude * 0.6f);
            Vector3 centerPoint = points[s] + lateralWobble;

            for (int r = 0; r < RadialSideCount; r++)
            {
                float angle = (Mathf.PI * 2f * r) / RadialSideCount;
                Vector3 offset = ((right * Mathf.Cos(angle)) + (binormal * Mathf.Sin(angle))) * radius;

                int vertexIndex = (s * RadialSideCount) + r;
                tubeVertices[vertexIndex] = centerPoint + offset;
                tubeUVs[vertexIndex] = new Vector2(r / (float)RadialSideCount, along * 4.5f);
            }
        }

        tubeMesh.vertices = tubeVertices;
        tubeMesh.uv = tubeUVs;
        tubeMesh.RecalculateNormals();
        tubeMesh.RecalculateBounds();
    }

    private void BuildTubeBuffers()
    {
        int vertexCount = ArcSegmentCount * RadialSideCount;
        int triangleCount = (ArcSegmentCount - 1) * RadialSideCount * 2;

        arcPoints = new Vector3[ArcSegmentCount];
        tubeVertices = new Vector3[vertexCount];
        tubeUVs = new Vector2[vertexCount];
        tubeTriangles = new int[triangleCount * 3];

        int triIndex = 0;
        for (int s = 0; s < ArcSegmentCount - 1; s++)
        {
            for (int r = 0; r < RadialSideCount; r++)
            {
                int rNext = (r + 1) % RadialSideCount;
                int v0 = (s * RadialSideCount) + r;
                int v1 = (s * RadialSideCount) + rNext;
                int v2 = ((s + 1) * RadialSideCount) + r;
                int v3 = ((s + 1) * RadialSideCount) + rNext;

                tubeTriangles[triIndex++] = v0;
                tubeTriangles[triIndex++] = v2;
                tubeTriangles[triIndex++] = v1;

                tubeTriangles[triIndex++] = v1;
                tubeTriangles[triIndex++] = v2;
                tubeTriangles[triIndex++] = v3;
            }
        }
    }

    private ParticleSystem BuildSplashSystem(HexSortMaterialLibrary materials)
    {
        GameObject splashObject = new GameObject("StreamSplash");
        splashObject.transform.SetParent(transform, false);
        RuntimeViewUtility.SetLayerRecursively(splashObject, IgnoreRaycastLayer);

        ParticleSystem system = splashObject.AddComponent<ParticleSystem>();
        system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = system.main;
        main.duration = 4f;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = 0.45f;
        main.startSize = 0.07f;
        main.startSpeed = 0f;
        main.startColor = Color.white;
        main.gravityModifier = 1.4f;
        main.maxParticles = 256;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;

        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = false;

        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = system.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve shrinkCurve = new AnimationCurve(
            new Keyframe(0f, 0.6f),
            new Keyframe(0.25f, 1f),
            new Keyframe(1f, 0f));
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, shrinkCurve);

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = system.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient fadeGradient = new Gradient();
        fadeGradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f),
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 0.55f),
                new GradientAlphaKey(0f, 1f),
            });
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(fadeGradient);

        ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = materials.CreateDropletMaterialInstance();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.alignment = ParticleSystemRenderSpace.View;

        splashEmitParams = new ParticleSystem.EmitParams
        {
            applyShapeToPosition = false,
        };

        system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        return system;
    }
}
