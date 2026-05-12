using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public enum WallPainterDragPlane
{
    XZ,
    XY
}

public enum WallPainterHierarchyMode
{
    VisibleIndividuals,
    HiddenIndividuals,
    StrokeParents
}

public class WallPainterWindow : EditorWindow
{
    private const string HelpText = "Left mouse drag in Scene view to paint. Hold Shift while dragging to erase.";

    [SerializeField] private GameObject wallAsset;
    [SerializeField] private Transform parent;
    [SerializeField] private LayerMask paintSurfaceMask = ~0;
    [SerializeField] private float gridSize = 1f;
    [SerializeField] private float yOffset;
    [SerializeField] private Vector3 rotationOffset;
    [SerializeField] private Vector3 scale = Vector3.one;
    [SerializeField] private bool alignToSurfaceNormal;
    [SerializeField] private bool useGroundPlaneWhenNoSurface = true;
    [SerializeField] private float groundPlaneY;
    [SerializeField] private bool autoRotateFromDragDirection;
    [SerializeField] private WallPainterDragPlane dragPlane = WallPainterDragPlane.XZ;
    [SerializeField] private float xAxisYaw = 90f;
    [SerializeField] private float zOrYAxisYaw = 0f;
    [SerializeField] private bool fillGapsWhileDragging = true;
    [SerializeField] private bool skipExistingSceneWalls = true;
    [SerializeField] private bool lockPaintHeight;
    [SerializeField] private bool overlapCornerOnTurn = true;
    [SerializeField] private WallPainterHierarchyMode hierarchyMode = WallPainterHierarchyMode.VisibleIndividuals;

    private readonly HashSet<Vector3Int> paintedCells = new HashSet<Vector3Int>();
    private readonly HashSet<string> paintedCornerOverlaps = new HashSet<string>();
    private bool paintingEnabled;
    private Vector3 previewPosition;
    private Vector3 previewNormal = Vector3.up;
    private bool hasPreview;
    private bool hasStrokeCell;
    private Vector3Int lastStrokeCell;
    private Vector3Int lastDirection = Vector3Int.forward;
    private int strokeIndex;
    private Transform currentStrokeParent;

    [MenuItem("Tools/Reflex/Wall Painter")]
    public static void Open()
    {
        GetWindow<WallPainterWindow>("Wall Painter");
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Wall Painter", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(HelpText, MessageType.Info);

        wallAsset = (GameObject)EditorGUILayout.ObjectField("Wall Model/Prefab", wallAsset, typeof(GameObject), false);
        parent = (Transform)EditorGUILayout.ObjectField("Parent", parent, typeof(Transform), true);
        paintSurfaceMask = LayerMaskField("Paint Surface Mask", paintSurfaceMask);

        EditorGUILayout.Space(6f);
        gridSize = Mathf.Max(0.01f, EditorGUILayout.FloatField("Grid Size", gridSize));
        yOffset = EditorGUILayout.FloatField("Y Offset", yOffset);
        rotationOffset = EditorGUILayout.Vector3Field("Rotation Offset", rotationOffset);
        scale = EditorGUILayout.Vector3Field("Scale", scale);
        alignToSurfaceNormal = EditorGUILayout.Toggle("Align To Surface Normal", alignToSurfaceNormal);
        useGroundPlaneWhenNoSurface = EditorGUILayout.Toggle("Use Ground Plane Fallback", useGroundPlaneWhenNoSurface);

        using (new EditorGUI.DisabledScope(!useGroundPlaneWhenNoSurface))
        {
            groundPlaneY = EditorGUILayout.FloatField("Ground Plane Y", groundPlaneY);
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Drag Painting", EditorStyles.boldLabel);
        autoRotateFromDragDirection = EditorGUILayout.Toggle("Auto Rotate From Drag", autoRotateFromDragDirection);
        fillGapsWhileDragging = EditorGUILayout.Toggle("Fill Drag Gaps", fillGapsWhileDragging);
        skipExistingSceneWalls = EditorGUILayout.Toggle("Skip Existing Scene Walls", skipExistingSceneWalls);
        lockPaintHeight = EditorGUILayout.Toggle("Lock Paint Height", lockPaintHeight);
        overlapCornerOnTurn = EditorGUILayout.Toggle("Overlap Corner On Turn", overlapCornerOnTurn);
        hierarchyMode = (WallPainterHierarchyMode)EditorGUILayout.EnumPopup("Hierarchy Mode", hierarchyMode);

        using (new EditorGUI.DisabledScope(!autoRotateFromDragDirection))
        {
            dragPlane = (WallPainterDragPlane)EditorGUILayout.EnumPopup("Drag Plane", dragPlane);
            xAxisYaw = EditorGUILayout.FloatField("X Axis Yaw", xAxisYaw);
            zOrYAxisYaw = EditorGUILayout.FloatField(dragPlane == WallPainterDragPlane.XZ ? "Z Axis Yaw" : "Y Axis Yaw", zOrYAxisYaw);
        }

        EditorGUILayout.Space(6f);
        using (new EditorGUI.DisabledScope(wallAsset == null))
        {
            string buttonText = paintingEnabled ? "Stop Painting" : "Start Painting";
            if (GUILayout.Button(buttonText, GUILayout.Height(28f)))
            {
                paintingEnabled = !paintingEnabled;
                ResetStroke();
                SceneView.RepaintAll();
            }
        }

        if (GUILayout.Button("Clear Brush Memory"))
        {
            paintedCells.Clear();
            paintedCornerOverlaps.Clear();
            ResetStroke();
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Hide Painted Children"))
        {
            SetPaintedChildrenHidden(true);
        }

        if (GUILayout.Button("Reveal Painted Children"))
        {
            SetPaintedChildrenHidden(false);
        }
        EditorGUILayout.EndHorizontal();

        if (wallAsset == null)
        {
            EditorGUILayout.HelpBox("Assign an FBX/model or prefab first. Your Big Wall FBX can go here.", MessageType.Warning);
        }
    }

    private void OnSceneGUI(SceneView sceneView)
    {
        if (!paintingEnabled || wallAsset == null)
        {
            return;
        }

        Event currentEvent = Event.current;
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        if (TryGetPaintPoint(currentEvent.mousePosition, out Vector3 point, out Vector3 normal))
        {
            previewPosition = SnapToGrid(point) + Vector3.up * yOffset;
            previewNormal = normal;
            hasPreview = true;
            DrawPreview();
        }
        else
        {
            hasPreview = false;
        }

        bool isPaintEvent = currentEvent.type == EventType.MouseDown || currentEvent.type == EventType.MouseDrag;
        if (isPaintEvent && currentEvent.button == 0 && hasPreview)
        {
            Vector3Int currentCell = GetCell(previewPosition);
            if (currentEvent.shift)
            {
                EraseStrokeTo(currentCell);
            }
            else
            {
                PaintStrokeTo(currentCell, previewNormal);
            }

            currentEvent.Use();
        }

        if (currentEvent.type == EventType.MouseUp && currentEvent.button == 0)
        {
            ResetStroke();
        }

        sceneView.Repaint();
    }

    private bool TryGetPaintPoint(Vector2 mousePosition, out Vector3 point, out Vector3 normal)
    {
        Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);

        if (Physics.Raycast(ray, out RaycastHit hit, 1000f, paintSurfaceMask))
        {
            point = hit.point;
            normal = hit.normal;
            return true;
        }

        if (useGroundPlaneWhenNoSurface)
        {
            Plane plane = new Plane(Vector3.up, new Vector3(0f, groundPlaneY, 0f));
            if (plane.Raycast(ray, out float enter))
            {
                point = ray.GetPoint(enter);
                normal = Vector3.up;
                return true;
            }
        }

        point = Vector3.zero;
        normal = Vector3.up;
        return false;
    }

    private void PaintAt(Vector3 position, Vector3 normal)
    {
        PaintAt(position, normal, lastDirection);
    }

    private void PaintAt(Vector3 position, Vector3 normal, Vector3Int direction)
    {
        Vector3Int cell = GetCell(position);
        if (!paintedCells.Add(cell))
        {
            return;
        }

        if (skipExistingSceneWalls && FindWallAtCell(cell) != null)
        {
            return;
        }

        GameObject instance = InstantiateWallAsset();
        Undo.RegisterCreatedObjectUndo(instance, "Paint Wall");

        instance.transform.SetParent(GetPlacementParent(), true);
        instance.transform.position = position;
        instance.transform.rotation = GetRotation(normal, direction);
        instance.transform.localScale = scale;
        instance.name = wallAsset.name;
        ApplyHierarchyMode(instance);
    }

    private void PaintCornerOverlapAt(Vector3Int cell, Vector3 normal, Vector3Int direction)
    {
        string cornerKey = GetCornerKey(cell, direction);
        if (!paintedCornerOverlaps.Add(cornerKey))
        {
            return;
        }

        GameObject instance = InstantiateWallAsset();
        Undo.RegisterCreatedObjectUndo(instance, "Paint Wall Corner");

        instance.transform.SetParent(GetPlacementParent(), true);
        instance.transform.position = CellToWorld(cell);
        instance.transform.rotation = GetRotation(normal, direction);
        instance.transform.localScale = scale;
        instance.name = $"{wallAsset.name} Corner";
        ApplyHierarchyMode(instance);
    }

    private GameObject InstantiateWallAsset()
    {
        GameObject prefabInstance = PrefabUtility.InstantiatePrefab(wallAsset) as GameObject;
        if (prefabInstance != null)
        {
            return prefabInstance;
        }

        return Instantiate(wallAsset);
    }

    private void EraseAt(Vector3 position)
    {
        Vector3Int cell = GetCell(position);
        List<GameObject> walls = FindWallsAtCell(cell);
        foreach (GameObject wall in walls)
        {
            Undo.DestroyObjectImmediate(wall);
        }

        paintedCells.Remove(cell);
        RemoveCornerKeysForCell(cell);
    }

    private GameObject FindWallAtCell(Vector3Int cell)
    {
        List<GameObject> walls = FindWallsAtCell(cell);
        return walls.Count > 0 ? walls[0] : null;
    }

    private List<GameObject> FindWallsAtCell(Vector3Int cell)
    {
        Vector3 center = CellToWorld(cell);
        float radius = gridSize * 0.45f;
        List<GameObject> walls = new List<GameObject>();

        foreach (GameObject candidate in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (!candidate.scene.IsValid() || candidate == null)
            {
                continue;
            }

            if (!IsPaintedWallName(candidate.name))
            {
                continue;
            }

            float distance = Vector3.Distance(candidate.transform.position, center);
            if (distance <= radius)
            {
                walls.Add(candidate);
            }
        }

        return walls;
    }

    private bool IsPaintedWallName(string candidateName)
    {
        return candidateName == wallAsset.name ||
               candidateName == $"{wallAsset.name}(Clone)" ||
               candidateName == $"{wallAsset.name} Corner" ||
               candidateName == $"{wallAsset.name} Corner(Clone)";
    }

    private void PaintStrokeTo(Vector3Int currentCell, Vector3 normal)
    {
        if (!hasStrokeCell)
        {
            BeginStrokeParent();
            PaintAt(CellToWorld(currentCell), normal, lastDirection);
            lastStrokeCell = currentCell;
            hasStrokeCell = true;
            return;
        }

        List<Vector3Int> cells = fillGapsWhileDragging ? BuildCellsBetween(lastStrokeCell, currentCell) : new List<Vector3Int> { currentCell };
        foreach (Vector3Int cell in cells)
        {
            Vector3Int direction = GetDominantDirection(cell - lastStrokeCell);
            if (direction != Vector3Int.zero)
            {
                if (overlapCornerOnTurn && lastDirection != Vector3Int.zero && direction != lastDirection)
                {
                    PaintCornerOverlapAt(lastStrokeCell, normal, direction);
                }

                lastDirection = direction;
            }

            PaintAt(CellToWorld(cell), normal, lastDirection);
            lastStrokeCell = cell;
        }
    }

    private void EraseStrokeTo(Vector3Int currentCell)
    {
        if (!hasStrokeCell)
        {
            EraseAt(CellToWorld(currentCell));
            lastStrokeCell = currentCell;
            hasStrokeCell = true;
            return;
        }

        List<Vector3Int> cells = fillGapsWhileDragging ? BuildCellsBetween(lastStrokeCell, currentCell) : new List<Vector3Int> { currentCell };
        foreach (Vector3Int cell in cells)
        {
            EraseAt(CellToWorld(cell));
            lastStrokeCell = cell;
        }
    }

    private List<Vector3Int> BuildCellsBetween(Vector3Int start, Vector3Int end)
    {
        List<Vector3Int> cells = new List<Vector3Int>();
        Vector3Int current = start;
        int guard = 0;

        while (current != end && guard < 512)
        {
            Vector3Int delta = end - current;
            Vector3Int step = GetDominantDirection(delta);
            if (step == Vector3Int.zero)
            {
                break;
            }

            current += step;
            cells.Add(current);
            guard++;
        }

        return cells;
    }

    private Vector3Int GetDominantDirection(Vector3Int delta)
    {
        if (dragPlane == WallPainterDragPlane.XY)
        {
            if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y) && delta.x != 0)
            {
                return new Vector3Int(delta.x > 0 ? 1 : -1, 0, 0);
            }

            if (delta.y != 0)
            {
                return new Vector3Int(0, delta.y > 0 ? 1 : -1, 0);
            }

            return Vector3Int.zero;
        }

        if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.z) && delta.x != 0)
        {
            return new Vector3Int(delta.x > 0 ? 1 : -1, 0, 0);
        }

        if (delta.z != 0)
        {
            return new Vector3Int(0, 0, delta.z > 0 ? 1 : -1);
        }

        return Vector3Int.zero;
    }

    private void ResetStroke()
    {
        hasStrokeCell = false;
        currentStrokeParent = null;
    }

    private void BeginStrokeParent()
    {
        if (hierarchyMode != WallPainterHierarchyMode.StrokeParents || currentStrokeParent != null)
        {
            return;
        }

        GameObject strokeObject = new GameObject($"Wall Stroke {++strokeIndex:000}");
        Undo.RegisterCreatedObjectUndo(strokeObject, "Create Wall Stroke Parent");
        strokeObject.transform.SetParent(parent, true);
        currentStrokeParent = strokeObject.transform;
    }

    private Transform GetPlacementParent()
    {
        if (hierarchyMode == WallPainterHierarchyMode.StrokeParents)
        {
            BeginStrokeParent();
            return currentStrokeParent;
        }

        return parent;
    }

    private void ApplyHierarchyMode(GameObject instance)
    {
        instance.hideFlags = hierarchyMode == WallPainterHierarchyMode.HiddenIndividuals ? HideFlags.HideInHierarchy : HideFlags.None;
    }

    private string GetCornerKey(Vector3Int cell, Vector3Int direction)
    {
        return $"{cell.x}:{cell.y}:{cell.z}:{direction.x}:{direction.y}:{direction.z}";
    }

    private void RemoveCornerKeysForCell(Vector3Int cell)
    {
        List<string> keysToRemove = new List<string>();
        string prefix = $"{cell.x}:{cell.y}:{cell.z}:";

        foreach (string key in paintedCornerOverlaps)
        {
            if (key.StartsWith(prefix))
            {
                keysToRemove.Add(key);
            }
        }

        foreach (string key in keysToRemove)
        {
            paintedCornerOverlaps.Remove(key);
        }
    }

    private void SetPaintedChildrenHidden(bool hidden)
    {
        foreach (GameObject candidate in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (!candidate.scene.IsValid() || candidate == null)
            {
                continue;
            }

            if (wallAsset != null && !IsPaintedWallName(candidate.name))
            {
                continue;
            }

            if (parent != null && !candidate.transform.IsChildOf(parent))
            {
                continue;
            }

            candidate.hideFlags = hidden ? HideFlags.HideInHierarchy : HideFlags.None;
            EditorUtility.SetDirty(candidate);
        }

        EditorApplication.RepaintHierarchyWindow();
    }

    private Vector3 SnapToGrid(Vector3 point)
    {
        Vector3 snapped = new Vector3(
            Mathf.Round(point.x / gridSize) * gridSize,
            Mathf.Round(point.y / gridSize) * gridSize,
            Mathf.Round(point.z / gridSize) * gridSize
        );

        if (lockPaintHeight)
        {
            snapped.y = groundPlaneY;
        }

        return snapped;
    }

    private Vector3Int GetCell(Vector3 point)
    {
        return new Vector3Int(
            Mathf.RoundToInt(point.x / gridSize),
            Mathf.RoundToInt(point.y / gridSize),
            Mathf.RoundToInt(point.z / gridSize)
        );
    }

    private Vector3 CellToWorld(Vector3Int cell)
    {
        return new Vector3(cell.x * gridSize, cell.y * gridSize, cell.z * gridSize);
    }

    private Quaternion GetRotation(Vector3 normal, Vector3Int direction)
    {
        Quaternion baseRotation = alignToSurfaceNormal ? Quaternion.FromToRotation(Vector3.up, normal) : Quaternion.identity;
        Quaternion dragRotation = autoRotateFromDragDirection ? Quaternion.Euler(GetDragRotation(direction)) : Quaternion.identity;
        return baseRotation * dragRotation * Quaternion.Euler(rotationOffset);
    }

    private Vector3 GetDragRotation(Vector3Int direction)
    {
        bool isXAxis = Mathf.Abs(direction.x) > 0;
        bool isSecondaryAxis = dragPlane == WallPainterDragPlane.XZ ? Mathf.Abs(direction.z) > 0 : Mathf.Abs(direction.y) > 0;

        if (isXAxis)
        {
            return new Vector3(0f, xAxisYaw, 0f);
        }

        if (isSecondaryAxis)
        {
            return new Vector3(0f, zOrYAxisYaw, 0f);
        }

        return Vector3.zero;
    }

    private void DrawPreview()
    {
        Handles.color = new Color(0.2f, 0.8f, 1f, 0.9f);
        Handles.DrawWireCube(previewPosition, Vector3.one * gridSize);
        Vector3 arrowDirection = GetPreviewDirection();
        Handles.ArrowHandleCap(0, previewPosition, Quaternion.LookRotation(arrowDirection), gridSize * 0.5f, EventType.Repaint);
    }

    private Vector3 GetPreviewDirection()
    {
        if (!autoRotateFromDragDirection)
        {
            return previewNormal;
        }

        if (lastDirection == Vector3Int.zero)
        {
            return Vector3.forward;
        }

        return new Vector3(lastDirection.x, lastDirection.y, lastDirection.z).normalized;
    }

    private LayerMask LayerMaskField(string label, LayerMask selected)
    {
        List<string> layerNames = new List<string>();
        List<int> layerNumbers = new List<int>();

        for (int i = 0; i < 32; i++)
        {
            string layerName = LayerMask.LayerToName(i);
            if (!string.IsNullOrEmpty(layerName))
            {
                layerNames.Add(layerName);
                layerNumbers.Add(i);
            }
        }

        int maskWithoutEmpty = 0;
        for (int i = 0; i < layerNumbers.Count; i++)
        {
            if (((1 << layerNumbers[i]) & selected.value) != 0)
            {
                maskWithoutEmpty |= 1 << i;
            }
        }

        maskWithoutEmpty = EditorGUILayout.MaskField(label, maskWithoutEmpty, layerNames.ToArray());

        int mask = 0;
        for (int i = 0; i < layerNumbers.Count; i++)
        {
            if ((maskWithoutEmpty & (1 << i)) != 0)
            {
                mask |= 1 << layerNumbers[i];
            }
        }

        selected.value = mask;
        return selected;
    }
}
