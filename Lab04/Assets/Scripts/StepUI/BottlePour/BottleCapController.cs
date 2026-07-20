using UnityEngine;
using System.Collections;


public class BottleCapController : MonoBehaviour
{

    public Transform cap;


    public float rotateAngle = 90f;


    public float speed = 2f;


    // Z 轴移动的偏移量（可在 Inspector 中调整）
    public float positionOffset = 0.1f;



    private Quaternion closeRotation;
    private Vector3 closePosition;



    void Start()
    {
        closeRotation = cap.localRotation;
        closePosition = cap.localPosition;
    }



    public void OpenCap()
    {

        StartCoroutine(Open());

    }




    IEnumerator Open()
    {


        Quaternion target =
        Quaternion.Euler(
            0,
            rotateAngle,
            -90);

        Vector3 targetPosition = closePosition + new Vector3(0f, 0f, -positionOffset);



        float t = 0;


        while (t < 1)
        {

            t += Time.deltaTime * speed;


            cap.localRotation =
            Quaternion.Lerp(
                closeRotation,
                target,
                t);

            cap.localPosition =
                Vector3.Lerp(
                    closePosition,
                    targetPosition,
                    t);


            yield return null;

        }

    }



    public void CloseCap()
    {

        StartCoroutine(Close());

    }



    IEnumerator Close()
    {

        Quaternion target =
        Quaternion.Euler(
            0,
            rotateAngle,
            90f);

        Vector3 targetPosition = closePosition + new Vector3(0f, -positionOffset, 0f);


        float t = 0;


        while (t < 1)
        {

            t += Time.deltaTime * speed;


            cap.localRotation =
            Quaternion.Lerp(
                target,
                closeRotation,
                t);

            cap.localPosition =
                Vector3.Lerp(
                    targetPosition,
                    closePosition,
                    t);


            yield return null;

        }

    }

}