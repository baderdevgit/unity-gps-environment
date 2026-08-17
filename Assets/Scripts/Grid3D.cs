using UnityEngine;

public class Grid3D : MonoBehaviour
{
    [SerializeField] private DataReceiver receiver;

    public int columns = 5;
    public int rows = 5;
    public float cellSize = 0.5f;
    public float lineWidth = 0.02f;
    public Material lineMaterial;

    void Awake()
    {
        if (receiver == null)
            receiver = FindObjectOfType<DataReceiver>();
    }

    void OnEnable()
    {
        if (receiver != null)
            receiver.OnGridSizeChanged += HandleGridSizeChanged;
    }

    void OnDisable()
    {
        if (receiver != null)
            receiver.OnGridSizeChanged -= HandleGridSizeChanged;
    }

    void Start()
    {
        Generate();
    }

    // Rebuilds the grid at a new cell count (from the mobile UI's grid size
    // slider). LineRenderer point counts aren't resizable in place, so this
    // just clears the old lines and redraws from scratch.
    private void HandleGridSizeChanged(int cells)
    {
        columns = rows = Mathf.Max(1, cells);
        Generate();
    }

    private void Generate()
    {
        ClearLines();

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

    private void ClearLines()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);
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