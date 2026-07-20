using UnityEngine;
using System.Collections;


public class BottlePour : MonoBehaviour
{

    public Transform bottle;

    public Transform target;

    public GameObject liquidStream;

    public Transform targetLiquid;

    public float rotateAngle = 70f;

    public float speed = 2f;



    private Quaternion startRotation;



    void Start()
    {
        startRotation = bottle.localRotation;
    }



    public void Pour()
    {
        StartCoroutine(PourAnimation());
    }



    IEnumerator PourAnimation()
    {

        //瓶子移动到分液漏斗上方

        //Vector3 start =
        //bottle.position;


        //Vector3 end =
        //target.position;



        //float t = 0;


        //while (t < 1)
        //{

        //    t += Time.deltaTime * speed;


        //    bottle.position =
        //    Vector3.Lerp(
        //    start,
        //    end,
        //    t);


            yield return null;

        //}



        //倾斜

    //    Quaternion targetRot =
    //    Quaternion.Euler(
    //    0,
    //    0,
    //    rotateAngle);



    //    t = 0;


    //    while (t < 1)
    //    {

    //        t += Time.deltaTime * speed;


    //        bottle.localRotation =
    //        Quaternion.Lerp(
    //        startRotation,
    //        targetRot,
    //        t);


    //        yield return null;

    //    }



    //    //显示液流

    //    if (liquidStream)
    //    {
    //        liquidStream.SetActive(true);
    //    }



    //    yield return new WaitForSeconds(2);



    //    //增加液体

    //    if (targetLiquid)
    //    {

    //        targetLiquid.localScale +=
    //        new Vector3(
    //        0,
    //        0.2f,
    //        0);

    //    }



    //    if (liquidStream)
    //    {
    //        liquidStream.SetActive(false);
    //    }



    //    //恢复瓶子

    //    t = 0;


    //    while (t < 1)
    //    {

    //        t += Time.deltaTime * speed;


    //        bottle.localRotation =
    //        Quaternion.Lerp(
    //        targetRot,
    //        startRotation,
    //        t);


    //        yield return null;

    //    }


    }

}