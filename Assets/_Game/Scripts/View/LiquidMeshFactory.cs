using UnityEngine;

public static class LiquidMeshFactory
{
    public static Mesh BuildLiquidColumn(int radialSegments, int verticalSegments, float bottomRadius, float topRadius, float bottomLocalY, float topLocalY, bool includeTopCap = true)
    {
        Mesh mesh = new Mesh
        {
            name = "HexSortLiquidColumn",
        };

        int sideRingCount = verticalSegments + 1;
        int sideVertCount = sideRingCount * radialSegments;
        int topRingCount = includeTopCap ? radialSegments : 0;
        int topCapCenterCount = includeTopCap ? 1 : 0;
        int bottomRingCount = radialSegments;
        int totalVertCount = sideVertCount + topRingCount + topCapCenterCount + bottomRingCount + 1;

        Vector3[] vertices = new Vector3[totalVertCount];
        Vector3[] normals = new Vector3[totalVertCount];
        Vector2[] uvs = new Vector2[totalVertCount];

        int vertexIndex = 0;

        for (int ring = 0; ring < sideRingCount; ring++)
        {
            float verticalT = (sideRingCount > 1) ? (ring / (float)(sideRingCount - 1)) : 0f;
            float yLocal = Mathf.Lerp(bottomLocalY, topLocalY, verticalT);
            float ringRadius = Mathf.Lerp(bottomRadius, topRadius, verticalT);

            for (int radial = 0; radial < radialSegments; radial++)
            {
                float angle = (Mathf.PI * 2f * radial) / radialSegments;
                float cosA = Mathf.Cos(angle);
                float sinA = Mathf.Sin(angle);

                vertices[vertexIndex] = new Vector3(cosA * ringRadius, yLocal, sinA * ringRadius);
                normals[vertexIndex] = new Vector3(cosA, 0f, sinA);
                uvs[vertexIndex] = new Vector2(radial / (float)radialSegments, 0.05f + verticalT * 0.85f);
                vertexIndex++;
            }
        }

        int topRingStart = vertexIndex;
        int topCenterIndex = -1;
        if (includeTopCap)
        {
            for (int radial = 0; radial < topRingCount; radial++)
            {
                float angle = (Mathf.PI * 2f * radial) / topRingCount;
                float cosA = Mathf.Cos(angle);
                float sinA = Mathf.Sin(angle);

                vertices[vertexIndex] = new Vector3(cosA * topRadius, topLocalY, sinA * topRadius);
                normals[vertexIndex] = Vector3.up;
                uvs[vertexIndex] = new Vector2(radial / (float)topRingCount, 1f);
                vertexIndex++;
            }

            topCenterIndex = vertexIndex;
            vertices[vertexIndex] = new Vector3(0f, topLocalY, 0f);
            normals[vertexIndex] = Vector3.up;
            uvs[vertexIndex] = new Vector2(0.5f, 1f);
            vertexIndex++;
        }

        int bottomRingStart = vertexIndex;
        for (int radial = 0; radial < bottomRingCount; radial++)
        {
            float angle = (Mathf.PI * 2f * radial) / bottomRingCount;
            float cosA = Mathf.Cos(angle);
            float sinA = Mathf.Sin(angle);

            vertices[vertexIndex] = new Vector3(cosA * bottomRadius, bottomLocalY, sinA * bottomRadius);
            normals[vertexIndex] = Vector3.down;
            uvs[vertexIndex] = new Vector2(radial / (float)bottomRingCount, 0f);
            vertexIndex++;
        }

        int bottomCenterIndex = vertexIndex;
        vertices[vertexIndex] = new Vector3(0f, bottomLocalY, 0f);
        normals[vertexIndex] = Vector3.down;
        uvs[vertexIndex] = new Vector2(0.5f, 0f);
        vertexIndex++;

        int sideTriCount = verticalSegments * radialSegments * 2;
        int topTriCount = includeTopCap ? topRingCount : 0;
        int bottomTriCount = bottomRingCount;
        int[] triangles = new int[(sideTriCount + topTriCount + bottomTriCount) * 3];
        int triIndex = 0;

        for (int ring = 0; ring < verticalSegments; ring++)
        {
            for (int radial = 0; radial < radialSegments; radial++)
            {
                int radialNext = (radial + 1) % radialSegments;
                int v0 = (ring * radialSegments) + radial;
                int v1 = (ring * radialSegments) + radialNext;
                int v2 = ((ring + 1) * radialSegments) + radial;
                int v3 = ((ring + 1) * radialSegments) + radialNext;

                triangles[triIndex++] = v0;
                triangles[triIndex++] = v2;
                triangles[triIndex++] = v1;
                triangles[triIndex++] = v1;
                triangles[triIndex++] = v2;
                triangles[triIndex++] = v3;
            }
        }

        if (includeTopCap)
        {
            for (int radial = 0; radial < topRingCount; radial++)
            {
                int radialNext = (radial + 1) % topRingCount;
                triangles[triIndex++] = topCenterIndex;
                triangles[triIndex++] = topRingStart + radialNext;
                triangles[triIndex++] = topRingStart + radial;
            }
        }

        for (int radial = 0; radial < bottomRingCount; radial++)
        {
            int radialNext = (radial + 1) % bottomRingCount;
            triangles[triIndex++] = bottomCenterIndex;
            triangles[triIndex++] = bottomRingStart + radial;
            triangles[triIndex++] = bottomRingStart + radialNext;
        }

        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    public static Mesh BuildLiquidSurface(int radialSegments, int radialRings = 6)
    {
        Mesh mesh = new Mesh
        {
            name = "HexSortLiquidSurface",
        };

        int ringSegments = Mathf.Max(8, radialSegments);
        int rings = Mathf.Max(1, radialRings);

        // 1 centre vertex + (rings * ringSegments) vertices on concentric rings.
        int vertCount = 1 + (rings * ringSegments);
        Vector3[] vertices = new Vector3[vertCount];
        Vector3[] normals = new Vector3[vertCount];
        Vector2[] uvs = new Vector2[vertCount];

        int centerIdx = 0;
        vertices[centerIdx] = Vector3.zero;
        normals[centerIdx] = Vector3.up;
        uvs[centerIdx] = new Vector2(0.5f, 1f);

        for (int r = 0; r < rings; r++)
        {
            float radius = (r + 1) / (float)rings;
            for (int i = 0; i < ringSegments; i++)
            {
                int idx = 1 + (r * ringSegments) + i;
                float angle = (Mathf.PI * 2f * i) / ringSegments;
                vertices[idx] = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                normals[idx] = Vector3.up;
                uvs[idx] = new Vector2(i / (float)ringSegments, 1f);
            }
        }

        // Innermost fan (centre to ring 0) + concentric strips between rings.
        int innerFanTris = ringSegments;
        int stripTris = (rings - 1) * ringSegments * 2;
        int[] triangles = new int[(innerFanTris + stripTris) * 3];
        int triIdx = 0;

        // Centre fan to ring 0.
        for (int i = 0; i < ringSegments; i++)
        {
            int v0 = 1 + i;
            int v1 = 1 + ((i + 1) % ringSegments);
            triangles[triIdx++] = centerIdx;
            triangles[triIdx++] = v1;
            triangles[triIdx++] = v0;
        }

        // Strip between consecutive rings.
        for (int r = 0; r < rings - 1; r++)
        {
            int innerStart = 1 + (r * ringSegments);
            int outerStart = 1 + ((r + 1) * ringSegments);
            for (int i = 0; i < ringSegments; i++)
            {
                int next = (i + 1) % ringSegments;
                int v0 = innerStart + i;
                int v1 = innerStart + next;
                int v2 = outerStart + i;
                int v3 = outerStart + next;

                triangles[triIdx++] = v0;
                triangles[triIdx++] = v3;
                triangles[triIdx++] = v1;

                triangles[triIdx++] = v0;
                triangles[triIdx++] = v2;
                triangles[triIdx++] = v3;
            }
        }

        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }
}
