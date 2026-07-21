using UnityEngine;

[ExecuteInEditMode]
public class SpiralTubeGenerator : MonoBehaviour
{
    public int coils = 9;
    public float totalHeight = 3.5f;
    public float spiralRadius = 0.25f;
    public float tubeRadius = 0.06f;
    public int segmentsPerCoil = 24;

    [ContextMenu("Éú³ÉÂÝÐý¹Ü")]
    void BuildSpiral()
    {
        while (transform.childCount > 0)
            DestroyImmediate(transform.GetChild(0).gameObject);

        int totalSegments = coils * segmentsPerCoil;
        float halfH = totalHeight * 0.5f;

        for (int i = 0; i < totalSegments; i++)
        {
            float t1 = (float)i / totalSegments;
            float t2 = (float)(i + 1) / totalSegments;
            float a1 = t1 * coils * Mathf.PI * 2f;
            float a2 = t2 * coils * Mathf.PI * 2f;
            float y1 = Mathf.Lerp(-halfH, halfH, t1);
            float y2 = Mathf.Lerp(-halfH, halfH, t2);

            float x1 = Mathf.Cos(a1) * spiralRadius;
            float z1 = Mathf.Sin(a1) * spiralRadius;
            float x2 = Mathf.Cos(a2) * spiralRadius;
            float z2 = Mathf.Sin(a2) * spiralRadius;

            Vector3 p1 = new Vector3(x1, y1, z1);
            Vector3 p2 = new Vector3(x2, y2, z2);

            GameObject seg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            seg.name = "Seg_" + i;
            seg.transform.SetParent(transform);
            seg.transform.localPosition = (p1 + p2) * 0.5f;


            float len = Vector3.Distance(p1, p2);
            seg.transform.localScale = new Vector3(tubeRadius / 0.5f, len / 2f, tubeRadius / 0.5f);
            seg.transform.rotation = Quaternion.FromToRotation(Vector3.up, (p2 - p1).normalized);
        }
    }
}
