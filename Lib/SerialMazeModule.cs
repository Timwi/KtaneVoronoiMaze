using System;
using System.Collections.Generic;
using System.Linq;
using RT.KitchenSink.Geometry;
using RT.Util.ExtensionMethods;
using RT.Util.Geometry;
using UnityEngine;

using Rnd = UnityEngine.Random;

public class SerialMazeModule : MonoBehaviour
{
    public KMBombInfo Bomb;
    public KMBombModule Module;
    public KMSelectable ModuleSelectable;

    public MeshRenderer LineTemplate;
    public Material RedLine;

    public TextMesh TextTemplate;
    public KMSelectable SelectableTemplate;

    private static int _moduleIdCounter = 1;
    private int _moduleId;

    void Awake()
    {
        _moduleId = _moduleIdCounter++;

        VoronoiDiagram bestVoronoi = null;
        double bestVoronoiLength = 0;
        PointD[] bestPoints = null;
        var rnd = new System.Random(Rnd.Range(0, int.MaxValue));

        var slc = new PointD(.975, .975);
        var slr = .225;

        for (var iter = 0; iter < 100; iter++)
        {
            var points = new List<PointD>();
            while (points.Count < 15)
            {
                var newPoint = new PointD(rnd.NextDouble(), rnd.NextDouble());
                if (points.Any(p => p.Distance(newPoint) < .01))
                    continue;
                points.Add(newPoint);
            }
            var voronoi = VoronoiDiagram.GenerateVoronoiDiagram(points.ToArray(), 1, 1, VoronoiDiagramFlags.IncludeEdgePolygons);
            var shortestEdge = voronoi.Edges.Min(e => e.start.Distance(e.end));
            if (shortestEdge > bestVoronoiLength && !voronoi.Edges.Any(edge =>
                Enumerable.Range(0, 9).Any(cut => ((cut / 8.0) * edge.start + (1 - cut / 8.0) * edge.end).Distance(slc) < slr)))
            {
                bestVoronoiLength = shortestEdge;
                bestVoronoi = voronoi;
                bestPoints = points.ToArray();
            }
        }

        foreach (var (start, end, siteA, siteB) in bestVoronoi.Edges)
        {
            var midPoint = convertPoint((start + end) / 2);
            var length = start.Distance(end) * .08 / .5;
            var line = Instantiate(LineTemplate, transform);
            line.gameObject.name = $"Edge {siteA} → {siteB}";
            line.transform.localPosition = new Vector3((float) midPoint.X, .01501f, (float) midPoint.Y);
            line.transform.localEulerAngles = new Vector3(90, (float) (90 - Math.Atan2(end.Y - start.Y, end.X - start.X) * 180 / Math.PI), 0);
            line.transform.localScale = new Vector3(.0014f, (float) length, .1f);
        }

        var children = new List<KMSelectable>();
        for (var pointIx = 0; pointIx < bestPoints.Length; pointIx++)
        {
            var polygon = bestVoronoi.Polygons[pointIx];
            var labelPoint = polygon == null ? bestPoints[pointIx] : polygon.GetLabelPoint(.005);

            if (Application.isEditor && false)
            {
                var label = Instantiate(TextTemplate, transform);
                label.gameObject.name = $"Label {(char) ('A' + pointIx)}";
                label.transform.localPosition = convertPointToVector(labelPoint, .0152f);
                label.text = $"{pointIx + 1}";
            }

            var selectable = Instantiate(SelectableTemplate, transform);

            var colliderMeshTris = new List<Vector3>();
            for (int j = 0, i = polygon.Vertices.Count - 1; j < polygon.Vertices.Count; i = j++)
            {
                colliderMeshTris.Add(convertPointToVector(polygon.Vertices[i], .0151f));
                colliderMeshTris.Add(convertPointToVector(polygon.Vertices[i], .02f));
                colliderMeshTris.Add(convertPointToVector(polygon.Vertices[j], .02f));

                colliderMeshTris.Add(convertPointToVector(polygon.Vertices[j], .02f));
                colliderMeshTris.Add(convertPointToVector(polygon.Vertices[j], .0151f));
                colliderMeshTris.Add(convertPointToVector(polygon.Vertices[i], .0151f));

                if (i != 0 && j != 0)
                {
                    colliderMeshTris.Add(convertPointToVector(polygon.Vertices[0], .02f));
                    colliderMeshTris.Add(convertPointToVector(polygon.Vertices[j], .02f));
                    colliderMeshTris.Add(convertPointToVector(polygon.Vertices[i], .02f));

                    colliderMeshTris.Add(convertPointToVector(polygon.Vertices[0], .0151f));
                    colliderMeshTris.Add(convertPointToVector(polygon.Vertices[i], .0151f));
                    colliderMeshTris.Add(convertPointToVector(polygon.Vertices[j], .0151f));
                }
            }
            var colliderMesh = new Mesh();
            colliderMesh.vertices = colliderMeshTris.ToArray();
            colliderMesh.triangles = Enumerable.Range(0, colliderMeshTris.Count).ToArray();
            colliderMesh.normals = Enumerable.Repeat(new Vector3(0, 1, 0), colliderMeshTris.Count).ToArray();
            selectable.gameObject.GetComponent<MeshCollider>().sharedMesh = colliderMesh;

            var highlightMeshTris = new List<Vector3>();
            foreach (var (rStart, rEnd, siteA, siteB) in bestVoronoi.Edges.Where(e => e.siteA == pointIx || e.siteB == pointIx))
            {
                // Figure out whether our site is “left” of the line or not
                var vA = bestPoints[siteA] - rStart;
                var vM = rEnd - rStart;
                var angleToA = Math.Atan2(vA.Y * vM.X - vA.X * vM.Y, vA.X * vM.X + vA.Y * vM.Y);
                var (start, end) = (angleToA < 0 ^ siteA == pointIx) ? (rEnd, rStart) : (rStart, rEnd);

                var mid = (start + end) / 2;
                PointD n(PointD p, double len) => p / p.Distance() * len;
                var p1 = n((end - start).Rotated(Math.PI * 2 / 5), .05) + mid;
                var p2 = n((end - start).Rotated(Math.PI * 3 / 5), .05) + mid;
                var m = n((end - start).Rotated(Math.PI / 2), .005) + mid;
                highlightMeshTris.Add(convertPointToVector(m, 0));
                highlightMeshTris.Add(convertPointToVector(p2, 0));
                highlightMeshTris.Add(convertPointToVector(p1, 0));
            }
            var highlightMesh = new Mesh();
            highlightMesh.vertices = highlightMeshTris.ToArray();
            highlightMesh.triangles = Enumerable.Range(0, highlightMeshTris.Count).ToArray();
            highlightMesh.normals = Enumerable.Repeat(new Vector3(0, 1, 0), highlightMeshTris.Count).ToArray();

            selectable.Highlight.GetComponent<MeshFilter>().sharedMesh = highlightMesh;

            children.Add(selectable);
        }

        Destroy(LineTemplate.gameObject);
        Destroy(TextTemplate.gameObject);
        Destroy(SelectableTemplate.gameObject);

        ModuleSelectable.Children = children.ToArray();
        ModuleSelectable.UpdateChildren();
    }

    PointD convertPoint(PointD orig) => (orig - new PointD(.5, .5)) * .08 / .5;
    Vector3 convertPointToVector(PointD orig, float y = .0151f)
    {
        var p = convertPoint(orig);
        return new Vector3((float) p.X, y, (float) p.Y);
    }
}
