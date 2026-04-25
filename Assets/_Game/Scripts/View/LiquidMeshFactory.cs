using UnityEngine;

public static class LiquidMeshFactory
{
    public static Mesh BuildLiquidColumn(int radialSegments, int verticalSegments, float radius, float bottomLocalY, float topLocalY)
    {
        Mesh mesh = new Mesh
        {
            name = "HexSortLiquidColumn",
        };

        int sideRingCount = verticalSegments + 1;
        int sideVertCount = sideRingCount * radialSegments;
        int topRingCount = radialSegments;
        int bottomRingCount = radialSegments;
        int totalVertCount = sideVertCount + topRingCount + 1 + bottomRingCount + 1;

        Vector3[] vertices = new Vector3[totalVertCount];
        Vector3[] normals = new Vector3[totalVertCount];
        Vector2[] uvs = new Vector2[totalVertCount];

        int vertexIndex = 0;

        for (int ring = 0; ring < sideRingCount; ring++)
        {
            float verticalT = (sideRingCount > 1) ? (ring / (float)(sideRingCount - 1)) : 0f;
            float yLocal = Mathf.Lerp(bottomLocalY, topLocalY, verticalT);

            for (int radial = 0; radial < radialSegments; radial++)
            {
                float angle = (Mathf.PI * 2f * radial) / radialSegments;
                float cosA = Mathf.Cos(angle);
                float sinA = Mathf.Sin(angle);

                vertices[vertexIndex] = new Vector3(cosA * radius, yLocal, sinA * radius);
                normals[vertexIndex] = new Vector3(cosA, 0f, sinA);
                uvs[vertexIndex] = new Vector2(radial / (float)radialSegments, 0.05f + verticalT * 0.85f);
                vertexIndex++;
            }
        }

        int topRingStart = vertexIndex;
        for (int radial = 0; radial < topRingCount; radial++)
        {
            float angle = (Mathf.PI * 2f * radial) / topRingCount;
            float cosA = Mathf.Cos(angle);
            float sinA = Mathf.Sin(angle);

            vertices[vertexIndex] = new Vector3(cosA * radius, topLocalY, sinA * radius);
            normals[vertexIndex] = Vector3.up;
            uvs[vertexIndex] = new Vector2(radial / (float)topRingCount, 1f);
            vertexIndex++;
        }

        int topCenterIndex = vertexIndex;
        vertices[vertexIndex] = new Vector3(0f, topLocalY, 0f);
        normals[vertexIndex] = Vector3.up;
        uvs[vertexIndex] = new Vector2(0.5f, 1f);
        vertexIndex++;

        int bottomRingStart = vertexIndex;
        for (int radial = 0; radial < bottomRingCount; radial++)
        {
            float angle = (Mathf.PI * 2f * radial) / bottomRingCount;
            float cosA = Mathf.Cos(angle);
            float sinA = Mathf.Sin(angle);

            vertices[vertexIndex] = new Vector3(cosA * radius, bottomLocalY, sinA * radius);
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
        int topTriCount = topRingCount;
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

        for (int radial = 0; radial < topRingCount; radial++)
        {
            int radialNext = (radial + 1) % topRingCount;
            triangles[triIndex++] = topCenterIndex;
            triangles[triIndex++] = topRingStart + radial;
            triangles[triIndex++] = topRingStart + radialNext;
        }

        for (int radial = 0; radial < bottomRingCount; radial++)
        {
            int radialNext = (radial + 1) % bottomRingCount;
            triangles[triIndex++] = bottomCenterIndex;
            triangles[triIndex++] = bottomRingStart + radialNext;
            triangles[triIndex++] = bottomRingStart + radial;
        }

        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }
}
