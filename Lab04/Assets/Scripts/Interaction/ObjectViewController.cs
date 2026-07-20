using UnityEngine;
using System.Collections;
/*
挂到所有可观察仪器
功能：
✅ 点击进入观察
✅ 平滑移动
✅ 自动聚焦
✅ 放大
✅ 点击其他仪器直接切换
✅ ESC恢复
✅ 永远恢复实验台初始视角
 */

public class ObjectViewController : MonoBehaviour
{


    public float viewDistance = 1.5f;


    public float moveSpeed = 3f;


    public float zoom = 1.3f;



    private Camera cam;



    private static Vector3 homePosition;

    private static Quaternion homeRotation;


    private static bool savedHome = false;



    private static ObjectViewController current;



    private bool viewing = false;



    private Coroutine moveRoutine;



    private Outline outline;



    void Start()
    {

        cam =
        Camera.main;


        outline =
        GetComponent<Outline>();

    }



    void Update()
    {

        if (Input.GetKeyDown(KeyCode.Escape)
           &&
           viewing)
        {
            ExitView();
        }

    }




    void OnMouseDown()
    {


        if (current != null &&
           current != this)
        {

            current.viewing = false;

        }



        if (viewing)
        {
            ExitView();
            return;
        }



        EnterView();

    }





    void EnterView()
    {


        if (!savedHome)
        {

            homePosition =
            cam.transform.position;


            homeRotation =
            cam.transform.rotation;


            savedHome = true;

        }



        current = this;


        viewing = true;



        Vector3 dir =
        (
            cam.transform.position
            -
            transform.position

        ).normalized;



        Vector3 target =
        transform.position
        +
        dir * viewDistance;



        if (moveRoutine != null)
            StopCoroutine(moveRoutine);



        moveRoutine =
        StartCoroutine(
            MoveCamera(target)
        );


    }





    IEnumerator MoveCamera(Vector3 target)
    {


        Vector3 start =
        cam.transform.position;



        Quaternion startRot =
        cam.transform.rotation;



        Vector3 focus =
        transform.position;



        Vector3 dir =
        (target - focus).normalized;



        Vector3 finalPos =
        focus
        +
        dir *
        (viewDistance / zoom);



        Quaternion finalRot =
        Quaternion.LookRotation(
            focus - finalPos);



        float t = 0;



        while (t < 1)
        {

            t += Time.deltaTime * moveSpeed;



            cam.transform.position =
            Vector3.Lerp(
                start,
                finalPos,
                t);



            cam.transform.rotation =
            Quaternion.Slerp(
                startRot,
                finalRot,
                t);



            yield return null;

        }

    }




    void ExitView()
    {


        viewing = false;


        current = null;



        if (moveRoutine != null)
            StopCoroutine(moveRoutine);



        moveRoutine =
        StartCoroutine(
            Restore()
        );

    }




    IEnumerator Restore()
    {


        Vector3 start =
        cam.transform.position;


        Quaternion rot =
        cam.transform.rotation;



        float t = 0;



        while (t < 1)
        {

            t += Time.deltaTime * moveSpeed;



            cam.transform.position =
            Vector3.Lerp(
                start,
                homePosition,
                t);



            cam.transform.rotation =
            Quaternion.Slerp(
                rot,
                homeRotation,
                t);



            yield return null;

        }


    }


}