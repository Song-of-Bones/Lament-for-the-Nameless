using System;
using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lament.SceneGraph
{
    /// <summary>
    /// Custom edge drawing: instead of the built-in spline, every connection is routed from a small
    /// set of canonical shapes (horizontal / vertical / 45° only) and the best one is picked by
    /// scoring length, bends, card overlap and how closely it hugs lines that are already routed.
    /// Few bends win, cards are avoided, and another line is crossed when going around costs more.
    /// </summary>
    public static class SceneGraphEdgeRouter
    {
        private const float ArrowLength = 10f;
        private const float ArrowHalfWidth = 4.5f;
        private const float LineWidth = 2f;
        private const float PortClearance = 18f;
        private const float HitThreshold = 9f;

        // Scoring weights (length is in pixels).
        private const float BendPenalty = 45f;
        private const float CardPenalty = 6000f;
        private const float ParallelDistance = 10f;
        private const float ParallelWeight = 12f;
        private const float CrossingPenalty = 260f;
        private const float CollinearTolerance = 1.2f;
        private const float LaneGap = 26f;
        private const int MaxLaneCandidates = 10;

        private static readonly Dictionary<Edge, List<Vector2>> Routes =
            new Dictionary<Edge, List<Vector2>>();

        private static readonly Dictionary<Edge, EdgeHitArea> HitAreas =
            new Dictionary<Edge, EdgeHitArea>();

        public static void Reset()
        {
            Routes.Clear();
            HitAreas.Clear();
        }

        /// <summary>缩放/平移后强制重画所有连线（网格不会自动跟着视图变换重绘）。</summary>
        public static void RepaintAll(IReadOnlyList<Edge> edges)
        {
            if (edges == null) return;
            for (int i = 0; i < edges.Count; i++)
                edges[i]?.edgeControl?.MarkDirtyRepaint();
        }

        /// <summary>Distance from a graph-content point to an edge's route; float.MaxValue when unrouted.</summary>
        public static float DistanceToRoute(Edge edge, Vector2 contentPoint)
        {
            if (edge == null || !Routes.TryGetValue(edge, out var route) || route.Count < 2)
                return float.MaxValue;

            float best = float.MaxValue;
            for (int i = 0; i < route.Count - 1; i++)
                best = Mathf.Min(best, DistanceToSegment(contentPoint, route[i], route[i + 1]));
            return best;
        }

        /// <summary>Applies color, replaces the built-in spline drawing and installs the hit area.</summary>
        /// <param name="registerMenu">Called once with the element that should own the edge menu.</param>
        public static void Apply(Edge edge, Color color, Action<VisualElement> registerMenu = null)
        {
            if (edge == null) return;
            var control = edge.edgeControl;
            if (control == null) return;

            control.inputColor = color;
            control.outputColor = color;
            control.edgeWidth = Mathf.RoundToInt(LineWidth);
            control.capRadius = 0f;
            control.drawFromCap = false;
            control.drawToCap = false;
            control.interceptWidth = 18f;
            // Routes can leave the element's own rect; the built-in spline never did.
            control.style.overflow = Overflow.Visible;

            // Assigning replaces the built-in spline drawing for this edge (VisualElement.generateVisualContent).
            control.generateVisualContent = context => Generate(context, edge, control, color);

            var hit = EnsureHitArea(edge, control);
            if (hit != null) registerMenu?.Invoke(hit);
        }

        /// <summary>Routes every edge in one pass, so later edges can steer clear of earlier ones.</summary>
        public static void RouteAll(IReadOnlyList<Edge> edges, IReadOnlyList<Rect> nodeRects)
        {
            Routes.Clear();
            if (edges == null || edges.Count == 0) return;

            var obstacles = new List<Rect>();
            if (nodeRects != null)
            {
                for (int i = 0; i < nodeRects.Count; i++)
                {
                    Rect rect = nodeRects[i];
                    if (rect.width > 1f && rect.height > 1f) obstacles.Add(Inflate(rect, 4f));
                }
            }

            var placed = new List<List<Vector2>>();

            for (int i = 0; i < edges.Count; i++)
            {
                var edge = edges[i];
                var control = edge != null ? edge.edgeControl : null;
                if (control == null) continue;

                Vector2 from = control.from;
                Vector2 to = control.to;
                if (float.IsNaN(from.x) || float.IsNaN(from.y) || float.IsNaN(to.x) || float.IsNaN(to.y))
                    continue;

                Rect sourceRect = NodeRect(edge.output, from);
                Rect targetRect = NodeRect(edge.input, to);

                var route = ChooseRoute(from, to, sourceRect, targetRect, obstacles, placed);
                if (route == null) continue;

                Routes[edge] = route;
                placed.Add(route);
            }

            RefreshHitAreas(edges);
        }

        /// <summary>
        /// Puts each edge's clickable band at the bounding box of its route. The band lives next to
        /// the edge, not inside the EdgeControl, because a detour leaves the EdgeControl's rect and
        /// picking is clipped to the element's own rectangle.
        /// </summary>
        private static void RefreshHitAreas(IReadOnlyList<Edge> edges)
        {
            for (int i = 0; i < edges.Count; i++)
            {
                var edge = edges[i];
                if (edge == null || !HitAreas.TryGetValue(edge, out var area) || area.parent == null) continue;

                if (!Routes.TryGetValue(edge, out var route) || route.Count < 2)
                {
                    area.style.display = DisplayStyle.None;
                    continue;
                }

                float pad = HitThreshold + 4f;
                float minX = float.MaxValue, minY = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue;

                foreach (var point in route)
                {
                    minX = Mathf.Min(minX, point.x);
                    minY = Mathf.Min(minY, point.y);
                    maxX = Mathf.Max(maxX, point.x);
                    maxY = Mathf.Max(maxY, point.y);
                }

                minX -= pad; minY -= pad; maxX += pad; maxY += pad;

                area.style.display = DisplayStyle.Flex;
                area.style.left = minX;
                area.style.top = minY;
                area.style.width = Mathf.Max(1f, maxX - minX);
                area.style.height = Mathf.Max(1f, maxY - minY);
                area.Origin = new Vector2(minX, minY);
            }
        }

        // ---- route candidates ------------------------------------------------------------

        private static List<Vector2> ChooseRoute(
            Vector2 from, Vector2 to, Rect sourceRect, Rect targetRect,
            List<Rect> obstacles, List<List<Vector2>> placed)
        {
            // Ports sit inside their card; the routed part starts and ends just outside them.
            var exitPoint = new Vector2(sourceRect.xMax + PortClearance, from.y);
            var enterPoint = new Vector2(targetRect.xMin - PortClearance, to.y);

            var candidates = new List<List<Vector2>>(10);

            float minX = Mathf.Min(exitPoint.x, enterPoint.x);
            float maxX = Mathf.Max(exitPoint.x, enterPoint.x);

            float[] lanes =
            {
                (exitPoint.x + enterPoint.x) * 0.5f,
                minX + 30f,
                maxX - 30f,
                sourceRect.xMax + 36f,
                targetRect.xMin - 36f,
                minX + 90f,
                maxX - 90f
            };

            foreach (float laneX in lanes)
            {
                candidates.Add(Clean(new List<Vector2>
                {
                    from, exitPoint,
                    new Vector2(laneX, exitPoint.y),
                    new Vector2(laneX, enterPoint.y),
                    enterPoint, to
                }));
            }

            candidates.Add(Clean(BuildDiagonal(from, to, exitPoint, enterPoint)));

            foreach (float laneY in CollectLanes(obstacles))
                candidates.Add(Clean(BuildLane(from, to, exitPoint, enterPoint, laneY)));

            List<Vector2> best = null;
            float bestScore = float.MaxValue;

            foreach (var candidate in candidates)
            {
                if (candidate == null || candidate.Count < 2) continue;
                if (CrossesAnyCard(candidate, obstacles)) continue;   // 线绝对不穿卡片

                float score = Score(candidate, obstacles, placed);
                if (score >= bestScore) continue;
                bestScore = score;
                best = candidate;
            }

            // 所有候选都被卡片挡住时，退回"贴着全场上方走"的通道，它一定不会穿卡片。
            if (best == null)
            {
                float above = LaneAbove(obstacles);
                best = Clean(BuildLane(from, to, exitPoint, enterPoint, above));
            }

            return best;
        }

        /// <summary>连线（除两端端口所在的卡片外）不得进入任何卡片矩形。</summary>
        private static bool CrossesAnyCard(List<Vector2> points, List<Rect> obstacles)
        {
            if (points.Count < 2) return false;

            Vector2 start = points[0];
            Vector2 end = points[points.Count - 1];

            for (int i = 0; i < points.Count - 1; i++)
            {
                foreach (var rect in obstacles)
                {
                    // 端口本来就在自己的卡片里，这两张卡要排除，否则每条线都会被判成“穿卡”。
                    if (rect.Contains(start) || rect.Contains(end)) continue;
                    if (SegmentIntersectsRect(points[i], points[i + 1], rect)) return true;
                }
            }

            return false;
        }

        /// <summary>Two segments: leave the card, one 45° run, then straight into the target.</summary>
        private static List<Vector2> BuildDiagonal(Vector2 from, Vector2 to, Vector2 exitPoint, Vector2 enterPoint)
        {
            var points = new List<Vector2> { from, exitPoint };

            float dx = enterPoint.x - exitPoint.x;
            float dy = enterPoint.y - exitPoint.y;
            float ady = Mathf.Abs(dy);

            if (dx > 1f && ady <= dx)
                points.Add(new Vector2(exitPoint.x + ady, enterPoint.y));
            else if (dx > 1f)
            {
                float sign = dy >= 0f ? 1f : -1f;
                points.Add(new Vector2(exitPoint.x + dx, exitPoint.y + sign * dx));
                points.Add(enterPoint);
            }
            else points.Add(new Vector2(exitPoint.x, enterPoint.y));

            points.Add(enterPoint);
            points.Add(to);
            return points;
        }

        /// <summary>Leaves the card, runs along a horizontal lane that clears every card, comes back.</summary>
        private static List<Vector2> BuildLane(
            Vector2 from, Vector2 to, Vector2 exitPoint, Vector2 enterPoint, float laneY)
        {
            return new List<Vector2>
            {
                from, exitPoint,
                new Vector2(exitPoint.x, laneY),
                new Vector2(enterPoint.x, laneY),
                new Vector2(enterPoint.x, enterPoint.y),
                enterPoint, to
            };
        }

        private static float LaneAbove(List<Rect> obstacles)
        {
            float top = float.MaxValue;
            foreach (var rect in obstacles) top = Mathf.Min(top, rect.yMin);
            return top == float.MaxValue ? 0f : top - LaneGap;
        }

        private static float LaneBelow(List<Rect> obstacles)
        {
            float bottom = float.MinValue;
            foreach (var rect in obstacles) bottom = Mathf.Max(bottom, rect.yMax);
            return bottom == float.MinValue ? 0f : bottom + LaneGap;
        }

        /// <summary>
        /// Candidate detour lanes: just outside every card plus the global top and bottom, so a
        /// line can slip past a single card instead of running all the way around the graph.
        /// </summary>
        private static List<float> CollectLanes(List<Rect> obstacles)
        {
            var lanes = new List<float> { LaneAbove(obstacles), LaneBelow(obstacles) };

            foreach (var rect in obstacles)
            {
                lanes.Add(rect.yMin - LaneGap);
                lanes.Add(rect.yMax + LaneGap);
            }

            lanes.Sort();

            var result = new List<float>(lanes.Count);
            float previous = float.NaN;
            foreach (float lane in lanes)
            {
                if (!float.IsNaN(previous) && Mathf.Abs(lane - previous) < ParallelDistance * 1.5f) continue;
                result.Add(lane);
                previous = lane;
                if (result.Count >= MaxLaneCandidates) break;
            }

            return result;
        }

        // ---- scoring ---------------------------------------------------------------------

        private static float Score(List<Vector2> points, List<Rect> obstacles, List<List<Vector2>> placed)
        {
            float score = 0f;

            for (int i = 0; i < points.Count - 1; i++)
            {
                score += Vector2.Distance(points[i], points[i + 1]);
                if (i < points.Count - 2) score += BendPenalty;
            }

            for (int i = 0; i < points.Count - 1; i++)
            {
                foreach (var rect in obstacles)
                {
                    if (!SegmentIntersectsRect(points[i], points[i + 1], rect)) continue;
                    score += CardPenalty;
                    break;
                }
            }

            for (int i = 0; i < points.Count - 1; i++)
            {
                foreach (var other in placed)
                {
                    for (int j = 0; j < other.Count - 1; j++)
                    {
                        float overlap = ParallelOverlap(points[i], points[i + 1], other[j], other[j + 1]);
                        if (overlap > 1f) score += overlap * ParallelWeight;
                        else if (SegmentsCross(points[i], points[i + 1], other[j], other[j + 1]))
                            score += CrossingPenalty;
                    }
                }
            }

            return score;
        }

        /// <summary>Length of the stretch where two segments run side by side (within ParallelDistance).</summary>
        private static float ParallelOverlap(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2)
        {
            Vector2 da = a2 - a1;
            Vector2 db = b2 - b1;
            if (da.sqrMagnitude < 0.01f || db.sqrMagnitude < 0.01f) return 0f;

            da.Normalize();
            db.Normalize();
            if (Mathf.Abs(Vector2.Dot(da, db)) < 0.985f) return 0f;

            float distance = Mathf.Abs((b1.x - a1.x) * da.y - (b1.y - a1.y) * da.x);
            if (distance > ParallelDistance) return 0f;

            float length = (a2 - a1).magnitude;
            float bMin = Mathf.Min(Vector2.Dot(b1 - a1, da), Vector2.Dot(b2 - a1, da));
            float bMax = Mathf.Max(Vector2.Dot(b1 - a1, da), Vector2.Dot(b2 - a1, da));

            return Mathf.Max(0f, Mathf.Min(bMax, length) - Mathf.Max(bMin, 0f));
        }

        private static bool SegmentsCross(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2)
        {
            float d1 = Cross(b2 - b1, a1 - b1);
            float d2 = Cross(b2 - b1, a2 - b1);
            float d3 = Cross(a2 - a1, b1 - a1);
            float d4 = Cross(a2 - a1, b2 - a1);
            return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) &&
                   ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static bool SegmentIntersectsRect(Vector2 a, Vector2 b, Rect rect)
        {
            if (rect.Contains(a) || rect.Contains(b)) return true;

            var min = rect.min;
            var max = rect.max;
            var bl = new Vector2(min.x, max.y);
            var tr = new Vector2(max.x, min.y);

            return SegmentsCross(a, b, min, bl) || SegmentsCross(a, b, bl, max) ||
                   SegmentsCross(a, b, max, tr) || SegmentsCross(a, b, tr, min);
        }

        // ---- drawing ---------------------------------------------------------------------

        private static void Generate(
            MeshGenerationContext context, Edge edge, EdgeControl control, Color baseColor)
        {
            if (edge == null || control == null) return;

            // EdgeControl.from/to live in the graph content space; mesh generation draws in the
            // element's own space, so shift by the element's layout origin.
            Vector2 from = control.from;
            Vector2 to = control.to;
            if (float.IsNaN(from.x) || float.IsNaN(from.y) || float.IsNaN(to.x) || float.IsNaN(to.y))
                return;

            var points = LocalPoints(control, edge);
            if (points == null || points.Count < 2) return;

            Color color = edge.selected ? Color.Lerp(baseColor, Color.white, 0.5f) : baseColor;

            var painter = context.painter2D;
            painter.strokeColor = color;
            painter.fillColor = color;
            painter.lineWidth = edge.selected ? LineWidth + 1f : LineWidth;
            painter.lineJoin = LineJoin.Round;
            painter.lineCap = LineCap.Round;

            painter.BeginPath();
            painter.MoveTo(points[0]);
            for (int i = 1; i < points.Count; i++)
                painter.LineTo(points[i]);
            painter.Stroke();

            DrawArrow(painter, points[points.Count - 2], points[points.Count - 1]);
        }

        /// <summary>内容坐标 → EdgeControl 局部坐标；走完整变换链，缩放/平移后依然对得上。</summary>
        private static Vector2 ToLocal(EdgeControl control, Vector2 contentPoint)
        {
            var parent = control != null ? control.parent : null;
            if (parent == null) return contentPoint - (control != null ? (Vector2)control.layout.position : Vector2.zero);
            return control.WorldToLocal(parent.LocalToWorld(contentPoint));
        }

        /// <summary>路线（内容坐标 → 局部坐标），没有路线时按端口位置生成兜底折线。</summary>
        private static List<Vector2> LocalPoints(EdgeControl control, Edge edge)
        {
            var points = new List<Vector2>(10);

            if (Routes.TryGetValue(edge, out var route) && route.Count >= 2)
            {
                for (int i = 0; i < route.Count; i++) AddPoint(points, ToLocal(control, route[i]));
                return points;
            }

            Vector2 from = ToLocal(control, control.from);
            Vector2 to = ToLocal(control, control.to);
            if (float.IsNaN(from.x) || float.IsNaN(from.y) || float.IsNaN(to.x) || float.IsNaN(to.y))
                return points;

            FallbackRoute(points, from, to);
            return points;
        }

        /// <summary>Simple two-bend route used until the routing pass has run (and if it fails).</summary>
        private static void FallbackRoute(List<Vector2> points, Vector2 from, Vector2 to)
        {
            float laneX = (from.x + to.x) * 0.5f;

            AddPoint(points, from);
            AddPoint(points, new Vector2(laneX, from.y));
            AddPoint(points, new Vector2(laneX, to.y));
            AddPoint(points, to);
        }

        private static void DrawArrow(Painter2D painter, Vector2 from, Vector2 to)
        {
            Vector2 direction = to - from;
            if (direction.sqrMagnitude < 0.0001f) return;
            direction.Normalize();

            Vector2 normal = new Vector2(-direction.y, direction.x);
            Vector2 back = to - direction * ArrowLength;

            painter.BeginPath();
            painter.MoveTo(to);
            painter.LineTo(back + normal * ArrowHalfWidth);
            painter.LineTo(back - normal * ArrowHalfWidth);
            painter.ClosePath();
            painter.Fill();
        }

        // ---- hit area so right-clicking the drawn line works ---------------------------

        private sealed class EdgeHitArea : VisualElement
        {
            public Edge Edge;
            public Vector2 Origin;

            public override bool ContainsPoint(Vector2 localPoint)
            {
                var control = Edge != null ? Edge.edgeControl : null;
                if (control == null) return false;

                // 命中区是 edge 容器的子元素，localPoint + Origin 就是内容坐标；
                // 路线也存的是内容坐标，两边同一空间，缩放不会影响判定。
                Vector2 point = localPoint + Origin;

                if (Routes.TryGetValue(Edge, out var route) && route.Count >= 2)
                {
                    for (int i = 0; i < route.Count - 1; i++)
                        if (DistanceToSegment(point, route[i], route[i + 1]) <= HitThreshold)
                            return true;
                    return false;
                }

                float scale = 1f;
                var parent = control.parent;
                if (parent != null)
                {
                    Vector3 s = parent.worldTransform.lossyScale;
                    scale = Mathf.Max(0.0001f, s.x);
                }

                var local = LocalPoints(control, Edge);
                for (int i = 0; i < local.Count - 1; i++)
                    if (DistanceToSegment(localPoint, local[i], local[i + 1]) <= HitThreshold / scale)
                        return true;

                return false;
            }
        }

        private static EdgeHitArea EnsureHitArea(Edge edge, EdgeControl control)
        {
            if (HitAreas.TryGetValue(edge, out var existing) && existing.parent != null) return existing;

            var host = edge.parent;
            if (host == null) return null;

            var area = new EdgeHitArea
            {
                Edge = edge,
                name = "edge-hit-area",
                pickingMode = PickingMode.Position,
                style = { position = Position.Absolute }
            };
            area.style.display = DisplayStyle.None;
            host.Add(area);
            HitAreas[edge] = area;
            return area;
        }

        private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float length = ab.sqrMagnitude;
            if (length < 0.0001f) return Vector2.Distance(point, a);

            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / length);
            return Vector2.Distance(point, a + ab * t);
        }

        // ---- helpers ---------------------------------------------------------------------

        private static List<Vector2> Clean(List<Vector2> points)
        {
            var result = new List<Vector2>(points.Count);
            foreach (var point in points) AddPoint(result, point);

            Simplify(result);
            NormalizeAngles(result);
            Simplify(result);
            return result;
        }

        private static void Simplify(List<Vector2> points)
        {
            for (int i = points.Count - 2; i >= 1; i--)
            {
                // Keep the port stubs straight: they are the first and last real segments.
                if (i == 1 || i == points.Count - 2) continue;

                Vector2 a = points[i - 1], b = points[i], c = points[i + 1];
                Vector2 span = c - a;
                float length = span.magnitude;

                if (length < 0.01f)
                {
                    points.RemoveAt(i);
                    continue;
                }

                float deviation = Mathf.Abs((b.x - a.x) * span.y - (b.y - a.y) * span.x) / length;
                if (deviation < CollinearTolerance) points.RemoveAt(i);
            }
        }

        /// <summary>Splits any leftover free-angle segment into a 45° part plus an orthogonal part.</summary>
        private static void NormalizeAngles(List<Vector2> points)
        {
            const float epsilon = 0.6f;
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector2 a = points[i], b = points[i + 1];
                float dx = b.x - a.x, dy = b.y - a.y;
                float adx = Mathf.Abs(dx), ady = Mathf.Abs(dy);

                if (adx < epsilon || ady < epsilon || Mathf.Abs(adx - ady) < epsilon) continue;

                float run = Mathf.Min(adx, ady);
                var mid = new Vector2(a.x + Mathf.Sign(dx) * run, a.y + Mathf.Sign(dy) * run);
                points.Insert(i + 1, mid);
                i++;
            }
        }

        private static void AddPoint(List<Vector2> points, Vector2 point)
        {
            if (points.Count > 0 && (points[points.Count - 1] - point).sqrMagnitude < 0.01f) return;
            points.Add(point);
        }

        private static Rect NodeRect(Port port, Vector2 fallback)
        {
            var node = port != null ? port.node : null;
            if (node == null) return new Rect(fallback, Vector2.zero);

            Rect rect = node.layout;
            if (rect.width <= 1f || rect.height <= 1f) rect = node.GetPosition();
            return rect;
        }

        private static Rect Inflate(Rect rect, float amount)
        {
            rect.xMin -= amount;
            rect.yMin -= amount;
            rect.xMax += amount;
            rect.yMax += amount;
            return rect;
        }
    }
}
