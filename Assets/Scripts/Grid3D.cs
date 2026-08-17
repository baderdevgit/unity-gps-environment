using UnityEngine;

public class Grid3D : MonoBehaviour
{
    public int columns = 5;
    public int rows = 5;
    public float cellSize = 0.5f;
    public float lineWidth = 0.02f;
    public Material lineMaterial;

    void Start()
    {
        float width = columns * cellSize;
        float height = rows * cellSize;
        // Local space, centered on this object - the parent transform's
        // position/rotation is applied automatically every frame (see
        // DrawLine's useWorldSpace = false), so the grid follows this
        // object's rotation live with no need to redraw the lines.
        Vector3 origin = new Vector3(-width / 2f, 0, -height / 2f);

        for (int x = 0; x <= columns; x++)
        {
            Vector3 start = origin + new Vector3(x * cellSize, 0, 0);
            Vector3 end = start + new Vector3(0, 0, height);
            DrawLine(start, end);
        }

        for (int z = 0; z <= rows; z++)
        {
            Vector3 start = origin + new Vector3(0, 0, z * cellSize);
            Vector3 end = start + new Vector3(width, 0, 0);
            DrawLine(start, end);
        }
    }

    void DrawLine(Vector3 start, Vector3 end)
    {
        GameObject lineObj = new GameObject("GridLine");
        lineObj.transform.SetParent(transform, false); // false = reset local transform to identity
        LineRenderer lr = lineObj.AddComponent<LineRenderer>();
        lr.material = lineMaterial;
        lr.startColor = lr.endColor = Color.black;
        lr.startWidth = lr.endWidth = lineWidth;
        lr.positionCount = 2;
        lr.useWorldSpace = false;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);
    }
}