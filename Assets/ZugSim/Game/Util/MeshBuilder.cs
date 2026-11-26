using System.Collections.Generic;
using UnityEngine;

namespace ZugSim.Game
{
    /// <summary>Hilfsklasse zum prozeduralen Erzeugen von Meshes (Boxen, Streifen, Kegel).</summary>
    public sealed class MeshBuilder
    {
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<int> tris = new List<int>();

        public int VertexCount { get { return verts.Count; } }

        /// <summary>
        /// Quad aus vier Eckpunkten (a,b,c,d fortlaufend um das Rechteck). <paramref name="outward"/> gibt die gewuenschte
        /// Sichtseite an; die Umlaufrichtung wird automatisch passend gewaehlt (Unity: im Uhrzeigersinn = Vorderseite).
        /// </summary>
        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector2 uvD)
        {
            int i = verts.Count;
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            bool flip = Vector3.Dot(n, outward) < 0f;
            if (flip) n = -n;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            normals.Add(n); normals.Add(n); normals.Add(n); normals.Add(n);
            uvs.Add(uvA); uvs.Add(uvB); uvs.Add(uvC); uvs.Add(uvD);
            if (!flip) { tris.Add(i); tris.Add(i + 1); tris.Add(i + 2); tris.Add(i); tris.Add(i + 2); tris.Add(i + 3); }
            else { tris.Add(i); tris.Add(i + 2); tris.Add(i + 1); tris.Add(i); tris.Add(i + 3); tris.Add(i + 2); }
        }

        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            AddQuad(a, b, c, d, outward, new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));
        }

        /// <summary>Quader mit Mittelpunkt, Rotation und Groesse (Breite X, Hoehe Y, Laenge Z).</summary>
        public void AddBox(Vector3 center, Quaternion rot, Vector3 size)
        {
            Vector3 h = size * 0.5f;
            Vector3 x = rot * new Vector3(h.x, 0, 0), y = rot * new Vector3(0, h.y, 0), z = rot * new Vector3(0, 0, h.z);
            Vector3 c = center;
            AddQuad(c + z - x - y, c + z - x + y, c + z + x + y, c + z + x - y, z);
            AddQuad(c - z - x - y, c - z - x + y, c - z + x + y, c - z + x - y, -z);
            AddQuad(c + x - z - y, c + x - z + y, c + x + z + y, c + x + z - y, x);
            AddQuad(c - x - z - y, c - x - z + y, c - x + z + y, c - x + z - y, -x);
            AddQuad(c + y - x - z, c + y - x + z, c + y + x + z, c + y + x - z, y);
            AddQuad(c - y - x - z, c - y - x + z, c - y + x + z, c - y + x - z, -y);
        }

        /// <summary>Pyramide (4 Seiten), Basis unten.</summary>
        public void AddPyramid(Vector3 baseCenter, float radius, float height)
        {
            Vector3 apex = baseCenter + Vector3.up * height;
            Vector3 p0 = baseCenter + new Vector3(-radius, 0, -radius);
            Vector3 p1 = baseCenter + new Vector3(-radius, 0, radius);
            Vector3 p2 = baseCenter + new Vector3(radius, 0, radius);
            Vector3 p3 = baseCenter + new Vector3(radius, 0, -radius);
            Vector3 mid = baseCenter + Vector3.up * (height * 0.3f);
            AddTri(p1, apex, p2, mid); AddTri(p2, apex, p3, mid); AddTri(p3, apex, p0, mid); AddTri(p0, apex, p1, mid);
        }

        void AddTri(Vector3 a, Vector3 b, Vector3 c, Vector3 inside)
        {
            int i = verts.Count;
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            Vector3 centroid = (a + b + c) / 3f;
            bool flip = Vector3.Dot(n, centroid - inside) < 0f;
            if (flip) n = -n;
            verts.Add(a); verts.Add(b); verts.Add(c);
            normals.Add(n); normals.Add(n); normals.Add(n);
            uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(0.5f, 1)); uvs.Add(new Vector2(1, 0));
            if (!flip) { tris.Add(i); tris.Add(i + 1); tris.Add(i + 2); }
            else { tris.Add(i); tris.Add(i + 2); tris.Add(i + 1); }
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            if (verts.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(verts);
            m.SetNormals(normals);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        public void Clear() { verts.Clear(); normals.Clear(); uvs.Clear(); tris.Clear(); }
    }
}
